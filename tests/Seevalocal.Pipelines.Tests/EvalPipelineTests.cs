using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Xunit;

namespace Seevalocal.Pipelines.Tests;

public sealed class EvalPipelineTests
{
    [Fact]
    public async Task RunItemAsync_AllStagesSucceed_ReturnsSucceededResult()
    {
        var stage1 = new FakeStage("StageA", succeeded: true,
            outputs: new Dictionary<string, object?> { ["StageA.x"] = 42 },
            metrics: [new MetricValue { Name = "xCount", Value = new MetricScalar.IntMetric(42) }]);

        var stage2 = new FakeStage("StageB", succeeded: true,
            outputs: new Dictionary<string, object?> { ["StageB.y"] = "hello" },
            metrics: []);

        var pipeline = new EvalPipeline(NullLogger<EvalPipeline>.Instance)
        {
            PipelineName = "Test",
            Stages = [stage1, stage2],
        };

        var ctx = TestHelpers.MakeContext();
        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: true);

        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
        _ = Assert.Single(result.Metrics);
        Assert.Equal(42, ((MetricScalar.IntMetric)result.Metrics[0].Value).Value);
        Assert.Equal(42, result.AllStageOutputs["StageA.x"]);
        Assert.Equal("hello", result.AllStageOutputs["StageB.y"]);
    }

    [Fact]
    public async Task RunItemAsync_FirstStageFails_ContinueOnFailureFalse_SkipsRemainingStages()
    {
        var stage1 = new FakeStage("StageA", succeeded: false);
        var stage2 = new TrackingStage("StageB");

        var pipeline = new EvalPipeline(NullLogger<EvalPipeline>.Instance)
        {
            PipelineName = "Test",
            Stages = [stage1, stage2],
        };

        var ctx = TestHelpers.MakeContext();
        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: false);

        Assert.False(result.Succeeded);
        Assert.False(stage2.Executed);
    }

    [Fact]
    public async Task RunItemAsync_FirstStageFails_ContinueOnFailureTrue_ExecutesRemainingStages()
    {
        var stage1 = new FakeStage("StageA", succeeded: false);
        var stage2 = new TrackingStage("StageB");

        var pipeline = new EvalPipeline(NullLogger<EvalPipeline>.Instance)
        {
            PipelineName = "Test",
            Stages = [stage1, stage2],
        };

        var ctx = TestHelpers.MakeContext();
        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: true);

        Assert.False(result.Succeeded);
        Assert.True(stage2.Executed);
    }

    [Fact]
    public async Task RunItemAsync_PromptStageResponse_SetsRawLlmResponse()
    {
        var stage = new FakeStage("PromptStage", succeeded: true,
            outputs: new Dictionary<string, object?> { ["PromptStage.response"] = "The answer is 42." });

        var pipeline = new EvalPipeline(NullLogger<EvalPipeline>.Instance) { PipelineName = "Test", Stages = [stage] };
        var ctx = TestHelpers.MakeContext();
        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: true);

        Assert.Equal("The answer is 42.", result.RawLlmResponse);
    }

    [Fact]
    public async Task RunItemAsync_StageThrowsException_ReturnsFailureWithoutRethrowing()
    {
        var throwingStage = new ThrowingStage("BrokenStage");
        var pipeline = new EvalPipeline(NullLogger<EvalPipeline>.Instance) { PipelineName = "Test", Stages = [throwingStage] };

        var ctx = TestHelpers.MakeContext();
        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: false);

        // Should not throw; instead returns failure
        Assert.False(result.Succeeded);
        Assert.Contains("Unhandled exception", result.FailureReason);
    }

    [Fact]
    public async Task RunItemAsync_RecordsEvalItemId()
    {
        var pipeline = new EvalPipeline(NullLogger<EvalPipeline>.Instance) { PipelineName = "Test", Stages = [] };
        var item = TestHelpers.MakeItem(id: "item-007");
        var ctx = TestHelpers.MakeContext(item: item);

        var result = await pipeline.RunItemAsync(ctx, continueOnStageFailure: true);

        Assert.Equal("item-007", result.EvalItemId);
    }

    [Fact]
    public async Task RunItemAsync_PreviousStageOutputsAreVisibleToSubsequentStages()
    {
        var stage1 = new FakeStage("StageA", succeeded: true,
            outputs: new Dictionary<string, object?> { ["StageA.value"] = "from-a" });

        var capturingStage = new CapturingStage("StageB");

        var pipeline = new EvalPipeline(NullLogger<EvalPipeline>.Instance) { PipelineName = "Test", Stages = [stage1, capturingStage] };
        var ctx = TestHelpers.MakeContext();
        _ = await pipeline.RunItemAsync(ctx, continueOnStageFailure: true);

        Assert.Equal("from-a", capturingStage.CapturedOutputs?.GetValueOrDefault("StageA.value") as string);
    }

    // ---- Fake stages ----

    private sealed class FakeStage(
        string name,
        bool succeeded,
        Dictionary<string, object?>? outputs = null,
        List<MetricValue>? metrics = null) : IEvalStage
    {
        public string StageName => name;

        public Task<StageResult> ExecuteAsync(EvalStageContext context)
        {
            return !succeeded
                ? Task.FromResult(StageResult.Failure($"{name} failed by design"))
                : Task.FromResult(StageResult.Success(
                outputs ?? [],
                metrics ?? []));
        }
    }

    private sealed class TrackingStage(string name) : IEvalStage
    {
        public string StageName => name;
        public bool Executed { get; private set; }

        public Task<StageResult> ExecuteAsync(EvalStageContext context)
        {
            Executed = true;
            return Task.FromResult(StageResult.Success(new Dictionary<string, object?>(), []));
        }
    }

    private sealed class ThrowingStage(string name) : IEvalStage
    {
        public string StageName => name;

        public Task<StageResult> ExecuteAsync(EvalStageContext context)
            => throw new InvalidOperationException("Intentional exception");
    }

    private sealed class CapturingStage(string name) : IEvalStage
    {
        public string StageName => name;
        public IReadOnlyDictionary<string, object?>? CapturedOutputs { get; private set; }

        public Task<StageResult> ExecuteAsync(EvalStageContext context)
        {
            CapturedOutputs = context.StageOutputs;
            return Task.FromResult(StageResult.Success(new Dictionary<string, object?>(), []));
        }
    }
}
