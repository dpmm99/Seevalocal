using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core.Models;
using Seevalocal.DataSources.Sources;
using Seevalocal.Server.Client;
using Seevalocal.Server.Models;
using Xunit;

namespace Seevalocal.Core.Pipeline.Tests;

public class PipelineOrchestratorTests
{
    private static EvalPipeline MakePipeline(params IEvalStage[] stages) =>
        new(NullLogger<EvalPipeline>.Instance)
        {
            PipelineName = "TestPipeline",
            Stages = stages
        };

    private static PipelineOrchestrator MakeOrchestrator(
        IEnumerable<EvalItem> items,
        EvalPipeline pipeline,
        IResultCollector collector,
        IProgress<EvalProgress>? progress = null)
    {
        var serverInfo = new ServerInfo
        {
            BaseUrl = "http://localhost:8080",
            TotalSlots = 4
        };
        var primaryClient = new LlamaServerClient(
            serverInfo,
            new HttpClient(),
            NullLogger<LlamaServerClient>.Instance);

        return new PipelineOrchestrator(
            dataSource: new ListDataSource(items),
            pipeline: pipeline,
            evalSetConfig: new EvalSetConfig { Id = "test-set" },
            resolvedConfig: TestHelpers.DefaultConfig(),
            primaryClient: primaryClient,
            judgeClient: null,
            resultCollector: collector,
            progress: progress ?? new NoOpProgress(),
            logger: NullLogger<PipelineOrchestrator>.Instance);
    }

    [Fact]
    public async Task RunAsync_AllItemsProcessed()
    {
        var items = Enumerable.Range(1, 10)
            .Select(static i => TestHelpers.MakeItem($"item-{i:D6}", $"Prompt {i}"))
            .ToList();

        var collector = new CapturingResultCollector();
        var pipeline = MakePipeline(new SucceedingStage());
        var orchestrator = MakeOrchestrator(items, pipeline, collector);

        await orchestrator.RunAsync(maxConcurrentCount: 3, ct: CancellationToken.None);

        Assert.Equal(10, collector.GetResults().Count);
    }

    [Fact(Skip = "Flaky test - timing dependent")]
    public async Task RunAsync_ConcurrencyLimitRespected()
    {
        var maxObserved = 0;
        var current = 0;
        var lockObj = new object();

        var items = Enumerable.Range(1, 20)
            .Select(i => TestHelpers.MakeItem($"item-{i:D6}", $"Prompt {i}"))
            .ToList();

        var trackingStage = new TrackingConcurrencyStage(() =>
        {
            lock (lockObj)
            {
                current++;
                if (current > maxObserved) maxObserved = current;
            }
        }, () =>
        {
            lock (lockObj) { current--; }
        });

        var collector = new CapturingResultCollector();
        var pipeline = MakePipeline(trackingStage);
        var orchestrator = MakeOrchestrator(items, pipeline, collector);

        await orchestrator.RunAsync(maxConcurrentCount: 4, ct: CancellationToken.None);

        // Items should all be processed by now since RunAsync waits for all tasks
        // But add a small safety delay for any final collection operations
        await Task.Delay(100);

        Assert.True(maxObserved <= 4, $"Max concurrent was {maxObserved}, expected ≤ 4");
        Assert.Equal(items.Count, collector.GetResults().Count);  // All items should be processed
    }

    [Fact]
    public async Task RunAsync_CancellationMidRun_StopsGracefully()
    {
        using var cts = new CancellationTokenSource();

        var items = Enumerable.Range(1, 100)
            .Select(i => TestHelpers.MakeItem($"item-{i:D6}", $"Prompt {i}"))
            .ToList();

        var processedCount = 0;
        var slowStage = new SlowStage(delayMs: 20, onExecute: () => Interlocked.Increment(ref processedCount));

        var collector = new CapturingResultCollector();
        var pipeline = MakePipeline(slowStage);
        var orchestrator = MakeOrchestrator(items, pipeline, collector);

        // Cancel after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            cts.Cancel();
        });

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            orchestrator.RunAsync(maxConcurrentCount: 2, ct: cts.Token));

        // Some items should have been processed before cancellation
        Assert.True(processedCount < 100, "Expected cancellation to stop before all 100 items");
    }

    [Fact(Skip = "Flaky test - timing dependent on async processing")]
    public async Task RunAsync_ProgressReported()
    {
        List<EvalProgress> progressReports = [];
        var progress = new DelegatingProgress<EvalProgress>(progressReports.Add);

        var items = Enumerable.Range(1, 5)
            .Select(i => TestHelpers.MakeItem($"item-{i:D6}", $"Prompt {i}"))
            .ToList();

        var collector = new CapturingResultCollector();
        var pipeline = MakePipeline(new SucceedingStage());
        var orchestrator = MakeOrchestrator(items, pipeline, collector, progress);

        await orchestrator.RunAsync(maxConcurrentCount: 2, ct: CancellationToken.None);

        Assert.Equal(5, progressReports.Count);
        Assert.All(progressReports, p => Assert.True(p.CompletedCount > 0));
    }

    // ── Test stage helpers ────────────────────────────────────────────────────

    private sealed class TrackingConcurrencyStage(Action onEnter, Action onExit) : IEvalStage
    {
        private readonly Action _onEnter = onEnter;
        private readonly Action _onExit = onExit;
        public string StageName => "TrackingStage";

        public async Task<StageResult> ExecuteAsync(EvalStageContext context)
        {
            _onEnter();
            try
            {
                await Task.Delay(10, context.CancellationToken);
                return StageResult.Success(new Dictionary<string, object?>(), []);
            }
            finally
            {
                _onExit();
            }
        }
    }

    private sealed class SlowStage(int delayMs, Action onExecute) : IEvalStage
    {
        private readonly int _delayMs = delayMs;
        private readonly Action _onExecute = onExecute;
        public string StageName => "SlowStage";

        public async Task<StageResult> ExecuteAsync(EvalStageContext context)
        {
            _onExecute();
            await Task.Delay(_delayMs, context.CancellationToken);
            return StageResult.Success(new Dictionary<string, object?>(), []);
        }
    }

    private sealed class NoOpProgress : IProgress<EvalProgress>
    {
        public void Report(EvalProgress value) { }
    }

    private sealed class DelegatingProgress<T>(Action<T> handler) : IProgress<T>
    {
        private readonly Action<T> _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
