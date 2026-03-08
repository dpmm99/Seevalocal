using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.Core.Pipeline.Tests;

public class EvalPipelineTests
{
    private static EvalPipeline MakePipeline(string name, params IEvalStage[] stages) =>
        new(NullLogger<EvalPipeline>.Instance)
        {
            PipelineName = name,
            Stages = stages
        };

    // ── Sequential execution ──────────────────────────────────────────────────

    [Fact]
    public async Task RunItemAsync_AllStagesSucceed_ReturnsSucceededResult()
    {
        var s1 = new SucceedingStage("Stage1", new Dictionary<string, object?> { ["Stage1.out"] = "a" });
        var s2 = new SucceedingStage("Stage2", new Dictionary<string, object?> { ["Stage2.out"] = "b" });

        var pipeline = MakePipeline("Test", s1, s2);
        var ctx = TestHelpers.MakeContext();

        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: false, evalSetId: "set1");

        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
        Assert.Equal("a", result.AllStageOutputs["Stage1.out"]);
        Assert.Equal("b", result.AllStageOutputs["Stage2.out"]);
        Assert.Equal(1, s1.CallCount);
        Assert.Equal(1, s2.CallCount);
    }

    [Fact]
    public async Task RunItemAsync_StageFailure_StopsSubsequentStagesWhenContinueIsFalse()
    {
        var s1 = new FailingStage("FailStage");
        var s2 = new SucceedingStage("AfterFailStage");

        var pipeline = MakePipeline("Test", s1, s2);
        var ctx = TestHelpers.MakeContext();

        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: false, evalSetId: "set1");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureReason);
        Assert.Equal(1, s1.CallCount);
        Assert.Equal(0, s2.CallCount); // skipped
    }

    [Fact]
    public async Task RunItemAsync_StageFailure_ContinuesSubsequentStagesWhenContinueIsTrue()
    {
        var s1 = new FailingStage("FailStage");
        var s2 = new SucceedingStage("AfterFailStage");

        var pipeline = MakePipeline("Test", s1, s2);
        var ctx = TestHelpers.MakeContext();

        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: true, evalSetId: "set1");

        Assert.False(result.Succeeded);
        Assert.Equal(1, s1.CallCount);
        Assert.Equal(1, s2.CallCount); // still ran
    }

    // ── Context propagation ───────────────────────────────────────────────────

    [Fact]
    public async Task RunItemAsync_StageOutputsAccumulateAndAreVisibleToLaterStages()
    {
        // s2 checks that it can see s1's output
        string? seenValue = null;
        var s1 = new SucceedingStage("S1", new Dictionary<string, object?> { ["S1.value"] = "hello" });
        var s2 = new CaptureStageOutputStage("S2", "S1.value", v => seenValue = v as string);

        var pipeline = MakePipeline("Test", s1, s2);
        var ctx = TestHelpers.MakeContext();

        _ = await pipeline.RunItemAsync(ctx, continueOnStageFailure: false, evalSetId: "set1");

        Assert.Equal("hello", seenValue);
    }

    // ── Metric collection ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunItemAsync_CollectsMetricsFromAllStages()
    {
        var m1 = new MetricValue { Name = "latencySeconds", Value = new MetricScalar.DoubleMetric(1.5) };
        var m2 = new MetricValue { Name = "tokenCount", Value = new MetricScalar.IntMetric(100) };

        var s1 = new SucceedingStage("S1", metrics: [m1]);
        var s2 = new SucceedingStage("S2", metrics: [m2]);

        var pipeline = MakePipeline("Test", s1, s2);
        var ctx = TestHelpers.MakeContext();

        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: false, evalSetId: "set1");

        Assert.Contains(result.Metrics, static m => m.Name == "latencySeconds");
        Assert.Contains(result.Metrics, static m => m.Name == "tokenCount");
    }

    // ── RawLlmResponse ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunItemAsync_SetsRawLlmResponseFromPromptStageOutput()
    {
        var stage = new SucceedingStage("PromptStage",
            new Dictionary<string, object?> { ["PromptStage.response"] = "The answer is 42" });

        var pipeline = MakePipeline("Test", stage);
        var ctx = TestHelpers.MakeContext();

        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: false, evalSetId: "set1");

        Assert.Equal("The answer is 42", result.RawLlmResponse);
    }

    // ── Unhandled exception ───────────────────────────────────────────────────

    [Fact]
    public async Task RunItemAsync_UnhandledStageException_ReturnedAsFailure()
    {
        var pipeline = MakePipeline("Test", new ThrowingStage());
        var ctx = TestHelpers.MakeContext();

        // Should never throw; exception is captured into the result
        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: false, evalSetId: "set1");

        Assert.False(result.Succeeded);
        Assert.Contains("Unhandled exception", result.FailureReason);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunItemAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var slowStage = new CancellingStage();
        var pipeline = MakePipeline("Test", slowStage);
        var ctx = TestHelpers.MakeContext(cancellationToken: cts.Token);

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.RunItemAsync(ctx, continueOnStageFailure: false, evalSetId: "set1"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class CaptureStageOutputStage(string name, string key, Action<object?> capture) : IEvalStage
    {
        private readonly string _key = key;
        private readonly Action<object?> _capture = capture;
        public string StageName { get; } = name;

        public Task<StageResult> ExecuteAsync(EvalStageContext context)
        {
            _ = context.StageOutputs.TryGetValue(_key, out var val);
            _capture(val);
            return Task.FromResult(StageResult.Success(
                new Dictionary<string, object?>(), []));
        }
    }

    private sealed class CancellingStage : IEvalStage
    {
        public string StageName => "CancellingStage";

        public Task<StageResult> ExecuteAsync(EvalStageContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(StageResult.Success(new Dictionary<string, object?>(), []));
        }
    }
}
