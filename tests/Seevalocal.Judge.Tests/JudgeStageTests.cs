using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.Judge.Tests;

public class JudgeStageTests
{
    private static JudgeConfig MakeConfig(
        JudgeResponseFormat format = JudgeResponseFormat.StructuredJson,
        string? systemPrompt = null) =>
        new()
        {
            ResponseFormat = format,
            ScoreMinValue = 0,
            ScoreMaxValue = 10,
            JudgeMaxTokenCount = 512,
            JudgeSystemPrompt = systemPrompt,
            JudgePromptTemplate = DefaultTemplates.StructuredJson,
        };

    private static EvalItem MakeItem(string id = "item-1") =>
        new()
        {
            Id = id,
            UserPrompt = "Translate 'hello' to French.",
            ExpectedOutput = "Bonjour",
        };

    private static ILlamaServerClient CreateMockClient()
    {
        var mockClient = Substitute.For<ILlamaServerClient>();
        return mockClient;
    }

    private static EvalStageContext MakeContext(
        ILlamaServerClient judgeClient,
        string actualOutput = "Bonjour",
        EvalItem? item = null) =>
        new()
        {
            Item = item ?? MakeItem(),
            Config = new ResolvedConfig(),
            PrimaryClient = CreateMockClient(),
            JudgeClient = judgeClient,
            StageOutputs = new Dictionary<string, object?> { ["PromptStage.response"] = actualOutput },
            CancellationToken = CancellationToken.None,
        };

    private static JudgeStage MakeStage(JudgeConfig? config = null) =>
        new(
            config ?? MakeConfig(),
            NullLogger<JudgeStage>.Instance,
            NullLogger<JudgePromptRenderer>.Instance,
            NullLogger<JudgeResponseParser>.Instance);

    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidJsonResponse_ProducesCorrectMetrics()
    {
        var judgeClientMock = Substitute.For<ILlamaServerClient>();
        _ = judgeClientMock
            .ChatCompletionAsync(Arg.Any<ChatCompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new ChatCompletionResponse
            {
                Choices = [new ChatChoice { Message = new ChatMessage { Content = """{"rationale":"correct","score":9,"passed":true}""" } }]
            }));

        var stage = MakeStage();
        var result = await stage.ExecuteAsync(MakeContext(judgeClientMock));

        _ = result.Succeeded.Should().BeTrue();

        // Metrics should now be prefixed with "judge."
        var scoreMetric = result.Metrics.Should().ContainSingle(static m => m.Name == "judge.score").Subject;
        _ = ((MetricScalar.DoubleMetric)scoreMetric.Value).Value.Should().BeApproximately(9.0, 1e-9);

        var passedMetric = result.Metrics.Should().ContainSingle(static m => m.Name == "judge.passed").Subject;
        _ = ((MetricScalar.BoolMetric)passedMetric.Value).Value.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_JudgeClientError_ReturnsFailedResult()
    {
        var judgeClientMock = Substitute.For<ILlamaServerClient>();
        _ = judgeClientMock
            .ChatCompletionAsync(Arg.Any<ChatCompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<ChatCompletionResponse>("Connection refused"));

        var stage = MakeStage();
        var result = await stage.ExecuteAsync(MakeContext(judgeClientMock));

        _ = result.Succeeded.Should().BeFalse();
        _ = result.FailureReason.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task ExecuteAsync_ParseFailure_ReturnsFailedResult()
    {
        var judgeClientMock = Substitute.For<ILlamaServerClient>();
        _ = judgeClientMock
            .ChatCompletionAsync(Arg.Any<ChatCompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new ChatCompletionResponse
            {
                Choices = [new ChatChoice { Message = new ChatMessage { Content = "I think it is fine." } }]
            }));

        // StructuredJson format cannot parse "I think it is fine."
        var stage = MakeStage(MakeConfig(JudgeResponseFormat.StructuredJson));
        var result = await stage.ExecuteAsync(MakeContext(judgeClientMock));

        _ = result.Succeeded.Should().BeFalse();
        _ = result.FailureReason.Should().Contain("parse judge response");
    }

    [Fact]
    public async Task ExecuteAsync_NullJudgeClient_Throws()
    {
        var context = new EvalStageContext
        {
            Item = MakeItem(),
            Config = new ResolvedConfig(),
            PrimaryClient = CreateMockClient(),
            JudgeClient = null,          // ← intentionally null
            CancellationToken = CancellationToken.None,
        };

        var stage = MakeStage();
        Func<Task<StageResult>> act = async () => await stage.ExecuteAsync(context);

        _ = await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*JudgeStage requires a judge client*");
    }

    [Fact]
    public async Task ExecuteAsync_MissingPromptStageOutput_SkipsItem()
    {
        var judgeClientMock = Substitute.For<ILlamaServerClient>();

        // StageOutputs does NOT contain PromptStage.response
        var context = new EvalStageContext
        {
            Item = MakeItem(),
            Config = new ResolvedConfig(),
            PrimaryClient = CreateMockClient(),
            JudgeClient = judgeClientMock,
            StageOutputs = new Dictionary<string, object?>(),
            CancellationToken = CancellationToken.None,
        };

        var stage = MakeStage();
        var result = await stage.ExecuteAsync(context);

        // Should fail (skip) when prompt stage output is missing
        _ = result.Succeeded.Should().BeFalse();
        _ = result.FailureReason.Should().Contain("primary LLM output is missing or empty");

        // Judge client should NOT have been called
        await judgeClientMock.DidNotReceive().ChatCompletionAsync(Arg.Any<ChatCompletionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Outputs_ContainAllExpectedKeys()
    {
        var judgeClientMock = Substitute.For<ILlamaServerClient>();
        _ = judgeClientMock
            .ChatCompletionAsync(Arg.Any<ChatCompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new ChatCompletionResponse
            {
                Choices = [new ChatChoice { Message = new ChatMessage { Content = """{"rationale":"great","score":10,"passed":true}""" } }]
            }));

        var stage = MakeStage();
        var result = await stage.ExecuteAsync(MakeContext(judgeClientMock));

        // Outputs should contain rawResponse and rationale
        _ = result.Outputs.Keys.Should().Contain([
            "JudgeStage.rawResponse",
            "JudgeStage.rationale",
        ]);
        _ = result.Outputs["JudgeStage.rationale"].Should().Be("great");

        // Metrics should contain judge.score and judge.passed
        var scoreMetric = result.Metrics.FirstOrDefault(m => m.Name == "judge.score");
        var passedMetric = result.Metrics.FirstOrDefault(m => m.Name == "judge.passed");

        _ = scoreMetric.Should().NotBeNull();
        _ = passedMetric.Should().NotBeNull();
        _ = scoreMetric!.Value.ToString().Should().Be("10");
        _ = passedMetric!.Value.ToString().Should().Be("true");
    }
}
