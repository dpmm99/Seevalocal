using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Metrics.Models;
using Seevalocal.Server;
using Seevalocal.Server.Models;
using Seevalocal.UI.Commands;
using Seevalocal.UI.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Seevalocal.UI.ViewModels;

/// <summary>
/// View-model for a single-phase eval run.
/// Handles server lifecycle for managed servers.
/// Creates orchestrator after starting servers.
/// </summary>
public sealed class EvalRunViewModel : IEvalRunViewModel, IAsyncDisposable
{
    private readonly EvalPipeline _pipeline;
    private readonly IDataSource _dataSource;
    private readonly PersistentResultCollector _collector;
    private readonly EvalSetConfig _evalSet;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IServerLifecycleService? _serverLifecycle;
    private readonly ServerConfig? _serverConfig;
    private readonly LlamaServerSettings? _serverSettings;
    private readonly ServerConfig? _judgeServerConfig;
    private readonly LlamaServerSettings? _judgeServerSettings;
    private readonly LlamaServerClient? _externalPrimaryClient;
    private readonly LlamaServerClient? _externalJudgeClient;

    private CancellationTokenSource? _cts;
    private bool _serverStopped;
    private bool _disposed;
    private LlamaServerClient? _managedPrimaryClient;
    private LlamaServerClient? _managedJudgeClient;
    private readonly Progress<EvalProgress> _progress;
    private int _earlyCompletionsLimit = 10;
    private PipelineOrchestrator? _orchestrator;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public EvalRunViewModel(
        ResolvedConfig config,
        EvalPipeline pipeline,
        IDataSource dataSource,
        PersistentResultCollector collector,
        EvalSetConfig evalSet,
        ILoggerFactory loggerFactory,
        ILogger logger,
        IServerLifecycleService? serverLifecycle = null,
        ServerConfig? serverConfig = null,
        LlamaServerSettings? serverSettings = null,
        ServerConfig? judgeServerConfig = null,
        LlamaServerSettings? judgeServerSettings = null,
        LlamaServerClient? externalPrimaryClient = null,
        LlamaServerClient? externalJudgeClient = null)
    {
        Config = config;
        _pipeline = pipeline;
        _dataSource = dataSource;
        _collector = collector;
        _evalSet = evalSet;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _serverLifecycle = serverLifecycle;
        _serverConfig = serverConfig;
        _serverSettings = serverSettings;
        _judgeServerConfig = judgeServerConfig;
        _judgeServerSettings = judgeServerSettings;
        _externalPrimaryClient = externalPrimaryClient;
        _externalJudgeClient = externalJudgeClient;
        _progress = new Progress<EvalProgress>();
        _progress.ProgressChanged += (_, progress) => OnProgressChanged(progress);

        PauseCommand = new RelayCommand(TogglePause, () => IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        LoadMoreEarlyCompletionsCommand = new RelayCommand(LoadMoreEarlyCompletions, () => Results.Count > EarlyCompletionsLimit);

        // Subscribe to server events if server lifecycle is available
        if (_serverLifecycle != null)
        {
            _serverLifecycle.ServerErrorReceived += OnServerErrorReceived;
            _serverLifecycle.LoadingProgressChanged += OnServerLoadingProgressChanged;
        }
    }

    private void OnServerErrorReceived(object? sender, ServerErrorEventArgs e)
    {
        // Update status line with error from llama-server
        StatusLine = $"llama-server error: {e.ErrorMessage}";
        OnPropertyChanged(nameof(StatusLine));
        _logger.LogWarning("llama-server error: {ErrorMessage}", e.ErrorMessage);
    }

    private void OnServerLoadingProgressChanged(object? sender, ServerLoadingProgressEventArgs e)
    {
        // Update progress bar and status line during server startup
        ProgressPercent = e.ProgressPercent;
        StatusLine = e.Message ?? $"Starting llama-server... {e.ProgressPercent:F0}%";
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(StatusLine));
    }

    private void OnProgressChanged(EvalProgress progress)
    {
        CompletedCount = progress.CompletedCount;
        TotalCount = progress.TotalCount;
        ProgressPercent = progress.TotalCount > 0 ? (double)progress.CompletedCount / progress.TotalCount * 100 : 0;
        EstimatedRemainingSeconds = progress.EstimatedRemainingSeconds;

        // Calculate moving average tokens/sec from last 10 completed items
        AverageTokensPerSecond = CalculateMovingAverageTokensPerSecond();

        // Update EarlyCompletions collection (will notify via ObservableCollection)
        UpdateEarlyCompletions();
        OnPropertyChanged(nameof(RecentActivitySummary));

        // Refresh results from collector's in-memory cache on each progress update (on UI thread)
        // This is safe because the cache is updated before the progress event is fired
        Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshResultsFromCache());

        if (progress.CompletedCount <= EarlyCompletionsLimit)
        {
            var recentResult = Results.LastOrDefault();
            if (recentResult != null)
            {
                var promptPreview = recentResult.UserPrompt?.Length > 50
                    ? $"{recentResult.UserPrompt.AsSpan(0, 50)}..."
                    : recentResult.UserPrompt ?? "";
                var responsePreview = recentResult.RawResponse?.Length > 50
                    ? $"{recentResult.RawResponse.AsSpan(0, 50)}..."
                    : recentResult.RawResponse ?? "";

                _logger.LogInformation(
                    "[{Status}] Item {Id}: \"{Prompt}\" => \"{Response}\"",
                    recentResult.Succeeded,
                    recentResult.Id,
                    promptPreview,
                    responsePreview);
            }
        }
    }

    /// <summary>
    /// Calculates a moving average of tokens/sec from the last 10 completed items.
    /// This provides a more stable and meaningful metric than instantaneous values.
    /// </summary>
    private double CalculateMovingAverageTokensPerSecond()
    {
        // Get the last 10 completed results
        var recentResults = Results.TakeLast(10).ToList();
        if (recentResults.Count == 0)
            return 0.0;

        double totalTokens = 0;
        double totalDuration = 0;

        foreach (var result in recentResults)
        {
            // Get total token count from metrics
            var totalTokenMetric = result.Metrics.FirstOrDefault(m => m.Name == "totalTokenCount");
            var tokenCount = totalTokenMetric?.Value is MetricScalar.IntMetric intMetric ? intMetric.Value : 0;
            
            totalTokens += tokenCount;
            totalDuration += result.DurationSeconds;
        }

        // Avoid division by zero
        if (totalDuration <= 0)
            return 0.0;

        return totalTokens / totalDuration;
    }

    private void OnOrchestratorProgressChanged(EvalProgress progress)
    {
        // Forward orchestrator progress to the main progress handler
        OnProgressChanged(progress);
    }

    private void RefreshResultsFromCache()
    {
        // Get results from in-memory cache (fast, no database access)
        var allResults = _collector.GetResults();

        // Clear and rebuild the Results collection
        Results.Clear();
        foreach (var result in allResults)
        {
            Results.Add(new EvalResultViewModel(result));
        }

        // Update EarlyCompletions to match
        UpdateEarlyCompletions();

        // Notify UI of changes
        OnPropertyChanged(nameof(Results));
        OnPropertyChanged(nameof(EarlyCompletions));
        OnPropertyChanged(nameof(HasMoreEarlyCompletions));
        OnPropertyChanged(nameof(RecentActivitySummary));
        OnPropertyChanged(nameof(LoadMoreEarlyCompletionsCommand));

        // Notify command that CanExecute may have changed (on UI thread for Avalonia)
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (LoadMoreEarlyCompletionsCommand is RelayCommand cmd)
            {
                cmd.NotifyCanExecuteChanged();
            }
        });
    }

    private async Task RefreshResultsFromDatabaseAsync()
    {
        // Get all results from the collector's database (with full stage outputs and metrics)
        // Use this for the final refresh after the run completes
        try
        {
            var allResults = await _collector.GetResultsAsync(_evalSet.Id, "primary", default);

            // Clear and rebuild the Results collection
            Results.Clear();
            foreach (var result in allResults)
            {
                Results.Add(new EvalResultViewModel(result));
            }

            // Update EarlyCompletions to match
            UpdateEarlyCompletions();

            // Notify UI of changes
            OnPropertyChanged(nameof(Results));
            OnPropertyChanged(nameof(HasMoreEarlyCompletions));
            OnPropertyChanged(nameof(RecentActivitySummary));
            OnPropertyChanged(nameof(LoadMoreEarlyCompletionsCommand));
        }
        catch (ObjectDisposedException)
        {
            // Collector was disposed, use cache instead
            RefreshResultsFromCache();
        }
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    public System.Windows.Input.ICommand PauseCommand { get; }
    public System.Windows.Input.ICommand CancelCommand { get; }
    public System.Windows.Input.ICommand LoadMoreEarlyCompletionsCommand { get; }

    public void TogglePause()
    {
        IsPaused = !IsPaused;
        StatusLine = IsPaused ? "Paused" : "Running...";
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(StatusLine));
        
        // Tell the orchestrator to pause/resume
        _orchestrator?.SetPaused(IsPaused);
    }

    private void LoadMoreEarlyCompletions()
    {
        EarlyCompletionsLimit += 10;
        OnPropertyChanged(nameof(EarlyCompletionsLimit));
        OnPropertyChanged(nameof(EarlyCompletions));
        OnPropertyChanged(nameof(HasMoreEarlyCompletions));
        OnPropertyChanged(nameof(LoadMoreEarlyCompletionsCommand));
    }

    // ─── Observable properties ────────────────────────────────────────────────

    public ResolvedConfig Config { get; }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetField(ref _isRunning, value);
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        private set => SetField(ref _isPaused, value);
    }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    private int _completedCount;
    public int CompletedCount
    {
        get => _completedCount;
        private set => SetField(ref _completedCount, value);
    }

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        private set => SetField(ref _totalCount, value);
    }

    private double _averageTokensPerSecond;
    public double AverageTokensPerSecond
    {
        get => _averageTokensPerSecond;
        private set => SetField(ref _averageTokensPerSecond, value);
    }

    private double? _estimatedRemainingSeconds;
    public double? EstimatedRemainingSeconds
    {
        get => _estimatedRemainingSeconds;
        private set => SetField(ref _estimatedRemainingSeconds, value);
    }

    private string _statusLine = "Ready";
    public string StatusLine
    {
        get => _statusLine;
        private set => SetField(ref _statusLine, value);
    }

    private RunSummary? _summary;
    public RunSummary? Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    private bool _hadFailures;
    public bool HadFailures
    {
        get => _hadFailures;
        private set => SetField(ref _hadFailures, value);
    }

    public ObservableCollection<EvalResultViewModel> Results { get; } = [];
    public ObservableCollection<EvalResultViewModel> EarlyCompletions { get; } = [];

    public int EarlyCompletionsLimit
    {
        get => _earlyCompletionsLimit;
        set
        {
            if (SetField(ref _earlyCompletionsLimit, value))
            {
                UpdateEarlyCompletions();
                OnPropertyChanged(nameof(HasMoreEarlyCompletions));
                OnPropertyChanged(nameof(LoadMoreEarlyCompletionsCommand));
            }
        }
    }

    public bool HasMoreEarlyCompletions => Results.Count > EarlyCompletionsLimit;

    /// <summary>
    /// Updates the EarlyCompletions collection to contain the first N results.
    /// Called when Results or EarlyCompletionsLimit changes.
    /// </summary>
    private void UpdateEarlyCompletions()
    {
        // Sync EarlyCompletions with first N items from Results
        var newEarlyCompletions = Results.Take(EarlyCompletionsLimit).ToList();
        
        // Remove items that are no longer in the first N
        for (int i = EarlyCompletions.Count - 1; i >= 0; i--)
        {
            if (i >= newEarlyCompletions.Count || EarlyCompletions[i] != newEarlyCompletions[i])
            {
                EarlyCompletions.RemoveAt(i);
            }
        }
        
        // Add new items
        for (int i = EarlyCompletions.Count; i < newEarlyCompletions.Count; i++)
        {
            EarlyCompletions.Add(newEarlyCompletions[i]);
        }
    }

    public string RecentActivitySummary
    {
        get
        {
            var count = Results.Count;
            if (count == 0) return "No completions yet...";
            
            // Show count and last item info
            var lastResult = Results.LastOrDefault();
            if (lastResult == null) return $"{count} items loaded...";
            
            var promptPreview = lastResult.UserPrompt?.Length > 40
                ? $"{lastResult.UserPrompt.AsSpan(0, 40)}..."
                : lastResult.UserPrompt ?? "N/A";
            return $"[{count}] {promptPreview}";
        }
    }

    // ─── Run methods ──────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken externalCt = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("A run is already in progress.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        IsRunning = true;
        IsPaused = false;
        Results.Clear();

        // Initialize CompletedCount from checkpoint if resuming
        CompletedCount = Config.Run?.ContinueFromCheckpoint == true && _collector != null
            ? await GetCheckpointCompletedCountAsync()
            : 0;

        // Load existing results from checkpoint if resuming
        if (Config.Run?.ContinueFromCheckpoint == true && _collector != null)
        {
            await LoadResultsFromCheckpointAsync();
        }

        // Notify commands that their CanExecute state has changed
        ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));

        // Determine max concurrent evals - will be updated from server props if managing server
        var orchestratorMaxConcurrent = Config.Run?.MaxConcurrentEvals ?? 4;

        try
        {
            _logger.LogInformation("EvalRunViewModel: Starting run '{RunName}'",
                Config.Run?.RunName ?? "(unnamed)");

            // Start managed primary server if needed
            LlamaServerClient? primaryClientToUse = _externalPrimaryClient;
            if (_serverLifecycle != null && _serverConfig != null && _serverSettings != null)
            {
                StatusLine = $"Starting primary llama-server at {DateTimeOffset.Now:HH:mm:ss}...";
                OnPropertyChanged(nameof(StatusLine));

                var startResult = await _serverLifecycle.StartAsync(_serverConfig, _serverSettings, _cts.Token);
                if (startResult.IsFailed)
                    throw new InvalidOperationException($"Failed to start primary llama-server: {startResult.Errors[0].Message}");

                var serverInfo = startResult.Value;
                _logger.LogInformation("Primary llama-server started at {BaseUrl}", serverInfo.BaseUrl);

                // Save the resolved binary path for resume capability
                if (serverInfo.BinaryPath is not null && _collector is PersistentResultCollector persistentCollector)
                {
                    await persistentCollector.SaveServerBinaryPathAsync("primary", serverInfo.BinaryPath, _cts.Token);
                }

                var primaryHttpClient = CreateHttpClient(serverInfo);
                var primaryClientLogger = _loggerFactory.CreateLogger<LlamaServerClient>();
                var maxConcurrent = Config.Run?.MaxConcurrentEvals ?? 4;
                _managedPrimaryClient = new LlamaServerClient(serverInfo, primaryHttpClient, primaryClientLogger, maxConcurrent);

                // Initialize semaphore based on actual server slot count
                await _managedPrimaryClient.InitializeSemaphoreFromServerAsync(_cts.Token);

                // Use server slot count for orchestrator if not explicitly configured
                if (Config.Run?.MaxConcurrentEvals is not int configuredMax)
                {
                    orchestratorMaxConcurrent = serverInfo.TotalSlots;
                }

                primaryClientToUse = _managedPrimaryClient;

                StatusLine = $"Running evaluations at {DateTimeOffset.Now:HH:mm:ss}...";
                OnPropertyChanged(nameof(StatusLine));
            }

            // Start managed judge server if needed
            LlamaServerClient? judgeClientToUse = _externalJudgeClient;
            if (_judgeServerConfig != null && _judgeServerSettings != null && _serverLifecycle != null)
            {
                StatusLine = $"Starting judge llama-server at {DateTimeOffset.Now:HH:mm:ss}...";
                OnPropertyChanged(nameof(StatusLine));

                var judgeStartResult = await _serverLifecycle.StartAsync(_judgeServerConfig, _judgeServerSettings, _cts.Token);
                if (judgeStartResult.IsFailed)
                    throw new InvalidOperationException($"Failed to start judge llama-server: {judgeStartResult.Errors[0].Message}");

                var judgeServerInfo = judgeStartResult.Value;
                _logger.LogInformation("Judge llama-server started at {BaseUrl}", judgeServerInfo.BaseUrl);

                // Save the resolved binary path for resume capability
                if (judgeServerInfo.BinaryPath is not null && _collector is PersistentResultCollector persistentCollector)
                {
                    await persistentCollector.SaveServerBinaryPathAsync("judge", judgeServerInfo.BinaryPath, _cts.Token);
                }

                var judgeHttpClient = CreateHttpClient(judgeServerInfo);
                var judgeClientLogger = _loggerFactory.CreateLogger<LlamaServerClient>();
                var maxConcurrent = Config.Run?.MaxConcurrentEvals ?? 4;
                _managedJudgeClient = new LlamaServerClient(judgeServerInfo, judgeHttpClient, judgeClientLogger, maxConcurrent);

                // Initialize semaphore based on actual server slot count
                await _managedJudgeClient.InitializeSemaphoreFromServerAsync(_cts.Token);

                judgeClientToUse = _managedJudgeClient;
            }

            // Create orchestrator now that we have clients (simple mode, returns results)
            var orchestratorLogger = _loggerFactory.CreateLogger<PipelineOrchestrator>();
            _orchestrator = new PipelineOrchestrator(
                _pipeline,
                _dataSource,
                _collector,
                Config,
                primaryClientToUse!,
                judgeClientToUse,
                orchestratorLogger);

            // Subscribe to orchestrator progress events
            _orchestrator.ProgressChanged += OnOrchestratorProgressChanged;

            // Run the pipeline
            await _orchestrator.RunAsync(orchestratorMaxConcurrent, _cts.Token);

            // Final results refresh after completion (from database with full stage outputs)
            await RefreshResultsFromDatabaseAsync();

            HadFailures = Results.Any(static r => !r.Succeeded);
            var completionTime = DateTimeOffset.Now;
            StatusLine = HadFailures ? $"Complete (with failures) at {completionTime:HH:mm:ss}"
                                     : $"Complete at {completionTime:HH:mm:ss}";
            OnPropertyChanged(nameof(StatusLine));
        }
        catch (OperationCanceledException)
        {
            StatusLine = $"Cancelled at {DateTimeOffset.Now:HH:mm:ss}";
            OnPropertyChanged(nameof(StatusLine));
            _logger.LogInformation("Run was cancelled");
        }
        catch (Exception ex)
        {
            StatusLine = $"Error at {DateTimeOffset.Now:HH:mm:ss}: {ex.Message}";
            OnPropertyChanged(nameof(StatusLine));
            _logger.LogError(ex, "Run failed with unhandled exception");
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsPaused));
            ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
            await StopAllServersAsync();
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task StopAllServersAsync()
    {
        if (_serverLifecycle != null && !_serverStopped)
        {
            _serverStopped = true;
            _logger.LogInformation("EvalRunViewModel: Stopping llama-server...");
            StatusLine = "Stopping llama-server...";
            OnPropertyChanged(nameof(StatusLine));

            try
            {
                await _serverLifecycle.DisposeAsync();
                _logger.LogInformation("EvalRunViewModel: llama-server stopped");
                StatusLine = $"Server stopped at {DateTimeOffset.Now:HH:mm:ss}";
                OnPropertyChanged(nameof(StatusLine));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EvalRunViewModel: Error stopping llama-server");
                StatusLine = $"Error stopping server at {DateTimeOffset.Now:HH:mm:ss}: {ex.Message}";
                OnPropertyChanged(nameof(StatusLine));
            }
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
        StatusLine = "Cancelling...";
        OnPropertyChanged(nameof(StatusLine));
    }

    // ─── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    // ─── IAsyncDisposable & IDisposable ───────────────────────────────────────

    /// <summary>
    /// Gets the count of already-completed items from checkpoint database.
    /// </summary>
    private async Task<int> GetCheckpointCompletedCountAsync()
    {
        if (_collector is not PersistentResultCollector persistentCollector)
            return 0;

        try
        {
            var completedIds = await persistentCollector.GetCompletedItemIdsAsync(_evalSet.Id, "primary", default);
            _logger.LogInformation("Checkpoint loaded: {Count} already-completed items", completedIds.Count);
            return completedIds.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load checkpoint completed count");
            return 0;
        }
    }

    /// <summary>
    /// Loads existing results from checkpoint database.
    /// </summary>
    private async Task LoadResultsFromCheckpointAsync()
    {
        if (_collector is not PersistentResultCollector persistentCollector)
            return;

        try
        {
            // Populate the collector's in-memory cache with checkpoint data
            // This ensures RefreshResultsFromCache() will display checkpoint completions
            await persistentCollector.PopulateCacheFromCheckpointAsync(_evalSet.Id, default);
            
            // Debug: log what was loaded
            var cacheCount = persistentCollector.GetResults().Count;
            _logger.LogInformation("Checkpoint loaded: {Count} results in cache, EvalSetId={EvalSetId}", cacheCount, _evalSet.Id);
            
            // Refresh the UI from the cache (same as live completions)
            RefreshResultsFromCache();

            _logger.LogInformation("After RefreshResultsFromCache: Results.Count={Count}", Results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load checkpoint results");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _progress.ProgressChanged -= (_, progress) => OnProgressChanged(progress);

        // Unsubscribe from server events
        if (_serverLifecycle != null)
        {
            _serverLifecycle.ServerErrorReceived -= OnServerErrorReceived;
            _serverLifecycle.LoadingProgressChanged -= OnServerLoadingProgressChanged;
        }

        await StopAllServersAsync();
        _cts?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private static HttpClient CreateHttpClient(ServerInfo serverInfo)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(serverInfo.BaseUrl),
            Timeout = TimeSpan.FromHours(6)
        };
        if (!string.IsNullOrEmpty(serverInfo.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serverInfo.ApiKey);
        }
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Seevalocal.UI/1.0");
        return httpClient;
    }
}

/// <summary>
/// A flat view of a single <see cref="EvalResult"/> for list display.
/// </summary>
public sealed class EvalResultViewModel(EvalResult result)
{
    private readonly EvalResult _result = result;

    public string Id => _result.EvalItemId;
    public string EvalSetId => _result.EvalSetId;
    public bool Succeeded => _result.Succeeded;
    public bool IsPassFailFormat => CheckIsPassFailFormat();
    
    /// <summary>
    /// Pipeline execution status: "Pending", "Succeeded", or "Failed".
    /// </summary>
    public string PipelineStatus => _result.Succeeded ? "Succeeded" : (_result.StartedAt == default ? "Pending" : "Failed");
    
    public string? FailureReason => _result.FailureReason;
    public double DurationSeconds => _result.DurationSeconds;
    public DateTimeOffset StartedAt => _result.StartedAt;
    public string? RawResponse => _result.RawLlmResponse;

    /// <summary>
    /// Checks if the judge is using pass/fail format (vs numeric score).
    /// </summary>
    private bool CheckIsPassFailFormat()
    {
        // Check if there's a numeric score metric - if so, it's not pass/fail
        var hasNumericScore = _result.Metrics.Any(m => 
            m.Name.Contains("Score", StringComparison.OrdinalIgnoreCase) && 
            m.Value is MetricScalar.DoubleMetric);
        return !hasNumericScore;
    }

    /// <summary>
    /// Exposes the underlying metrics for programmatic access (e.g., calculating tokens/sec).
    /// </summary>
    public IReadOnlyList<MetricValue> Metrics => _result.Metrics;

    public string? UserPrompt =>
        _result.AllStageOutputs.TryGetValue("PromptStage.userPrompt", out var val)
            ? val?.ToString() : null;

    public string? ExpectedOutput =>
        _result.AllStageOutputs.TryGetValue("PromptStage.expectedOutput", out var val)
            ? val?.ToString() : null;

    public string? JudgeRationale =>
        _result.AllStageOutputs.TryGetValue("JudgeStage.rationale", out var val)
            ? val?.ToString() : null;

    public double? JudgeScore =>
         (_result.Metrics.FirstOrDefault(static m => m.Name.Contains("Score") && m.Value is MetricScalar.DoubleMetric)?.Value as MetricScalar.DoubleMetric)?.Value;

    public IEnumerable<MetricDisplayItem> MetricDisplay =>
        _result.Metrics
            .Select(static m => new MetricDisplayItem(m.Name, m.Value?.ToString() ?? ""))
            .Where(static m => !string.IsNullOrEmpty(m.Value));
}

/// <summary>
/// Display item for metrics in the UI.
/// </summary>
public sealed class MetricDisplayItem(string name, string value)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
}
