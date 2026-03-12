using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.Server;
using System.Diagnostics;
using System.Threading.Channels;

namespace Seevalocal.Core.Pipeline;

/// <summary>
/// Orchestrates the evaluation of all items in a dataset.
/// Manages concurrency, retry, progress reporting, and result collection.
/// Includes resilience to llama-server crashes with automatic retry and circuit breaker.
/// Supports checkpoint/resume via PersistentResultCollector.
/// </summary>
/// <remarks>
/// <para><strong>Concurrency Control:</strong></para>
/// <para>
/// Concurrency is now controlled by semaphores inside each <c>LlamaServerClient</c> instance,
/// not by this orchestrator. Each client has its own semaphore (default: 10 concurrent requests),
/// which is acquired before making HTTP calls to the llama-server and released after receiving
/// the response.
/// </para>
/// <para>
/// This design ensures:
/// </para>
/// <list type="bullet">
/// <item>One semaphore per server instance (primary and judge have separate semaphores)</item>
/// <item>Natural lifetime - semaphore lifetime matches client/server lifetime</item>
/// <item>Automatic deduplication - one client = one semaphore per server</item>
/// <item>Cleaner separation - orchestrator orchestrates, client handles server communication</item>
/// </list>
/// <para>
/// The <c>maxConcurrentEvals</c> parameter passed to <c>RunAsync</c> now controls the number of
/// concurrent eval items being processed, while the client's semaphore controls concurrent
/// requests to each server. For best results, these should be similar values.
/// </para>
/// </remarks>
/// <remarks>
/// Creates a full-featured orchestrator with checkpoint/resume support.
/// </remarks>
public sealed class PipelineOrchestrator(
    IDataSource dataSource,
    EvalPipeline pipeline,
    EvalSetConfig? evalSetConfig,
    ResolvedConfig resolvedConfig,
    LlamaServerClient? primaryClient,
    LlamaServerClient? judgeClient,
    IResultCollector resultCollector,
    IProgress<EvalProgress>? progress,
    ILogger<PipelineOrchestrator> logger,
    HashSet<string>? completedItemIds = null,
    string phase = "primary",
    bool returnResults = false)
{
    private readonly IDataSource _dataSource = dataSource;
    private readonly EvalPipeline _pipeline = pipeline;
    private readonly EvalSetConfig? _evalSetConfig = evalSetConfig;
    private readonly ResolvedConfig _resolvedConfig = resolvedConfig;
    private readonly LlamaServerClient? _primaryClient = primaryClient;
    private readonly LlamaServerClient? _judgeClient = judgeClient;
    private readonly IResultCollector _resultCollector = resultCollector;
    private readonly IProgress<EvalProgress>? _progress = progress;
    private Action<EvalProgress>? _progressEvent;  // Not readonly - used by event
    private readonly ILogger<PipelineOrchestrator> _logger = logger;
    private readonly HashSet<string>? _completedItemIds = completedItemIds;
    private readonly string _phase = phase;
    private readonly bool _returnResults = returnResults;

    // Circuit breaker state for server crash resilience
    private int _consecutiveServerCrashes;
    private const int MaxConsecutiveServerCrashes = 5;
    private readonly Lock _crashLock = new();

    // Pause mechanism
    private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);
    private volatile bool _isPaused;

    /// <summary>
    /// Pauses or resumes the evaluation. When paused, the orchestrator will wait
    /// before processing each item.
    /// </summary>
    public void SetPaused(bool paused)
    {
        if (paused && !_isPaused)
        {
            _isPaused = true;
            _pauseSemaphore.Wait(); // Acquire the semaphore to block processing
        }
        else if (!paused && _isPaused)
        {
            _isPaused = false;
            _pauseSemaphore.Release(); // Release to allow processing to continue
        }
    }

    /// <summary>
    /// Progress event for simple scenarios (non-checkpoint).
    /// </summary>
    public event Action<EvalProgress>? ProgressChanged
    {
        add => _progressEvent += value;
        remove => _progressEvent -= value;
    }

    /// <summary>
    /// Event raised when an item starts processing.
    /// </summary>
    public event Action<string>? ItemStarted;  // EvalItemId

    /// <summary>
    /// Event raised when an item completes processing.
    /// </summary>
    public event Action<string>? ItemCompleted;  // EvalItemId

    /// <summary>
    /// Creates a simple orchestrator without checkpoint/resume support.
    /// </summary>
    public PipelineOrchestrator(
        EvalPipeline pipeline,
        IDataSource dataSource,
        IResultCollector collector,
        ResolvedConfig config,
        LlamaServerClient primaryClient,
        LlamaServerClient? judgeClient,
        ILogger<PipelineOrchestrator> logger)
        : this(
            dataSource,
            pipeline,
            null,  // evalSetConfig
            config,
            primaryClient,
            judgeClient,
            collector,
            null,  // progress (use event instead)
            logger,
            null,  // completedItemIds
            "single",
            returnResults: true)
    {
    }

    /// <summary>
    /// Run all items concurrently. Returns results if configured for simple mode.
    /// </summary>
    public async Task<IReadOnlyList<EvalResult>> RunAsync(
        int maxConcurrentEvals,
        CancellationToken ct)
    {
        if (_returnResults)
        {
            // Simple mode: return results at end
            return await RunSimpleAsync(maxConcurrentEvals, ct);
        }
        else
        {
            // Checkpoint mode: results via collector, void return
            await RunWithCheckpointAsync(maxConcurrentEvals, ct);
            return _resultCollector.GetResults();
        }
    }

    /// <summary>
    /// Simple execution without checkpoint/resume.
    /// </summary>
    private async Task<IReadOnlyList<EvalResult>> RunSimpleAsync(int maxConcurrentEvals, CancellationToken ct)
    {
        _logger.LogInformation(
            "Pipeline '{PipelineName}' starting. MaxConcurrency={MaxConcurrency}",
            _pipeline.PipelineName, maxConcurrentEvals);

        var totalCount = await _dataSource.GetCountAsync(ct);
        var overallSw = Stopwatch.StartNew();
        var completedCount = 0;
        var completedTokensPerSecondSum = 0.0;
        var completedWithTokens = 0;

        var channel = Channel.CreateUnbounded<EvalItem>(
            new UnboundedChannelOptions { SingleWriter = true });

        // Initial progress report - include already-completed items from checkpoint
        var alreadyCompletedCount = _completedItemIds?.Count ?? 0;
        _progressEvent?.Invoke(new EvalProgress
        {
            EvalItemId = "",
            Succeeded = false,
            CompletedCount = alreadyCompletedCount,  // Start with checkpoint count
            TotalCount = (totalCount ?? -1) + alreadyCompletedCount,  // Total includes completed
            ElapsedSeconds = 0,
            EstimatedRemainingSeconds = 60 * ((totalCount ?? 0) - alreadyCompletedCount),
            AverageCompletionTokensPerSecond = 0
        });

        // Producer: stream items into channel
        var producer = Task.Run(async () =>
        {
            try
            {
                var itemsWritten = 0;
                await foreach (var item in _dataSource.GetItemsAsync(ct))
                {
                    await channel.Writer.WriteAsync(item, ct);
                    itemsWritten++;
                }
                _logger.LogDebug("Producer wrote {ItemsWritten} items to channel", itemsWritten);
                channel.Writer.Complete();
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

        var semaphore = new SemaphoreSlim(maxConcurrentEvals, maxConcurrentEvals);
        List<Task> consumerTasks = [];
        var itemsRead = 0;

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            await semaphore.WaitAsync(ct);

            var capturedItem = item;
            itemsRead++;
            _logger.LogDebug("Consumer read item {ItemNumber}: {ItemId}", itemsRead, capturedItem.Id);
            consumerTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var result = await RunItemSimpleAsync(capturedItem, ct);

                    await _resultCollector.CollectAsync(result, ct);

                    var done = Interlocked.Increment(ref completedCount);

                    // Estimate tokens/s for progress
                    var tpsMetric = result.Metrics.FirstOrDefault(m => m.Name == "promptTokensPerSecond");
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

                    _progressEvent?.Invoke(new EvalProgress
                    {
                        EvalItemId = capturedItem.Id,
                        Succeeded = result.Succeeded,
                        CompletedCount = alreadyCompletedCount + done,  // Include checkpoint count
                        TotalCount = (totalCount ?? -1) + alreadyCompletedCount,  // Total includes completed
                        ElapsedSeconds = elapsed,
                        EstimatedRemainingSeconds = remaining,
                        AverageCompletionTokensPerSecond = avgTps
                    });

                    _logger.LogDebug(
                        "Pipeline '{PipelineName}' completed item '{ItemId}' in {DurationSeconds:F2}s. Succeeded={Succeeded}",
                        _pipeline.PipelineName, capturedItem.Id, result.DurationSeconds, result.Succeeded);
                }
                catch (Exception ex)
                {
                    // Log the exception but don't rethrow - we want to continue processing other items
                    _logger.LogError(ex, "Pipeline '{PipelineName}' failed to process item '{ItemId}'",
                        _pipeline.PipelineName, capturedItem.Id);

                    // Create a failed result so the item is still counted
                    var failedResult = new EvalResult
                    {
                        EvalItemId = capturedItem.Id,
                        EvalSetId = _evalSetConfig?.Id ?? "",
                        Succeeded = false,
                        FailureReason = ex.Message,
                        Metrics = [],
                        AllStageOutputs = new Dictionary<string, object?>(),
                        StartedAt = DateTimeOffset.UtcNow,
                        DurationSeconds = 0
                    };

                    await _resultCollector.CollectAsync(failedResult, ct);
                    _ = Interlocked.Increment(ref completedCount);
                }
                finally
                {
                    _ = semaphore.Release();
                }
            }, ct));
        }

        _logger.LogDebug("Consumer finished reading {ItemsRead} items from channel, waiting for {TaskCount} tasks", itemsRead, consumerTasks.Count);
        await producer;
        _logger.LogDebug("Producer completed");
        await Task.WhenAll(consumerTasks);
        _logger.LogDebug("All {TaskCount} consumer tasks completed", consumerTasks.Count);
        await _resultCollector.FinalizeAsync(ct);

        overallSw.Stop();
        _logger.LogInformation(
            "Pipeline '{PipelineName}' finished. {CompletedCount} items in {DurationSeconds:F2}s",
            _pipeline.PipelineName, completedCount, overallSw.Elapsed.TotalSeconds);

        return _resultCollector.GetResults();
    }

    /// <summary>
    /// Run item without retry/circuit breaker (simple mode).
    /// </summary>
    private async Task<EvalResult> RunItemSimpleAsync(EvalItem item, CancellationToken ct)
    {
        // Load existing stage outputs from database (for checkpoint resumption / judge phase)
        var existingStageOutputs = new Dictionary<string, object?>();
        if (_resultCollector is PersistentResultCollector persistentCollector)
        {
            existingStageOutputs = await persistentCollector.GetStageOutputsAsync(item.Id, ct);
        }

        // Load the last completed stage for checkpoint resumption
        string? lastCompletedStage = null;
        if (_resultCollector is PersistentResultCollector stageCollector)
        {
            lastCompletedStage = await stageCollector.GetLastCompletedStageAsync(item.Id, ct);
        }

        var context = new EvalStageContext
        {
            Item = item,
            Config = _resolvedConfig,
            PrimaryClient = _primaryClient,
            JudgeClient = _judgeClient,
            CancellationToken = ct,
            StageOutputs = existingStageOutputs,  // Pre-load existing outputs (PromptStage will use these)
            LastCompletedStage = lastCompletedStage  // For skipping already-completed stages
        };

        var continueOnStageFailure = _resolvedConfig.Run?.ContinueOnEvalFailure ?? true;
        return await _pipeline.RunItemAsync(context, continueOnStageFailure, evalSetId: _evalSetConfig?.Id ?? "", ct: ct);
    }

    /// <summary>
    /// Full execution with checkpoint/resume, retry, and circuit breaker.
    /// </summary>
    private async Task RunWithCheckpointAsync(int maxConcurrentCount, CancellationToken ct)
    {
        var runSw = Stopwatch.StartNew();

        var totalCount = await _dataSource.GetCountAsync(ct);
        var skippedCount = _completedItemIds?.Count ?? 0;
        var effectiveTotal = totalCount.HasValue ? totalCount.Value - skippedCount : totalCount;

        _logger.LogInformation(
            "Orchestrator starting: EvalSet={EvalSetId}, Pipeline={PipelineName}, Phase={Phase}, MaxConcurrent={MaxConcurrentCount}, TotalItems={TotalItems}, Skipped={SkippedCount}",
            _evalSetConfig?.Id ?? "unknown", _pipeline.PipelineName, _phase, maxConcurrentCount, effectiveTotal?.ToString() ?? "unknown", skippedCount);

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
                    
                    // In 2-phase mode, also skip items completed in the OTHER phase
                    // For primary phase: skip if any stage was completed (including JudgeStage from previous run)
                    // For judge phase: skip if JudgeStage was completed
                    if (_resultCollector is PersistentResultCollector persistentCollector)
                    {
                        var lastCompletedStage = await persistentCollector.GetLastCompletedStageAsync(item.Id, ct);
                        if (!string.IsNullOrEmpty(lastCompletedStage))
                        {
                            if (_phase == "primary")
                            {
                                // Primary phase: skip if ANY stage was completed (item was processed before)
                                _logger.LogDebug("Skipping item {EvalItemId} - already processed (last={LastCompletedStage})", 
                                    item.Id, lastCompletedStage);
                                continue;
                            }
                            else if (_phase == "judge" && lastCompletedStage == "JudgeStage")
                            {
                                // Judge phase: skip if JudgeStage was completed
                                _logger.LogDebug("Skipping item {EvalItemId} - judge already completed", item.Id);
                                continue;
                            }
                        }
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
            .Select(_ => ConsumeAsync(channel.Reader, semaphore, effectiveTotal, runSw, () => Interlocked.Increment(ref localCompletedCount), processingTasks, ct))
            .ToArray();

        await Task.WhenAll([producerTask, .. consumerTasks]);

        // Wait for all in-flight processing tasks to complete
        await Task.WhenAll(processingTasks);

        runSw.Stop();
        _logger.LogInformation(
            "Orchestrator finished: EvalSet={EvalSetId}, Phase={Phase}, Completed={CompletedCount}, Skipped={SkippedCount}, Elapsed={ElapsedSeconds:F2}s",
            _evalSetConfig?.Id ?? "unknown", _phase, localCompletedCount, skippedCount, runSw.Elapsed.TotalSeconds);
    }

    private async Task ConsumeAsync(
        ChannelReader<EvalItem> reader,
        SemaphoreSlim semaphore,
        int? totalCount,
        Stopwatch runSw,
        Func<int> incrementCompleted,
        System.Collections.Concurrent.ConcurrentBag<Task> processingTasks,
        CancellationToken ct)
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
        // Fire item started event
        ItemStarted?.Invoke(item.Id);

        try
        {
            // Wait for pause semaphore (blocks if paused)
            if (_isPaused)
            {
                await _pauseSemaphore.WaitAsync(ct);
                _pauseSemaphore.Release(); // Immediately release so other items can check pause state
            }

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

            var alreadyCompletedCount = _completedItemIds?.Count ?? 0;
            _progress?.Report(new EvalProgress
            {
                EvalItemId = item.Id,
                Succeeded = result.Succeeded,
                CompletedCount = alreadyCompletedCount + completed,  // Include checkpoint count
                TotalCount = (totalCount ?? -1) + alreadyCompletedCount,  // Total includes completed
                ElapsedSeconds = elapsed,
                EstimatedRemainingSeconds = estimatedRemaining
            });
        }
        finally
        {
            // Fire item completed event
            ItemCompleted?.Invoke(item.Id);
        }
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
                    EvalSetId = _evalSetConfig?.Id ?? "",
                    Succeeded = false,
                    FailureReason = $"llama-server crashed {MaxConsecutiveServerCrashes} consecutive times. Server may be unstable or misconfigured.",
                    Metrics = [],
                    AllStageOutputs = new Dictionary<string, object?>(),
                    StartedAt = DateTimeOffset.UtcNow,
                    DurationSeconds = 0
                };
            }

            // Load existing stage outputs from database (for checkpoint resumption / judge phase)
            var existingStageOutputs = new Dictionary<string, object?>();
            if (_resultCollector is PersistentResultCollector persistentCollector)
            {
                existingStageOutputs = await persistentCollector.GetStageOutputsAsync(item.Id, ct);
            }

            // Load the last completed stage for checkpoint resumption
            string? lastCompletedStage = null;
            if (_resultCollector is PersistentResultCollector stageCollector)
            {
                lastCompletedStage = await stageCollector.GetLastCompletedStageAsync(item.Id, ct);
            }

            var context = new EvalStageContext
            {
                Item = item,
                Config = _resolvedConfig,
                PrimaryClient = _primaryClient,
                JudgeClient = _judgeClient,
                CancellationToken = ct,
                StageOutputs = existingStageOutputs,  // Pre-load existing outputs (PromptStage will use these)
                LastCompletedStage = lastCompletedStage  // For skipping already-completed stages
            };

            EvalResult result;
            try
            {
                result = await _pipeline.RunItemAsync(context, _resolvedConfig.Run?.ContinueOnEvalFailure ?? true, _evalSetConfig?.Id ?? "", ct);
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
                    continue;

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
                    EvalSetId = _evalSetConfig?.Id ?? "",
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
                    EvalSetId = _evalSetConfig?.Id ?? "",
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
                    EvalSetId = _evalSetConfig?.Id ?? "",
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

    private static bool IsServerCrashException(HttpRequestException ex)
    {
        return ex.StatusCode is System.Net.HttpStatusCode.BadGateway  // 502
                              or System.Net.HttpStatusCode.ServiceUnavailable  // 503
                              or System.Net.HttpStatusCode.GatewayTimeout;  // 504
    }

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
        logger.LogInformation("CreatePrimaryAsync: evalSetConfig.Id = {EvalSetId}", evalSetConfig.Id);

        // Log all eval set IDs in the checkpoint database for debugging
        var dbEvalSetIds = await resultCollector.GetEvalSetIdsAsync(ct);
        logger.LogInformation("Checkpoint database contains EvalSetIds: {EvalSetIds}", string.Join(", ", dbEvalSetIds));

        // Query completed items for ALL EvalSetIds in the database (not just the current one)
        // This handles the case where previous runs used different EvalSetIds
        var allCompletedItemIds = new HashSet<string>();
        foreach (var dbEvalSetId in dbEvalSetIds)
        {
            var completedForThisId = await resultCollector.GetCompletedItemIdsAsync(dbEvalSetId, "primary", ct);
            logger.LogInformation("EvalSetId {EvalSetId}: {CompletedCount} primary items completed", dbEvalSetId, completedForThisId.Count);
            foreach (var id in completedForThisId)
            {
                allCompletedItemIds.Add(id);
            }
        }

        logger.LogInformation("Primary phase: {CompletedCount} total items already completed across all EvalSetIds (will resume from checkpoint)", allCompletedItemIds.Count);

        return new PipelineOrchestrator(
            dataSource,
            pipeline,
            evalSetConfig,
            resolvedConfig,
            primaryClient,
            judgeClient,
            resultCollector,
            progress,
            logger,
            allCompletedItemIds,
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
        // Get items that have completed judge phase (LastCompletedStage = 'JudgeComplete')
        var allCompletedItemIds = await resultCollector.GetJudgeCompletedItemIdsAsync(ct);

        logger.LogInformation("Judge phase: {CompletedCount} items already have judge results (will resume from checkpoint)", allCompletedItemIds.Count);

        return new PipelineOrchestrator(
            dataSource,
            pipeline,
            evalSetConfig,
            resolvedConfig,
            primaryClient: null,
            judgeClient,
            resultCollector,
            progress,
            logger,
            allCompletedItemIds,
            phase: "judge");
    }

    /// <summary>
    /// Checks if a separate judge phase is needed (both primary and judge servers are locally managed).
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
