using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Metrics.Models;
using Seevalocal.Server;
using Seevalocal.Server.Models;
using Seevalocal.UI.Commands;
using Seevalocal.UI.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Seevalocal.UI.Services;

/// <summary>
/// View model for a two-phase evaluation run.
/// Phase 1: Primary evaluation (runs on primary llama-server)
/// Phase 2: Judge evaluation (runs on judge llama-server, only started after phase 1 completes)
/// Supports checkpoint/resume from SQLite database.
/// </summary>
public sealed class TwoPhaseEvalRunViewModel : IEvalRunViewModel, IAsyncDisposable
{
    private readonly EvalSetConfig _evalSet;
    private readonly EvalPipeline _pipeline;  // Full pipeline (with JudgeStage for judge phase)
    private readonly EvalPipeline _primaryPipeline;  // Primary phase pipeline (without JudgeStage if judge is locally managed)
    private readonly IDataSource _dataSource;
    private readonly PersistentResultCollector _collector;
    private readonly IServerLifecycleService _serverLifecycle;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly Progress<EvalProgress> _progress;

    private PipelineOrchestrator? _primaryOrchestrator;
    private bool _primaryPhaseComplete;
    private bool _judgePhaseComplete;
    private LlamaServerClient? _primaryClient;  // Track primary client for managed servers
    private int _primarySlotCount = 4;  // Default, updated from server props
    private int _judgeSlotCount = 4;    // Default, updated from server props
    private bool _disposed;
    private int _earlyCompletionsLimit = 10;

    private CancellationTokenSource? _cts;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public TwoPhaseEvalRunViewModel(
        ResolvedConfig config,
        EvalSetConfig evalSet,
        EvalPipeline pipeline,
        IDataSource dataSource,
        PersistentResultCollector collector,
        IServerLifecycleService serverLifecycle,
        ILoggerFactory loggerFactory,
        ILogger logger,
        Progress<EvalProgress> progress,
        EvalPipeline primaryPipeline)
    {
        Config = config;
        _evalSet = evalSet;
        _pipeline = pipeline;
        _primaryPipeline = primaryPipeline;
        _dataSource = dataSource;
        _collector = collector;
        _serverLifecycle = serverLifecycle;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _progress = progress;

        _progress.ProgressChanged += OnProgressChanged;

        PauseCommand = new RelayCommand(TogglePause, () => IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        LoadMoreEarlyCompletionsCommand = new RelayCommand(LoadMoreEarlyCompletions, () => Results.Count > EarlyCompletionsLimit);

        // Subscribe to server events
        _serverLifecycle.ServerErrorReceived += OnServerErrorReceived;
        _serverLifecycle.LoadingProgressChanged += OnServerLoadingProgressChanged;
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

    // ─── IEvalRunViewModel Implementation ─────────────────────────────────────

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
    public double ProgressPercent { get; private set; }
    public int CompletedCount { get; private set; }
    public int TotalCount { get; private set; }
    public double? EstimatedRemainingSeconds { get; private set; }
    public double AverageTokensPerSecond { get; private set; }
    public string StatusLine { get; private set; } = "Ready";
    public RunSummary? Summary { get; private set; }
    public bool HadFailures { get; private set; }
    public ObservableCollection<EvalResultViewModel> Results { get; } = [];

    public int EarlyCompletionsLimit
    {
        get => _earlyCompletionsLimit;
        set
        {
            if (_earlyCompletionsLimit != value)
            {
                _earlyCompletionsLimit = value;
                OnPropertyChanged(nameof(EarlyCompletionsLimit));
                OnPropertyChanged(nameof(EarlyCompletions));
                OnPropertyChanged(nameof(LoadMoreEarlyCompletionsCommand));
            }
        }
    }

    public IEnumerable<EvalResultViewModel> EarlyCompletions => Results.Take(EarlyCompletionsLimit);

    public bool HasMoreEarlyCompletions => Results.Count > EarlyCompletionsLimit;

    public System.Windows.Input.ICommand PauseCommand { get; }
    public System.Windows.Input.ICommand CancelCommand { get; }
    public System.Windows.Input.ICommand LoadMoreEarlyCompletionsCommand { get; }

    public void TogglePause()
    {
        IsPaused = !IsPaused;
        StatusLine = IsPaused ? "Paused" : "Running...";
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(StatusLine));
        ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
        
        // Tell the orchestrator to pause/resume
        _primaryOrchestrator?.SetPaused(IsPaused);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        StatusLine = "Cancelling...";
        OnPropertyChanged(nameof(StatusLine));
    }

    private void LoadMoreEarlyCompletions()
    {
        EarlyCompletionsLimit += 10;
        OnPropertyChanged(nameof(HasMoreEarlyCompletions));
    }

    public async Task StartAsync(CancellationToken externalCt = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("A run is already in progress.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        IsRunning = true;
        IsPaused = false;

        // Notify commands that their CanExecute state has changed
        ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));

        // Check if we're continuing from a checkpoint
        var checkpointStatus = Config.Run.ContinueFromCheckpoint ? " (continuing from checkpoint)" : "";
        StatusLine = $"Starting two-phase evaluation{checkpointStatus}...";
        OnPropertyChanged(nameof(StatusLine));
        Results.Clear();
        CompletedCount = 0;
        OnPropertyChanged(nameof(CompletedCount));

        // Get total count from data source
        var totalCount = await _dataSource.GetCountAsync(_cts.Token);
        if (totalCount.HasValue)
        {
            TotalCount = totalCount.Value;
            OnPropertyChanged(nameof(TotalCount));
        }

        try
        {
            _logger.LogInformation("TwoPhaseEvalRunViewModel: Starting run '{RunName}'{CheckpointStatus}",
                Config.Run.RunName ?? "(unnamed)",
                Config.Run.ContinueFromCheckpoint ? " (continuing from checkpoint)" : "");

            // Start primary server if managed (before creating orchestrator so UI shows progress)
            if (Config.Server.Manage != false && _serverLifecycle != null)
            {
                StatusLine = $"Starting primary llama-server at {DateTimeOffset.Now:HH:mm:ss}...";
                OnPropertyChanged(nameof(StatusLine));
                var startResult = await _serverLifecycle.StartAsync(
                    Config.Server,
                    Config.LlamaServer,
                    _cts.Token);

                if (startResult.IsFailed)
                {
                    throw new InvalidOperationException($"Failed to start primary server: {startResult.Errors[0].Message}");
                }

                var primaryServerInfo = startResult.Value;
                _logger.LogInformation("Primary llama-server started at {BaseUrl}", primaryServerInfo.BaseUrl);

                // Save the resolved binary path for resume capability
                if (primaryServerInfo.BinaryPath is not null && _collector is PersistentResultCollector persistentCollector)
                {
                    await persistentCollector.SaveServerBinaryPathAsync("primary", primaryServerInfo.BinaryPath, _cts.Token);
                }

                // Create HTTP client for primary server
                var primaryHttpClient = CreateHttpClient(primaryServerInfo);
                var primaryClientLogger = _loggerFactory.CreateLogger<LlamaServerClient>();
                var maxConcurrent = Config.Run?.MaxConcurrentEvals ?? 4;
                _primaryClient = new LlamaServerClient(primaryServerInfo, primaryHttpClient, primaryClientLogger, maxConcurrent);

                // Initialize semaphore based on actual server slot count
                await _primaryClient.InitializeSemaphoreFromServerAsync(_cts.Token);

                // Store slot count for orchestrator
                _primarySlotCount = Config.Run?.MaxConcurrentEvals ?? primaryServerInfo.TotalSlots;
            }

            // Create the primary phase orchestrator now that server is ready
            // Use _primaryPipeline which doesn't have JudgeStage if judge is locally managed
            var orchestratorLogger = _loggerFactory.CreateLogger<PipelineOrchestrator>();
            _primaryOrchestrator = await PipelineOrchestratorFactory.CreatePrimaryAsync(
                _dataSource,
                _primaryPipeline,
                _evalSet,
                Config,
                _primaryClient!,
                _collector,
                _progress,
                orchestratorLogger,
                _cts.Token);

            // Phase 1: Primary evaluation
            StatusLine = "Phase 1: Primary evaluation...";
            OnPropertyChanged(nameof(StatusLine));
            await RunPrimaryPhaseAsync(_cts.Token);
            _primaryPhaseComplete = true;

            // Phase 2: Judge evaluation
            StatusLine = "Phase 1 complete at {DateTimeOffset.Now:HH:mm:ss}. Starting judge server...";
            OnPropertyChanged(nameof(StatusLine));
            await RunJudgePhaseAsync(_cts.Token);

            HadFailures = Results.Any(static r => !r.Succeeded);
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

            // Stop all servers and close database when the run completes
            await StopAllServersAsync();
            await _collector.DisposeAsync();
            _cts.Dispose();
            _cts = null;

            if (!StatusLine.StartsWith("Complete") && !StatusLine.StartsWith("Error") && !StatusLine.StartsWith("Done") && !StatusLine.StartsWith("Cancelled"))
            {
                var completionTime = DateTimeOffset.Now;
                StatusLine = $"Stopped at {completionTime:HH:mm:ss}";
                OnPropertyChanged(nameof(StatusLine));
            }
        }
    }

    private async Task RunPrimaryPhaseAsync(CancellationToken ct)
    {
        var maxConcurrent = Config.Run?.MaxConcurrentEvals ?? _primarySlotCount;

        try
        {
            if (_primaryOrchestrator != null) await _primaryOrchestrator.RunAsync(maxConcurrent, ct);

            _logger.LogInformation("Primary phase completed");
            StatusLine = "Primary phase complete. Preparing judge phase...";
            OnPropertyChanged(nameof(StatusLine));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Primary phase failed");
            throw;
        }
    }

    private async Task RunJudgePhaseAsync(CancellationToken ct)
    {
        // Stop primary server before starting judge (saves resources)
        StatusLine = "Stopping primary llama-server...";
        OnPropertyChanged(nameof(StatusLine));

        // Actually stop the primary server now, not just at the end
        try
        {
            await _serverLifecycle.DisposeAsync();
            _logger.LogInformation("Primary llama-server stopped before judge phase");
            StatusLine = "Primary server stopped. Starting judge server...";
            OnPropertyChanged(nameof(StatusLine));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping primary server before judge phase");
        }

        // Start judge server
        LlamaServerClient? judgeClient = null;

        if (Config.Judge is { Manage: true } judgeConfig)
        {
            StatusLine = "Starting judge llama-server...";
            OnPropertyChanged(nameof(StatusLine));

            var judgeSettings = judgeConfig.ServerSettings ?? new LlamaServerSettings();
            var judgeServerConfig = judgeConfig.ServerConfig;

            // Ensure judge uses different port than primary
            if (Config.Server.Manage != false && judgeServerConfig.Port == Config.Server.Port)
            {
                judgeServerConfig = judgeServerConfig with { Port = judgeServerConfig.Port + 1 };
            }

            // Start judge server and track its lifecycle for proper shutdown
            var judgeStartResult = await _serverLifecycle.StartAsync(
                judgeServerConfig,
                judgeSettings,
                ct);

            if (judgeStartResult.IsFailed)
            {
                throw new InvalidOperationException($"Failed to start judge server: {judgeStartResult.Errors[0].Message}");
            }

            var judgeServerInfo = judgeStartResult.Value;
            _logger.LogInformation("Judge llama-server started at {BaseUrl}", judgeServerInfo.BaseUrl);

            // Save the resolved binary path for resume capability
            if (judgeServerInfo.BinaryPath is not null && _collector is PersistentResultCollector persistentCollector)
            {
                await persistentCollector.SaveServerBinaryPathAsync("judge", judgeServerInfo.BinaryPath, ct);
            }

            var judgeHttpClient = CreateHttpClient(judgeServerInfo);
            var judgeClientLogger = _loggerFactory.CreateLogger<LlamaServerClient>();
            var maxConcurrent = Config.Run?.MaxConcurrentEvals ?? 4;
            judgeClient = new LlamaServerClient(judgeServerInfo, judgeHttpClient, judgeClientLogger, maxConcurrent);

            // Initialize semaphore based on actual server slot count
            await judgeClient.InitializeSemaphoreFromServerAsync(ct);

            // Store slot count for orchestrator
            _judgeSlotCount = Config.Run?.MaxConcurrentEvals ?? judgeServerInfo.TotalSlots;
        }

        try
        {
            // Create judge phase orchestrator
            var orchestratorLogger = _loggerFactory.CreateLogger<PipelineOrchestrator>();
            var judgeProgress = new Progress<EvalProgress>();
            judgeProgress.ProgressChanged += OnProgressChanged;

            var judgeOrchestrator = await PipelineOrchestratorFactory.CreateJudgeAsync(
                _dataSource,
                _pipeline,
                _evalSet,
                Config,
                judgeClient!,
                _collector,
                judgeProgress,
                orchestratorLogger,
                ct);

            StatusLine = "Phase 2: Judge evaluation...";
            OnPropertyChanged(nameof(StatusLine));
            var maxConcurrent = Config.Run?.MaxConcurrentEvals ?? _judgeSlotCount;
            await judgeOrchestrator.RunAsync(maxConcurrent, ct);

            _logger.LogInformation("Judge phase completed");
        }
        finally
        {
            _judgePhaseComplete = true;
            var completionTime = DateTimeOffset.Now;
            StatusLine = $"Done at {completionTime:HH:mm:ss}";
            OnPropertyChanged(nameof(StatusLine));

            // Stop judge server immediately after judge phase completes
            if (Config.Judge is { Manage: true })
            {
                try
                {
                    await _serverLifecycle.DisposeAsync();
                    _logger.LogInformation("Judge llama-server stopped after judge phase");
                    StatusLine = "Judge server stopped. Finalizing results...";
                    OnPropertyChanged(nameof(StatusLine));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping judge server after judge phase");
                }
            }

            _logger.LogDebug("Judge phase finally block completed");
        }
    }

    private async Task StopAllServersAsync(bool force = false)
    {
        // Stop primary server if it wasn't already stopped (e.g., if judge phase wasn't run)
        if (force || (Config.Server.Manage != false && _serverLifecycle != null && !_primaryPhaseComplete))
        {
            try
            {
                if (_serverLifecycle != null)
                {
                    await _serverLifecycle.DisposeAsync();
                    _logger.LogInformation("Primary llama-server stopped");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping primary server");
            }
        }

        // Note: Judge server is already stopped in RunJudgePhaseAsync finally block.
        // We don't need to dispose it again here.
        if (Config.Judge is { Manage: true } && _judgePhaseComplete)
        {
            _logger.LogDebug("Judge server already stopped in RunJudgePhaseAsync, skipping disposal in StopAllServersAsync");
        }
    }

    private void OnProgressChanged(object? sender, EvalProgress e)
    {
        // Update all properties on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CompletedCount = e.CompletedCount;
            EstimatedRemainingSeconds = e.EstimatedRemainingSeconds;
            
            // Calculate moving average tokens/sec from last 10 completed items
            AverageTokensPerSecond = CalculateMovingAverageTokensPerSecond();

            var results = _collector.GetResults();

            Results.Clear();
            foreach (var result in results)
            {
                Results.Add(new EvalResultViewModel(result));
            }

            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(EstimatedRemainingSeconds));
            OnPropertyChanged(nameof(AverageTokensPerSecond));
            OnPropertyChanged(nameof(Results));
            OnPropertyChanged(nameof(EarlyCompletions));
            OnPropertyChanged(nameof(HasMoreEarlyCompletions));
            OnPropertyChanged(nameof(LoadMoreEarlyCompletionsCommand));

            // Notify command that CanExecute may have changed
            if (LoadMoreEarlyCompletionsCommand is RelayCommand cmd)
            {
                cmd.NotifyCanExecuteChanged();
            }
        });
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

    // ─── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    // ─── IAsyncDisposable & IDisposable ───────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Unsubscribe from events
        _progress.ProgressChanged -= OnProgressChanged;
        _serverLifecycle.ServerErrorReceived -= OnServerErrorReceived;
        _serverLifecycle.LoadingProgressChanged -= OnServerLoadingProgressChanged;

        // Dispose async resources
        try
        {
            await _collector.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing persistent result collector");
        }

        // Stop all servers
        await StopAllServersAsync(true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Unsubscribe from events
        _progress.ProgressChanged -= OnProgressChanged;

        // Note: We cannot await async operations here in synchronous Dispose.
        // Callers should prefer DisposeAsync when possible.
        // For synchronous disposal, we can only dispose synchronous resources.
        _logger.LogWarning("Synchronous Dispose called on TwoPhaseEvalRunViewModel. Prefer DisposeAsync for proper cleanup.");
    }
}
