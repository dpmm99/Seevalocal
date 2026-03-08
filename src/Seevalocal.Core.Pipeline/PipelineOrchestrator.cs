using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.Server.Client;
using System.Diagnostics;
using System.Threading.Channels;

namespace Seevalocal.Core.Pipeline;

/// <summary>
/// Orchestrates the evaluation of all items in a dataset.
/// Manages concurrency, retry, progress reporting, and result collection.
/// Includes resilience to llama-server crashes with automatic retry and circuit breaker.
/// Supports two-phase execution (primary evaluation, then judge evaluation) and checkpoint/resume.
/// </summary>
public sealed class PipelineOrchestrator(
    IDataSource dataSource,
    EvalPipeline pipeline,
    EvalSetConfig evalSetConfig,
    ResolvedConfig resolvedConfig,
    LlamaServerClient primaryClient,
    LlamaServerClient? judgeClient,
    IResultCollector resultCollector,
    IProgress<EvalProgress> progress,
    ILogger<PipelineOrchestrator> logger,
    HashSet<string>? completedItemIds = null,
    string phase = "primary")
{
    private readonly IDataSource _dataSource = dataSource;
    private readonly EvalPipeline _pipeline = pipeline;
    private readonly EvalSetConfig _evalSetConfig = evalSetConfig;
    private readonly ResolvedConfig _resolvedConfig = resolvedConfig;
    private readonly LlamaServerClient _primaryClient = primaryClient;
    private readonly LlamaServerClient? _judgeClient = judgeClient;
    private readonly IResultCollector _resultCollector = resultCollector;
    private readonly IProgress<EvalProgress> _progress = progress;
    private readonly ILogger<PipelineOrchestrator> _logger = logger;
    private readonly HashSet<string>? _completedItemIds = completedItemIds;  // Items already done in this phase
    private readonly string _phase = phase;  // "primary" or "judge"

    // Circuit breaker state for server crash resilience
    private int _consecutiveServerCrashes;
    private const int MaxConsecutiveServerCrashes = 5;  // Stop after 5 consecutive crashes
    private readonly object _crashLock = new();

    /// <summary>
    /// Run all items concurrently, up to maxConcurrentCount.
    /// Skips items that were already completed (checkpoint resume).
    /// Returns when all items are complete or cancellation is requested.
    /// </summary>
    public async Task RunAsync(int maxConcurrentCount, CancellationToken ct)
    {
        var runSw = Stopwatch.StartNew();

        var totalCount = await _dataSource.GetCountAsync(ct);
        var skippedCount = _completedItemIds?.Count ?? 0;
        var effectiveTotal = totalCount.HasValue ? totalCount.Value - skippedCount : totalCount;

        _logger.LogInformation(
            "Orchestrator starting: EvalSet={EvalSetId}, Pipeline={PipelineName}, Phase={Phase}, MaxConcurrent={MaxConcurrentCount}, TotalItems={TotalItems}, Skipped={SkippedCount}",
            _evalSetConfig.Id, _pipeline.PipelineName, _phase, maxConcurrentCount, effectiveTotal?.ToString() ?? "unknown", skippedCount);

        // Set result collector on pipeline for checkpoint saving
        if (_resultCollector is PersistentResultCollector persistentCollector)
        {
            _pipeline.ResultCollector = persistentCollector;
            
            // Save startup parameters for primary phase
            if (_phase == "primary")
            {
                await persistentCollector.SaveStartupParametersAsync(_resolvedConfig, ct);
            }
        }

        var channel = Channel.CreateUnbounded<EvalItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });

        // Track in-flight tasks to ensure all items are processed
        var processingTasks = new System.Collections.Concurrent.ConcurrentBag<Task>();

        // Producer: write all items from the data source to the channel, skipping completed ones
        var producerTask = Task.Run(async () =>
        {
            try
            {
                var itemsProcessed = 0;
                await foreach (var item in _dataSource.GetItemsAsync(ct))
                {
                    // Skip items already completed in this phase (checkpoint resume)
                    if (_completedItemIds?.Contains(item.Id) == true)
                    {
                        _logger.LogDebug("Skipping already completed item {EvalItemId} (phase={Phase})", item.Id, _phase);
                        continue;
                    }

                    await channel.Writer.WriteAsync(item, ct);
                    itemsProcessed++;
                    _logger.LogTrace("Queued item {EvalItemId} (#{Number})", item.Id, itemsProcessed);
                }
                channel.Writer.Complete();
                _logger.LogDebug("Producer finished writing {Count} items to channel (skipped {Skipped})", itemsProcessed, skippedCount);
            }
            catch (OperationCanceledException)
            {
                channel.Writer.Complete();
                _logger.LogInformation("Producer cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Producer encountered an error");
                channel.Writer.Complete(ex);
            }
        }, ct);

        // Consumers: limited by semaphore
        var semaphore = new SemaphoreSlim(maxConcurrentCount, maxConcurrentCount);

        // Use a shared counter for completed items
        var localCompletedCount = 0;

        var consumerTasks = Enumerable
            .Range(0, maxConcurrentCount)
            .Select(_ => ConsumeAsync(channel.Reader, semaphore, effectiveTotal, runSw, ct, () => Interlocked.Increment(ref localCompletedCount), processingTasks))
            .ToArray();

        await Task.WhenAll([producerTask, .. consumerTasks]);

        // Wait for all in-flight processing tasks to complete
        await Task.WhenAll(processingTasks);

        runSw.Stop();
        _logger.LogInformation(
            "Orchestrator finished: EvalSet={EvalSetId}, Phase={Phase}, Completed={CompletedCount}, Skipped={SkippedCount}, Elapsed={ElapsedSeconds:F2}s",
            _evalSetConfig.Id, _phase, localCompletedCount, skippedCount, runSw.Elapsed.TotalSeconds);
    }

    private async Task ConsumeAsync(
        ChannelReader<EvalItem> reader,
        SemaphoreSlim semaphore,
        int? totalCount,
        Stopwatch runSw,
        CancellationToken ct,
        Func<int> incrementCompleted,
        System.Collections.Concurrent.ConcurrentBag<Task> processingTasks)
    {
        await foreach (var item in reader.ReadAllAsync(ct))
        {
            await semaphore.WaitAsync(ct);

            // Create a task for this item's processing
            var processTask = ProcessItemAsync(item, incrementCompleted, runSw, totalCount, ct);
            processingTasks.Add(processTask);

            // Release semaphore when done (don't wait for processing to complete)
            _ = processTask.ContinueWith(_ => semaphore.Release(), ct, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private async Task ProcessItemAsync(EvalItem item, Func<int> incrementCompleted, Stopwatch runSw, int? totalCount, CancellationToken ct)
    {
        var result = await RunItemWithRetryAsync(item, ct);

        // Use phase-appropriate collection method
        if (_phase == "judge" && _resultCollector is PersistentResultCollector persistentCollector)
        {
            await persistentCollector.CollectJudgeResultAsync(result, ct);
        }
        else
        {
            await _resultCollector.CollectAsync(result, ct);
        }

        var completed = incrementCompleted();
        var elapsed = runSw.Elapsed.TotalSeconds;

        double? estimatedRemaining = null;
        if (totalCount > 0 && completed > 0)
        {
            var rate = completed / elapsed;
            var remaining = totalCount.Value - completed;
            estimatedRemaining = remaining > 0 ? remaining / rate : 0.0;
        }

        _progress.Report(new EvalProgress
        {
            EvalItemId = item.Id,
            Succeeded = result.Succeeded,
            CompletedCount = completed,
            TotalCount = totalCount ?? -1,
            ElapsedSeconds = elapsed,
            EstimatedRemainingSeconds = estimatedRemaining
        });
    }

    private async Task<EvalResult> RunItemWithRetryAsync(EvalItem item, CancellationToken ct)
    {
        var retryConfig = new RetryConfig
        {
            MaxRetryCount = 2,
            InitialDelaySeconds = 1.0,
            BackoffMultiplier = 2.0
        };

        var attempt = 0;
        var delaySeconds = retryConfig.InitialDelaySeconds;

        while (true)
        {
            // Check circuit breaker before each attempt
            if (_consecutiveServerCrashes >= MaxConsecutiveServerCrashes)
            {
                _logger.LogError(
                    "Circuit breaker triggered: {MaxCrashes} consecutive server crashes detected. Aborting run.",
                    MaxConsecutiveServerCrashes);

                return new EvalResult
                {
                    EvalItemId = item.Id,
                    EvalSetId = _evalSetConfig.Id,
                    Succeeded = false,
                    FailureReason = $"llama-server crashed {MaxConsecutiveServerCrashes} consecutive times. Server may be unstable or misconfigured.",
                    Metrics = [],
                    AllStageOutputs = new Dictionary<string, object?>(),
                    StartedAt = DateTimeOffset.UtcNow,
                    DurationSeconds = 0
                };
            }

            var context = new EvalStageContext
            {
                Item = item,
                Config = _resolvedConfig,
                PrimaryClient = _primaryClient,
                JudgeClient = _judgeClient,
                CancellationToken = ct
            };

            EvalResult result;
            try
            {
                result = await _pipeline.RunItemAsync(
                    context,
                    _resolvedConfig.Run.ContinueOnEvalFailure,
                    _evalSetConfig.Id);
            }
            catch (HttpRequestException httpEx) when (IsServerCrashException(httpEx))
            {
                // Server crashed during request
                lock (_crashLock)
                {
                    _consecutiveServerCrashes++;
                    _logger.LogWarning(
                        httpEx,
                        "Item {EvalItemId} encountered server crash (attempt {Attempt}). Consecutive crashes: {Consecutive}/{Max}",
                        item.Id, attempt + 1, _consecutiveServerCrashes, MaxConsecutiveServerCrashes);
                }

                if (_consecutiveServerCrashes >= MaxConsecutiveServerCrashes)
                    continue;  // Will be caught by circuit breaker check on next iteration

                // Retry with backoff
                if (attempt < retryConfig.MaxRetryCount)
                {
                    attempt++;
                    _logger.LogWarning(
                        "Retrying item {EvalItemId} after server crash (attempt {Attempt}/{MaxRetry}) in {DelaySeconds:F1}s",
                        item.Id, attempt, retryConfig.MaxRetryCount, delaySeconds);

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                    delaySeconds *= retryConfig.BackoffMultiplier;
                    continue;
                }

                // Max retries exceeded
                return new EvalResult
                {
                    EvalItemId = item.Id,
                    EvalSetId = _evalSetConfig.Id,
                    Succeeded = false,
                    FailureReason = $"llama-server crashed {retryConfig.MaxRetryCount + 1} times. Server may be unstable.",
                    Metrics = [],
                    AllStageOutputs = new Dictionary<string, object?>(),
                    StartedAt = DateTimeOffset.UtcNow,
                    DurationSeconds = 0
                };
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                // Other transient failures (timeout, connection reset, etc.)
                if (attempt < retryConfig.MaxRetryCount)
                {
                    attempt++;
                    _logger.LogWarning(
                        ex,
                        "Item {EvalItemId} failed transiently (attempt {Attempt}/{MaxRetry}): {Reason}. Retrying in {DelaySeconds:F1}s",
                        item.Id, attempt, retryConfig.MaxRetryCount, ex.Message, delaySeconds);

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                    delaySeconds *= retryConfig.BackoffMultiplier;
                    continue;
                }

                // Max retries exceeded
                return new EvalResult
                {
                    EvalItemId = item.Id,
                    EvalSetId = _evalSetConfig.Id,
                    Succeeded = false,
                    FailureReason = $"Transient failure after {retryConfig.MaxRetryCount + 1} attempts: {ex.Message}",
                    Metrics = [],
                    AllStageOutputs = new Dictionary<string, object?>(),
                    StartedAt = DateTimeOffset.UtcNow,
                    DurationSeconds = 0
                };
            }
            catch (Exception ex)
            {
                // Non-transient failure - don't retry
                _logger.LogError(ex, "Item {EvalItemId} failed with non-transient error", item.Id);
                return new EvalResult
                {
                    EvalItemId = item.Id,
                    EvalSetId = _evalSetConfig.Id,
                    Succeeded = false,
                    FailureReason = ex.Message,
                    Metrics = [],
                    AllStageOutputs = new Dictionary<string, object?>(),
                    StartedAt = DateTimeOffset.UtcNow,
                    DurationSeconds = 0
                };
            }

            // Success - reset consecutive crash counter
            if (result.Succeeded)
            {
                lock (_crashLock)
                {
                    if (_consecutiveServerCrashes > 0)
                    {
                        _logger.LogDebug("Item {EvalItemId} succeeded. Resetting consecutive crash counter (was {Consecutive}).",
                            item.Id, _consecutiveServerCrashes);
                        _consecutiveServerCrashes = 0;
                    }
                }
                return result;
            }

            // Logic/scoring failure - don't retry, but also don't count as server crash
            _logger.LogWarning("Item {EvalItemId} failed (logic/scoring): {Reason}", item.Id, result.FailureReason);
            return result;
        }
    }

    /// <summary>
    /// Determines if an HTTP exception indicates a server crash (vs. a normal error response).
    /// </summary>
    private static bool IsServerCrashException(HttpRequestException ex)
    {
        // Server crash indicators:
        // - Connection refused/reset (server died)
        // - 502/503/504 during request (server unavailable)
        // - Stream closed unexpectedly
        return ex.StatusCode is System.Net.HttpStatusCode.BadGateway  // 502
                              or System.Net.HttpStatusCode.ServiceUnavailable  // 503
                              or System.Net.HttpStatusCode.GatewayTimeout;  // 504
    }

    /// <summary>
    /// Determines if an exception is transient and worth retrying.
    /// </summary>
    private static bool IsTransientException(Exception ex)
    {
        return ex is HttpRequestException httpEx && httpEx.StatusCode is not null
            && ((int)httpEx.StatusCode >= 500 || httpEx.StatusCode == System.Net.HttpStatusCode.RequestTimeout);
    }
}

/// <summary>
/// Retry configuration for transient failures.
/// </summary>
public record RetryConfig
{
    public int MaxRetryCount { get; init; } = 2;
    public double InitialDelaySeconds { get; init; } = 1.0;
    public double BackoffMultiplier { get; init; } = 2.0;
}

/// <summary>
/// Factory for creating PipelineOrchestrators with checkpoint/resume support.
/// Handles two-phase execution: primary evaluation followed by judge evaluation.
/// </summary>
public static class PipelineOrchestratorFactory
{
    /// <summary>
    /// Creates an orchestrator for the primary evaluation phase.
    /// </summary>
    public static async Task<PipelineOrchestrator> CreatePrimaryAsync(
        IDataSource dataSource,
        EvalPipeline pipeline,
        EvalSetConfig evalSetConfig,
        ResolvedConfig resolvedConfig,
        LlamaServerClient primaryClient,
        PersistentResultCollector resultCollector,
        IProgress<EvalProgress> progress,
        ILogger<PipelineOrchestrator> logger,
        CancellationToken ct,
        LlamaServerClient? judgeClient = null)
    {
        // Load completed items for this phase (checkpoint resume)
        var completedItemIds = await resultCollector.GetCompletedItemIdsAsync(evalSetConfig.Id, "primary", ct);

        logger.LogInformation("Primary phase: {CompletedCount} items already completed (will resume from checkpoint)", completedItemIds.Count);

        return new PipelineOrchestrator(
            dataSource,
            pipeline,
            evalSetConfig,
            resolvedConfig,
            primaryClient,
            judgeClient: judgeClient,  // Judge client for external judges (null for locally managed judges)
            resultCollector,
            progress,
            logger,
            completedItemIds,
            phase: "primary");
    }

    /// <summary>
    /// Creates an orchestrator for the judge evaluation phase.
    /// Uses primary phase results as input.
    /// </summary>
    public static async Task<PipelineOrchestrator> CreateJudgeAsync(
        IDataSource dataSource,
        EvalPipeline pipeline,
        EvalSetConfig evalSetConfig,
        ResolvedConfig resolvedConfig,
        LlamaServerClient judgeClient,
        PersistentResultCollector resultCollector,
        IProgress<EvalProgress> progress,
        ILogger<PipelineOrchestrator> logger,
        CancellationToken ct)
    {
        // Load completed items for judge phase (checkpoint resume)
        var completedItemIds = await resultCollector.GetCompletedItemIdsAsync(evalSetConfig.Id, "judge", ct);

        logger.LogInformation("Judge phase: {CompletedCount} items already judged (will resume from checkpoint)", completedItemIds.Count);

        return new PipelineOrchestrator(
            dataSource,
            pipeline,
            evalSetConfig,
            resolvedConfig,
            primaryClient: null,  // Not used in judge phase
            judgeClient,
            resultCollector,
            progress,
            logger,
            completedItemIds,
            phase: "judge");
    }

    /// <summary>
    /// Checks if a separate phase is needed for judgment (both the model-being-evaluated server and judge server are locally managed).
    /// </summary>
    public static bool NeedsJudgePhase(ResolvedConfig config)
    {
        return config.Judge is { Manage: true } && config.Server is { Manage: true };
    }

    /// <summary>
    /// Checks if primary phase is complete (all items processed).
    /// </summary>
    public static async Task<bool> IsPrimaryPhaseCompleteAsync(
        PersistentResultCollector resultCollector,
        EvalSetConfig evalSetConfig,
        IDataSource dataSource,
        CancellationToken ct)
    {
        var totalCount = await dataSource.GetCountAsync(ct);
        if (!totalCount.HasValue) return false;

        var completedItemIds = await resultCollector.GetCompletedItemIdsAsync(evalSetConfig.Id, "primary", ct);
        return completedItemIds.Count >= totalCount.Value;
    }
}
