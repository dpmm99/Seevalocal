using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.UI.Commands;
using Seevalocal.UI.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Seevalocal.UI.ViewModels;

/// <summary>
/// View-model for eval generation runs.
/// Provides progress tracking, pause/resume, and cancel functionality.
/// Reuses the Run Dashboard pattern.
/// </summary>
public sealed class EvalGenRunViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IEvalGenService _evalGenService;
    private readonly ILogger<EvalGenRunViewModel> _logger;
    private EvalGenRun? _run;
    private bool _disposed;

    // ─── Properties ───────────────────────────────────────────────────────────

    public string RunName => _run?.RunName ?? "";
    public string Id => _run?.Id ?? "";
    public EvalGenConfig Config => _run?.Config ?? new EvalGenConfig();

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

    private bool _isPausing;  // True when waiting for in-flight items to complete
    public bool IsPausing
    {
        get => _isPausing;
        private set => SetField(ref _isPausing, value);
    }

    public string PauseButtonLabel => IsPaused ? "▶️ Resume" : "⏸️ Pause";

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        private set => SetField(ref _isCompleted, value);
    }

    private bool _isCancelled;
    public bool IsCancelled
    {
        get => _isCancelled;
        private set => SetField(ref _isCancelled, value);
    }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    private int _categoriesGenerated;
    public int CategoriesGenerated
    {
        get => _categoriesGenerated;
        private set => SetField(ref _categoriesGenerated, value);
    }

    private int _targetCategories;
    public int TargetCategories
    {
        get => _targetCategories;
        private set => SetField(ref _targetCategories, value);
    }

    private int _problemsGenerated;
    public int ProblemsGenerated
    {
        get => _problemsGenerated;
        private set => SetField(ref _problemsGenerated, value);
    }

    private int _targetProblems;
    public int TargetProblems
    {
        get => _targetProblems;
        private set => SetField(ref _targetProblems, value);
    }

    private int _problemsFleshedOut;
    public int ProblemsFleshedOut
    {
        get => _problemsFleshedOut;
        private set => SetField(ref _problemsFleshedOut, value);
    }

    private string _statusLine = "Ready";
    public string StatusLine
    {
        get => _statusLine;
        private set => SetField(ref _statusLine, value);
    }

    private double _averageTokensPerSecond;
    public double AverageTokensPerSecond
    {
        get => _averageTokensPerSecond;
        private set => SetField(ref _averageTokensPerSecond, value);
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        private set => SetField(ref _error, value);
    }

    private EvalGenPhase _currentPhase;
    public EvalGenPhase CurrentPhase
    {
        get => _currentPhase;
        private set => SetField(ref _currentPhase, value);
    }

    private ObservableCollection<GeneratedCategoryViewModel> _categories = [];
    public ObservableCollection<GeneratedCategoryViewModel> Categories
    {
        get => _categories;
        private set => SetField(ref _categories, value);
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    public ICommand PauseCommand { get; }
    public ICommand CancelCommand { get; }

    // ─── Events ───────────────────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public EvalGenRunViewModel(
        IEvalGenService evalGenService,
        ILogger<EvalGenRunViewModel> logger)
    {
        _evalGenService = evalGenService;
        _logger = logger;

        PauseCommand = new RelayCommand(TogglePause, () => IsRunning && !IsCompleted);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning && !IsCompleted);
    }

    // ─── Public Methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Start a new eval generation run.
    /// </summary>
    public async Task StartAsync(EvalGenConfig config, JudgeConfig? judgeConfig, CancellationToken cancellationToken, EvalGenCheckpointCollector? existingCollector = null)
    {
        if (_run?.IsRunning == true && !_run.IsCompleted)
        {
            _logger.LogWarning("Eval generation already in progress");
            return;
        }

        try
        {
            _logger.LogInformation("Starting eval generation: {RunName}, ContinueFromCheckpoint={ContinueFromCheckpoint}", config.RunName, config.ContinueFromCheckpoint);

            _run = await _evalGenService.GenerateAsync(config, judgeConfig, cancellationToken);

            // If we have an existing collector (from checkpoint resume), use it
            if (existingCollector != null)
            {
                _run.Collector = existingCollector;
            }

            // Subscribe to progress events
            _run.ProgressChanged += OnProgressChanged;
            _run.RunCompleted += OnRunCompleted;

            // Update initial state BEFORE waiting (so commands are enabled immediately)
            IsRunning = true;
            IsCompleted = false;
            IsCancelled = false;
            IsPaused = false;
            StatusLine = "Starting...";
            CurrentPhase = EvalGenPhase.GeneratingCategories;
            TargetCategories = config.TargetCategoryCount;
            TargetProblems = config.TargetCategoryCount * config.TargetProblemsPerCategory;

            // Load existing categories and progress from the shared collector
            if (_run.Collector != null)
            {
                LoadCategoriesFromCheckpoint(_run.Collector);
                
                // Load progress from checkpoint if resuming
                if (config.ContinueFromCheckpoint)
                {
                    var categories = _run.Collector.GetCategories();
                    var problems = _run.Collector.GetProblems();
                    var fleshedOutCount = problems.Count(p => p.IsComplete);
                    
                    CategoriesGenerated = categories.Count;
                    ProblemsGenerated = problems.Count;
                    ProblemsFleshedOut = fleshedOutCount;
                    
                    _logger.LogInformation("Loaded from checkpoint: {Categories} categories, {Problems} problems ({FleshedOut} fleshed out)",
                        CategoriesGenerated, ProblemsGenerated, ProblemsFleshedOut);
                    
                    StatusLine = $"Resumed from checkpoint: {CategoriesGenerated}/{TargetCategories} categories, {ProblemsGenerated}/{TargetProblems} problems";
                }
            }

            // Notify commands to re-evaluate CanExecute
            ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();

            OnPropertyChanged(nameof(RunName));
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(CategoriesGenerated));
            OnPropertyChanged(nameof(ProblemsGenerated));
            OnPropertyChanged(nameof(ProblemsFleshedOut));
            OnPropertyChanged(nameof(StatusLine));

            // Wait for completion
            await _run.WaitAsync();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Eval generation cancelled");
            IsCancelled = true;
            IsRunning = false;
            StatusLine = "Cancelled";
            ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during eval generation");
            Error = ex.Message;
            IsRunning = false;
            StatusLine = "Error";
            ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Continue a run from checkpoint.
    /// Returns the loaded config (including phase prompts) for UI display.
    /// </summary>
    public async Task<EvalGenConfig?> ContinueFromCheckpointAsync(string checkpointDbPath, CancellationToken cancellationToken)
    {
        // Load config from checkpoint
        var collector = new EvalGenCheckpointCollector(checkpointDbPath);
        var savedParams = await collector.LoadStartupParametersAsync(cancellationToken);

        if (savedParams == null)
        {
            Error = "No checkpoint found at specified path";
            await collector.DisposeAsync();
            return null;
        }

        var config = savedParams.Value.Config with
        {
            ContinueFromCheckpoint = true,
            CheckpointDatabasePath = checkpointDbPath
        };

        // Load existing categories from checkpoint
        LoadCategoriesFromCheckpoint(collector);

        // Use saved judge config if available
        var judgeConfig = savedParams.Value.JudgeConfig;

        // Store collector for use after StartAsync
        var checkpointCollector = collector;

        await StartAsync(config, judgeConfig, cancellationToken, checkpointCollector);

        // Don't dispose collector - it's now owned by the run
        return config;
    }

    /// <summary>
    /// Load categories from checkpoint database.
    /// Preserves IsExpanded state for existing categories.
    /// </summary>
    private void LoadCategoriesFromCheckpoint(EvalGenCheckpointCollector collector)
    {
        try
        {
            // Use GetCategories for fast cache access
            var categories = collector.GetCategories();

            // Preserve IsExpanded state for existing categories
            var existingExpandedStates = Categories.ToDictionary(c => c.Id, c => c.IsExpanded);

            Categories.Clear();
            foreach (var category in categories)
            {
                var isExpanded = existingExpandedStates.GetValueOrDefault(category.Id, false);
                Categories.Add(new GeneratedCategoryViewModel
                {
                    Id = category.Id,
                    Name = category.Name,
                    ProblemCount = category.Problems.Count,
                    IsExpanded = isExpanded,
                    Problems = new ObservableCollection<GeneratedProblem>(category.Problems)
                });
            }

            _logger.LogDebug("Loaded {Count} categories from checkpoint", Categories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load categories from checkpoint");
        }
    }

    /// <summary>
    /// Refresh progress from the current run.
    /// </summary>
    public void RefreshProgress()
    {
        if (_run != null)
        {
            OnProgressChanged(_run.Progress);
        }
    }

    // ─── Private Methods ──────────────────────────────────────────────────────

    private void TogglePause()
    {
        if (_run == null || !_run.IsRunning || _run.IsCompleted)
            return;

        if (_run.IsPaused)
        {
            _run.Resume();
            IsPaused = false;
            IsPausing = false;
            StatusLine = "Running";
            _logger.LogInformation("Resuming eval generation");
        }
        else
        {
            _run.Pause();
            IsPaused = true;
            IsPausing = true;
            StatusLine = "Pausing...";
            _logger.LogInformation("Pausing eval generation");
            
            // Schedule a check to see if pause has completed
            // Since eval gen doesn't track in-flight items, we poll the run's pause state
            CheckPauseCompletedAsync();
        }

        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsPausing));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(PauseButtonLabel));
    }

    private async void CheckPauseCompletedAsync()
    {
        // Poll until the run confirms it's fully paused
        while (IsPausing && IsPaused && _run?.IsRunning == true)
        {
            await Task.Delay(500);
            // For eval gen, we consider it paused immediately since there's no in-flight tracking
            // Just update status to "Paused" after a brief delay
            IsPausing = false;
            StatusLine = "Paused";
            OnPropertyChanged(nameof(StatusLine));
            OnPropertyChanged(nameof(PauseButtonLabel));
        }
    }

    private void Cancel()
    {
        if (_run == null || !_run.IsRunning || _run.IsCompleted)
            return;

        _run.Cancel();
        StatusLine = "Cancelling...";
        _logger.LogInformation("Cancelling eval generation");
        OnPropertyChanged(nameof(StatusLine));
    }

    private void OnProgressChanged(EvalGenProgress progress)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Track old values to detect changes
            var oldCategoriesGenerated = CategoriesGenerated;
            var oldProblemsGenerated = ProblemsGenerated;
            var oldProblemsFleshedOut = ProblemsFleshedOut;
            var oldPhase = CurrentPhase;

            ProgressPercent = progress.OverallProgressPercent;
            CategoriesGenerated = progress.CategoriesGenerated;
            TargetCategories = progress.TargetCategories;
            ProblemsGenerated = progress.ProblemsGenerated;
            TargetProblems = progress.TargetProblems;
            ProblemsFleshedOut = progress.ProblemsFleshedOut;
            CurrentPhase = progress.CurrentPhase;
            StatusLine = progress.StatusMessage;
            AverageTokensPerSecond = _run?.AverageTokensPerSecond ?? 0;

            // Update categories list when category count changes, problem count changes,
            // problems are fleshed out, or when phase changes
            if (progress.CategoriesGenerated != oldCategoriesGenerated ||
                progress.ProblemsGenerated != oldProblemsGenerated ||
                progress.ProblemsFleshedOut != oldProblemsFleshedOut ||
                progress.CurrentPhase != oldPhase)
            {
                // Reload categories from the run's shared collector
                var collector = _run?.Collector;
                if (collector != null)
                {
                    LoadCategoriesFromCheckpoint(collector);
                }
            }

            // Notify commands to re-evaluate CanExecute
            ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();

            _logger.LogDebug(
                "Progress: Phase={Phase}, Categories={CatGen}/{CatTarget}, Problems={ProbGen}/{ProbTarget}, FleshedOut={FleshedOut}, TokensSec={TokensSec:F1}",
                progress.CurrentPhase,
                progress.CategoriesGenerated,
                progress.TargetCategories,
                progress.ProblemsGenerated,
                progress.TargetProblems,
                progress.ProblemsFleshedOut,
                AverageTokensPerSecond);
        });
    }

    private void OnRunCompleted()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            IsCompleted = true;
            IsCancelled = _run?.IsCancelled ?? false;
            Error = _run?.Error;

            // Check if there were any failures during generation
            var hadFailures = (_run?.TotalCategoriesFailed ?? 0) > 0 ||
                              (_run?.TotalProblemsFailed ?? 0) > 0 ||
                              (_run?.TotalFleshOutFailed ?? 0) > 0;

            if (_run?.IsCancelled == true)
            {
                StatusLine = "Cancelled";
            }
            else if (!string.IsNullOrEmpty(_run?.Error))
            {
                StatusLine = "Error";
            }
            else if (hadFailures)
            {
                StatusLine = $"Completed with {_run?.TotalCategoriesFailed + _run?.TotalProblemsFailed + _run?.TotalFleshOutFailed} failures";
            }
            else
            {
                StatusLine = "Completed";
            }

            // Notify commands to re-evaluate CanExecute (disable pause/cancel buttons)
            ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();

            _logger.LogInformation(
                "Eval generation completed: RunName={RunName}, Cancelled={Cancelled}, Error={Error}, Failures={Failures}",
                _run?.RunName,
                _run?.IsCancelled,
                _run?.Error,
                _run?.TotalCategoriesFailed + _run?.TotalProblemsFailed + _run?.TotalFleshOutFailed);
        });
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ─── Disposal ─────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            if (_run != null)
            {
                _run.ProgressChanged -= OnProgressChanged;
                _run.RunCompleted -= OnRunCompleted;
            }

            await Task.CompletedTask;
        }
    }
}

/// <summary>
/// View-model for a generated category.
/// </summary>
public sealed class GeneratedCategoryViewModel : INotifyPropertyChanged
{
    private string _name = "";
    private int _problemCount;
    private bool _isExpanded;
    private ObservableCollection<GeneratedProblem> _problems = [];

    public string Id { get; init; } = "";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public int ProblemCount
    {
        get => _problemCount;
        set => SetField(ref _problemCount, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public ObservableCollection<GeneratedProblem> Problems
    {
        get => _problems;
        set => SetField(ref _problems, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
