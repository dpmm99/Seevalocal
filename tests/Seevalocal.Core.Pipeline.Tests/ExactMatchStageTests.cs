using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline.Stages;
using Xunit;

namespace Seevalocal.Core.Pipeline.Tests;

public class ExactMatchStageTests
{
    private static ExactMatchStage MakeStage(bool caseSensitive = false, bool trimWhitespace = true) =>
        new(NullLogger<ExactMatchStage>.Instance)
        {
            CaseSensitive = caseSensitive,
            TrimWhitespace = trimWhitespace
        };

    private static EvalStageContext MakeCtxWithResponse(
        string? expectedOutput,
        string llmResponse,
        string stageKey = "PromptStage.response") =>
        TestHelpers.MakeContext(
            item: TestHelpers.MakeItem(expectedOutput: expectedOutput),
            stageOutputs: new Dictionary<string, object?> { [stageKey] = llmResponse });

    // ── Matching ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExactMatch_SameString_ReturnsTrue()
    {
        var stage = MakeStage();
        var ctx = MakeCtxWithResponse("Hello world", "Hello world");
        var result = await stage.ExecuteAsync(ctx);
        Assert.True(result.Succeeded);
        AssertExactMatch(result, true);
    }

    [Fact]
    public async Task ExactMatch_DifferentStrings_ReturnsFalse()
    {
        var stage = MakeStage();
        var ctx = MakeCtxWithResponse("Hello world", "Goodbye");
        var result = await stage.ExecuteAsync(ctx);
        Assert.True(result.Succeeded);
        AssertExactMatch(result, false);
    }

    [Fact]
    public async Task ExactMatch_CaseInsensitive_MatchesIgnoringCase()
    {
        var stage = MakeStage(caseSensitive: false);
        var ctx = MakeCtxWithResponse("HELLO", "hello");
        var result = await stage.ExecuteAsync(ctx);
        AssertExactMatch(result, true);
    }

    [Fact]
    public async Task ExactMatch_CaseSensitive_FailsOnDifferentCase()
    {
        var stage = MakeStage(caseSensitive: true);
        var ctx = MakeCtxWithResponse("HELLO", "hello");
        var result = await stage.ExecuteAsync(ctx);
        AssertExactMatch(result, false);
    }

    [Fact]
    public async Task ExactMatch_TrimWhitespace_MatchesAfterTrim()
    {
        var stage = MakeStage(trimWhitespace: true);
        var ctx = MakeCtxWithResponse("answer", "  answer  ");
        var result = await stage.ExecuteAsync(ctx);
        AssertExactMatch(result, true);
    }

    [Fact]
    public async Task ExactMatch_NoTrimWhitespace_FailsWhenPadded()
    {
        var stage = MakeStage(trimWhitespace: false);
        var ctx = MakeCtxWithResponse("answer", "  answer  ");
        var result = await stage.ExecuteAsync(ctx);
        AssertExactMatch(result, false);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExactMatch_NoExpectedOutput_ReturnsSuccessWithSkipped()
    {
        var stage = MakeStage();
        var ctx = MakeCtxWithResponse(expectedOutput: null, llmResponse: "anything");
        var result = await stage.ExecuteAsync(ctx);
        Assert.True(result.Succeeded);
        Assert.Empty(result.Metrics); // no exactMatch metric emitted when skipped
        Assert.True(result.Outputs.TryGetValue("ExactMatchStage.skipped", out var skipped) && (bool)skipped!);
    }

    [Fact]
    public async Task ExactMatch_MissingStageOutput_ReturnsFailure()
    {
        var stage = MakeStage();
        var ctx = TestHelpers.MakeContext(item: TestHelpers.MakeItem(expected: "expected"));
        // no PromptStage.response in StageOutputs
        var result = await stage.ExecuteAsync(ctx);
        Assert.False(result.Succeeded);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AssertExactMatch(StageResult result, bool expected)
    {
        var metric = result.Metrics.SingleOrDefault(static m => m.Name == "exactMatch");
        Assert.NotNull(metric);
        Assert.Equal(expected, ((MetricScalar.BoolMetric)metric.Value).Value);
    }
}
