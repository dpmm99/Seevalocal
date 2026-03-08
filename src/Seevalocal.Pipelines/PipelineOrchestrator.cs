using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Server.Client;
using System.Threading.Channels;

namespace Seevalocal.Pipelines;

/// <summary>
/// Drains a <see cref="IDataSource"/> through an <see cref="EvalPipeline"/> with bounded concurrency.
/// Progress events are published for each completed item.
/// </summary>
public sealed class PipelineOrchestrator( //TODO: Deduplicate the two PipelineOrchestrator
    EvalPipeline pipeline,
    IDataSource dataSource,
    IResultCollector collector,
    ResolvedConfig config,
    LlamaServerClient primaryClient,
    LlamaServerClient? judgeClient,
    ILogger<PipelineOrchestrator> logger)
{
    private readonly EvalPipeline _pipeline = pipeline;
    private readonly IDataSource _dataSource = dataSource;
    private readonly IResultCollector _collector = collector;
    private readonly ResolvedConfig _config = config;
    private readonly LlamaServerClient _primaryClient = primaryClient;
    private readonly LlamaServerClient? _judgeClient = judgeClient;
    private readonly ILogger<PipelineOrchestrator> _logger = logger;

    public event Action<EvalProgress>? ProgressChanged;

    /// <summary>
    /// Run the full pipeline. Returns all collected results.
    /// </summary>
    public async Task<IReadOnlyList<EvalResult>> RunAsync(
        int maxConcurrentEvals,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Pipeline '{PipelineName}' starting. MaxConcurrency={MaxConcurrency}",
            _pipeline.PipelineName, maxConcurrentEvals);

        var totalCount = await _dataSource.GetCountAsync(ct);
        var overallSw = System.Diagnostics.Stopwatch.StartNew();
        var completedCount = 0;
        var completedTokensPerSecondSum = 0.0;
        var completedWithTokens = 0;

        var channel = Channel.CreateUnbounded<EvalItem>(
            new UnboundedChannelOptions { SingleWriter = true });

        ProgressChanged?.Invoke(new EvalProgress
        {
            EvalItemId = "",
            Succeeded = false,
            CompletedCount = 0,
            TotalCount = totalCount ?? -1,
            ElapsedSeconds = 0,
            EstimatedRemainingSeconds = 60 * (totalCount ?? 0),
            AverageCompletionTokensPerSecond = 0
        });

        // Producer: stream items into channel
        //TODO: What if there are gigs of eval items? Don't want to waste the memory on that; that's ridiculous when you can just load them as you go. Does this channel fill up some amount and then wait for spaces to free up? That'd be the best thing to do. That's probably what CreateBounded(capacity) is for...
        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in _dataSource.GetItemsAsync(ct))
                    await channel.Writer.WriteAsync(item, ct);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        var semaphore = new SemaphoreSlim(maxConcurrentEvals, maxConcurrentEvals);
        List<Task> consumerTasks = [];

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            await semaphore.WaitAsync(ct);

            var capturedItem = item;
            consumerTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var context = new EvalStageContext
                    {
                        Item = capturedItem,
                        Config = _config,
                        PrimaryClient = _primaryClient,
                        JudgeClient = _judgeClient,
                        CancellationToken = ct
                    };

                    _logger.LogDebug(
                        "Pipeline '{PipelineName}' starting item '{ItemId}'",
                        _pipeline.PipelineName, capturedItem.Id);

                    var continueOnStageFailure = _config.Run?.ContinueOnEvalFailure ?? true;
                    var result = await _pipeline.RunItemAsync(
                        context,
                        continueOnStageFailure,
                        evalSetId: "");

                    await _collector.CollectAsync(result, ct);

                    var done = Interlocked.Increment(ref completedCount);

                    // Estimate tokens/s for progress (optional)
                    var tpsMetric = result.Metrics
                        .FirstOrDefault(m => m.Name == "promptTokensPerSecond");

                    double? avgTps = null;
                    if (tpsMetric?.Value is MetricScalar.DoubleMetric d)
                    {
                        completedWithTokens++;
                        completedTokensPerSecondSum += d.Value;
                        avgTps = completedTokensPerSecondSum / completedWithTokens;
                    }

                    var elapsed = overallSw.Elapsed.TotalSeconds;
                    double? remaining = totalCount.HasValue && done > 0
                        ? (elapsed / done) * (totalCount.Value - done)
                        : null;

                    ProgressChanged?.Invoke(new EvalProgress
                    {
                        EvalItemId = capturedItem.Id,
                        Succeeded = result.Succeeded,
                        CompletedCount = done,
                        TotalCount = totalCount ?? -1,
                        ElapsedSeconds = elapsed,
                        EstimatedRemainingSeconds = remaining,
                        AverageCompletionTokensPerSecond = avgTps
                    });

                    _logger.LogDebug(
                        "Pipeline '{PipelineName}' completed item '{ItemId}' in {DurationSeconds:F2}s. Succeeded={Succeeded}",
                        _pipeline.PipelineName, capturedItem.Id, result.DurationSeconds, result.Succeeded);
                }
                finally
                {
                    _ = semaphore.Release();
                }
            }, ct));
        }

        await producer;
        await Task.WhenAll(consumerTasks);
        await _collector.FinalizeAsync(ct);

        overallSw.Stop();
        _logger.LogInformation(
            "Pipeline '{PipelineName}' finished. {CompletedCount} items in {DurationSeconds:F2}s",
            _pipeline.PipelineName, completedCount, overallSw.Elapsed.TotalSeconds);

        return _collector.GetResults();
    }
}
