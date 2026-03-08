using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core.Models;
using Seevalocal.DataSources.Sources;
using Seevalocal.Server;
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
        IResultCollector collector)
    {
        var serverInfo = new ServerInfo
        {
            BaseUrl = "http://localhost:8080",
            TotalSlots = 4
        };

        // Use a mock HTTP handler that always succeeds
        var handler = new AlwaysSuccessHttpHandler();
        var primaryClient = new LlamaServerClient(
            serverInfo,
            new HttpClient(handler),
            NullLogger<LlamaServerClient>.Instance,
            maxConcurrentRequests: 10);

        return new PipelineOrchestrator(
            pipeline: pipeline,
            dataSource: new ListDataSource(items),
            collector: collector,
            config: TestHelpers.DefaultConfig(),
            primaryClient: primaryClient,
            judgeClient: null,
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

        await orchestrator.RunAsync(maxConcurrentEvals: 3, ct: CancellationToken.None);

        Assert.Equal(10, collector.GetResults().Count);
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
            orchestrator.RunAsync(maxConcurrentEvals: 2, ct: cts.Token));

        // Some items should have been processed before cancellation
        Assert.True(processedCount < 100, "Expected cancellation to stop before all 100 items");
    }

    // ── LlamaServerClient Semaphore Tests ─────────────────────────────────────

    [Fact]
    public async Task LlamaServerClient_SemaphoreLimitsConcurrentRequests()
    {
        var maxObserved = 0;
        var lockObj = new object();

        // Create a mock HTTP handler that tracks concurrent requests
        var concurrentRequests = 0;
        var handler = new TrackingHttpHandler(
            onEnter: () =>
            {
                lock (lockObj)
                {
                    concurrentRequests++;
                    if (concurrentRequests > maxObserved) maxObserved = concurrentRequests;
                }
            },
            onExit: () =>
            {
                lock (lockObj)
                {
                    concurrentRequests--;
                }
            });

        var serverInfo = new ServerInfo
        {
            BaseUrl = "http://localhost:8080",
            TotalSlots = 4
        };

        const int maxConcurrentRequests = 3;
        var client = new LlamaServerClient(
            serverInfo,
            new HttpClient(handler),
            NullLogger<LlamaServerClient>.Instance,
            maxConcurrentRequests: maxConcurrentRequests);

        // Make multiple concurrent requests
        var tasks = Enumerable.Range(1, 10).Select(_ =>
            client.ChatCompletionAsync(
                new ChatCompletionRequest
                {
                    Messages = [new ChatMessage { Role = "user", Content = "test" }]
                },
                CancellationToken.None)).ToList();

        // Ignore failures (mock will return errors, we just care about concurrency)
        await Task.WhenAll(tasks);

        // Small delay to ensure all onExit callbacks have run
        await Task.Delay(50);

        Assert.True(maxObserved <= maxConcurrentRequests,
            $"Max concurrent requests was {maxObserved}, expected ≤ {maxConcurrentRequests}");
    }

    [Fact]
    public async Task LlamaServerClient_SemaphoreReleasedAfterFailure()
    {
        var serverInfo = new ServerInfo
        {
            BaseUrl = "http://localhost:8080",
            TotalSlots = 4
        };

        // Create a handler that always fails
        var handler = new AlwaysFailHttpHandler();
        var client = new LlamaServerClient(
            serverInfo,
            new HttpClient(handler),
            NullLogger<LlamaServerClient>.Instance,
            maxConcurrentRequests: 2);

        // Make multiple requests that will fail
        var tasks = Enumerable.Range(1, 5).Select(_ =>
            client.ChatCompletionAsync(
                new ChatCompletionRequest
                {
                    Messages = [new ChatMessage { Role = "user", Content = "test" }]
                },
                CancellationToken.None)).ToList();

        // All should complete (with failures)
        var results = await Task.WhenAll(tasks);

        // All should have failed
        Assert.All(results, r => Assert.True(r.IsFailed));

        // Semaphore should have been released - make one more request to verify
        // If semaphore wasn't released, this would hang or fail differently
        var finalResult = await client.ChatCompletionAsync(
            new ChatCompletionRequest
            {
                Messages = [new ChatMessage { Role = "user", Content = "test" }]
            },
            CancellationToken.None);

        // Should still fail (handler always fails), but should not hang
        Assert.True(finalResult.IsFailed);
    }

    [Fact]
    public async Task LlamaServerClient_DisposeDisposesSemaphore()
    {
        var serverInfo = new ServerInfo
        {
            BaseUrl = "http://localhost:8080",
            TotalSlots = 4
        };

        var client = new LlamaServerClient(
            serverInfo,
            new HttpClient(),
            NullLogger<LlamaServerClient>.Instance,
            maxConcurrentRequests: 5);

        // Use the client
        _ = await client.ChatCompletionAsync(
            new ChatCompletionRequest
            {
                Messages = [new ChatMessage { Role = "user", Content = "test" }]
            },
            CancellationToken.None);

        // Dispose should not throw
        client.Dispose();

        // Second dispose should be safe (idempotent)
        client.Dispose();
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

    /// <summary>
    /// HTTP handler that always returns a successful response.
    /// </summary>
    private sealed class AlwaysSuccessHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                    "choices": [{
                        "message": {
                            "content": "mock response"
                        }
                    }],
                    "usage": {
                        "prompt_tokens": 10,
                        "completion_tokens": 20,
                        "total_tokens": 30
                    }
                }
                """)
            });
        }
    }

    /// <summary>
    /// HTTP handler that tracks concurrent requests.
    /// </summary>
    private sealed class TrackingHttpHandler(Action onEnter, Action onExit) : HttpMessageHandler
    {
        private readonly Action _onEnter = onEnter;
        private readonly Action _onExit = onExit;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _onEnter();
            try
            {
                // Simulate some network delay
                await Task.Delay(10, cancellationToken);

                // Return a mock response
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "choices": [{
                            "message": {
                                "content": "mock response"
                            }
                        }]
                    }
                    """)
                };
            }
            finally
            {
                _onExit();
            }
        }
    }

    /// <summary>
    /// HTTP handler that always returns an error.
    /// </summary>
    private sealed class AlwaysFailHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Mock failure")
            });
        }
    }
}
