using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline.Stages;
using Xunit;

namespace Seevalocal.Pipelines.Tests;

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
