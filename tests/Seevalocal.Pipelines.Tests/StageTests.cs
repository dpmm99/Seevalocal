using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline.Stages;
using Seevalocal.Judge;
using Xunit;

namespace Seevalocal.Pipelines.Tests;

public sealed class JudgeResponseParserTests
{
    [Theory]
    [InlineData("8", 10, 0.8, true)]
    [InlineData("6", 10, 0.6, true)]
    [InlineData("5", 10, 0.5, false)]
    [InlineData("10", 10, 1.0, true)]
    [InlineData("0", 10, 0.0, false)]
    [InlineData("7.5", 10, 0.75, true)]
    public void ParseScore_ExtractsCorrectRatioAndPassedFlag(
        string raw, int maxScore, double expectedRatio, bool expectedPassed)
    {
        var result = JudgeStage.ParseScore(raw, maxScore, passThreshold: 0.6);
        Assert.NotNull(result);
        Assert.Equal(expectedRatio, result!.ScoreRatio, precision: 5);
        Assert.Equal(expectedPassed, result.Passed);
    }

    [Theory]
    [InlineData("Score: 8 out of 10")]
    [InlineData("I'd give this a 7.")]
    [InlineData("The translation is good. 9")]
    public void ParseScore_ExtractsFirstNumber_FromNarrativeResponse(string raw)
    {
        var result = JudgeStage.ParseScore(raw, maxScore: 10, passThreshold: 0.6);
        Assert.NotNull(result);
    }

    [Fact]
    public void ParseScore_NoNumber_ReturnsNull()
    {
        var result = JudgeStage.ParseScore("The answer is great!", maxScore: 10, passThreshold: 0.6);
        Assert.Null(result);
    }

    [Fact]
    public void ParseScore_EmptyString_ReturnsNull()
    {
        var result = JudgeStage.ParseScore("", maxScore: 10, passThreshold: 0.6);
        Assert.Null(result);
    }
}

public sealed class ExactMatchStageTests
{
    private readonly ExactMatchStage _stage;

    public ExactMatchStageTests()
    {
        _stage = new ExactMatchStage(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExactMatchStage>.Instance,
            caseSensitive: false);
    }

    [Fact]
    public async Task Execute_MatchingResponse_EmitsExactMatchTrue()
    {
        var item = TestHelpers.MakeItem(expected: "Paris");
        var ctx = TestHelpers.MakeContext(
            item: item,
            outputs: new Dictionary<string, object?> { ["PromptStage.response"] = "paris" });

        var result = await _stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
        var metric = result.Metrics.First(static m => m.Name == "exactMatch");
        Assert.True(((MetricScalar.BoolMetric)metric.Value).Value);
    }

    [Fact]
    public async Task Execute_NonMatchingResponse_EmitsExactMatchFalse()
    {
        var item = TestHelpers.MakeItem(expected: "Paris");
        var ctx = TestHelpers.MakeContext(
            item: item,
            outputs: new Dictionary<string, object?> { ["PromptStage.response"] = "London" });

        var result = await _stage.ExecuteAsync(ctx);

        var metric = result.Metrics.First(static m => m.Name == "exactMatch");
        Assert.False(((MetricScalar.BoolMetric)metric.Value).Value);
    }

    [Fact]
    public async Task Execute_NoExpectedOutput_ReturnsSuccessWithSkipped()
    {
        var item = TestHelpers.MakeItem(expected: null);
        var ctx = TestHelpers.MakeContext(item: item);

        var result = await _stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Metrics); // no exactMatch metric emitted
        Assert.True(result.Outputs.TryGetValue("ExactMatchStage.skipped", out var skipped) && (bool)skipped!);
    }

    [Fact]
    public async Task Execute_WhitespaceNormalization_TreatsAsMatch()
    {
        var item = TestHelpers.MakeItem(expected: "  hello  ");
        var ctx = TestHelpers.MakeContext(
            item: item,
            outputs: new Dictionary<string, object?> { ["PromptStage.response"] = "hello" });

        var result = await _stage.ExecuteAsync(ctx);

        var metric = result.Metrics.First(static m => m.Name == "exactMatch");
        Assert.True(((MetricScalar.BoolMetric)metric.Value).Value);
    }
}
