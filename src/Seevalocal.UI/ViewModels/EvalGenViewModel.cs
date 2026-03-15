using Avalonia.Threading;
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
/// ViewModel for the Generate Evaluation Set feature.
/// Provides configuration inputs and progress display for agentic eval generation.
/// </summary>
public sealed class EvalGenViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IEvalGenService _evalGenService;
    private readonly EvalGenRunViewModel _runViewModel;
    private readonly IFilePickerService? _filePickerService;
    private readonly ILogger<EvalGenViewModel> _logger;

    private bool _disposed;
    private CancellationTokenSource? _runCts;

    // Configuration properties
    private string _domainPrompt = "";
    private string _contextPrompt = "";
    private string _systemPrompt = "";
    private int _targetCategoryCount = 10;
    private int _targetProblemsPerCategory = 5;
    private string _outputDirectoryPath = "";
    private string _checkpointDatabasePath = "";

    // State properties
    private bool _showConfigurationForm = true;
    private bool _showProgress;
    private bool _showCheckpointOption;
    private bool _isRunning;
    private bool _isPaused;
    private bool _isCompleted;
    private bool _isCancelled;
    private string? _error;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Commands
    public ICommand StartGenerationCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseOutputDirectoryCommand { get; }
    public ICommand BrowseCheckpointCommand { get; }
    public ICommand OpenOutputDirectoryCommand { get; }
    public ICommand ResetCommand { get; }

    // Configuration Properties
    public string DomainPrompt
    {
        get => _domainPrompt;
        set => SetField(ref _domainPrompt, value);
    }

    public string ContextPrompt
    {
        get => _contextPrompt;
        set => SetField(ref _contextPrompt, value);
    }

    public string SystemPrompt
    {
        get => _systemPrompt;
        set => SetField(ref _systemPrompt, value);
    }

    public int TargetCategoryCount
    {
        get => _targetCategoryCount;
        set => SetField(ref _targetCategoryCount, value);
    }

    public int TargetProblemsPerCategory
    {
        get => _targetProblemsPerCategory;
        set => SetField(ref _targetProblemsPerCategory, value);
    }

    public string OutputDirectoryPath
    {
        get => _outputDirectoryPath;
        set => SetField(ref _outputDirectoryPath, value);
    }

    public string CheckpointDatabasePath
    {
        get => _checkpointDatabasePath;
        set => SetField(ref _checkpointDatabasePath, value);
    }

    // State Properties (delegated to RunViewModel where applicable)
    public bool ShowConfigurationForm
    {
        get => _showConfigurationForm;
        private set => SetField(ref _showConfigurationForm, value);
    }

    public bool ShowProgress
    {
        get => _showProgress;
        private set => SetField(ref _showProgress, value);
    }

    public bool ShowCheckpointOption
    {
        get => _showCheckpointOption;
        set => SetField(ref _showCheckpointOption, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetField(ref _isRunning, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set => SetField(ref _isPaused, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set => SetField(ref _isCompleted, value);
    }

    public bool IsCancelled
    {
        get => _isCancelled;
        private set => SetField(ref _isCancelled, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetField(ref _error, value);
    }

    // Progress Properties (delegated from RunViewModel)
    public double ProgressPercent => _runViewModel.ProgressPercent;
    public int CategoriesGenerated => _runViewModel.CategoriesGenerated;
    public int TargetCategories => _runViewModel.TargetCategories;
    public int ProblemsGenerated => _runViewModel.ProblemsGenerated;
    public int TargetProblems => _runViewModel.TargetProblems;
    public int ProblemsFleshedOut => _runViewModel.ProblemsFleshedOut;
    public string StatusLine => _runViewModel.StatusLine;
    public EvalGenPhase CurrentPhase => _runViewModel.CurrentPhase;
    public ObservableCollection<GeneratedCategoryViewModel> Categories => _runViewModel.Categories;

    public EvalGenViewModel(
        IEvalGenService evalGenService,
        EvalGenRunViewModel runViewModel,
        IFilePickerService? filePickerService,
        ILogger<EvalGenViewModel> logger,
        Func<JudgeConfig?> getJudgeConfig)
    {
        _evalGenService = evalGenService;
        _runViewModel = runViewModel;
        _filePickerService = filePickerService;
        _logger = logger;
        _getJudgeConfig = getJudgeConfig;

        // Subscribe to RunViewModel property changes
        _runViewModel.PropertyChanged += OnRunViewModelPropertyChanged;

        // Initialize commands
        StartGenerationCommand = new RelayCommand(StartGenerationAsync, CanStartGeneration);
        TogglePauseCommand = new RelayCommand(TogglePause, () => IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        BrowseOutputDirectoryCommand = new RelayCommand(BrowseOutputDirectory);
        BrowseCheckpointCommand = new RelayCommand(BrowseCheckpoint);
        OpenOutputDirectoryCommand = new RelayCommand(OpenOutputDirectory);
        ResetCommand = new RelayCommand(Reset);

        // Set default output directory (with fallback if current directory is not writable)
        try
        {
            OutputDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "generated_evals", $"eval_{DateTime.Now:yyyyMMdd_HHmmss}");
        }
        catch
        {
            // Fallback to temp directory if current directory is not writable
            OutputDirectoryPath = Path.Combine(Path.GetTempPath(), "seevalocal_evals", $"eval_{DateTime.Now:yyyyMMdd_HHmmss}");
        }
    }

    private readonly Func<JudgeConfig?> _getJudgeConfig;

    private bool CanStartGeneration() => !IsRunning && !string.IsNullOrEmpty(OutputDirectoryPath);

    private async void StartGenerationAsync()
    {
        try
        {
            _logger.LogInformation("Starting eval generation from UI");

            _runCts = new CancellationTokenSource();

            // Build config
            var config = new EvalGenConfig
            {
                RunName = $"eval_gen_{DateTime.Now:yyyyMMdd_HHmmss}",
                OutputDirectoryPath = OutputDirectoryPath,
                TargetCategoryCount = TargetCategoryCount,
                TargetProblemsPerCategory = TargetProblemsPerCategory,
                DomainPrompt = DomainPrompt,
                ContextPrompt = ContextPrompt,
                SystemPrompt = SystemPrompt,
                ContinueFromCheckpoint = !string.IsNullOrEmpty(CheckpointDatabasePath),
                CheckpointDatabasePath = string.IsNullOrEmpty(CheckpointDatabasePath) ? null : CheckpointDatabasePath
            };

            // Get judge config from settings (via factory function)
            var judgeConfig = _getJudgeConfig();

            // Update UI state
            ShowConfigurationForm = false;
            ShowProgress = true;
            IsRunning = true;
            IsCompleted = false;
            IsCancelled = false;
            Error = null;

            // Start the run
            if (!string.IsNullOrEmpty(CheckpointDatabasePath))
            {
                await _runViewModel.ContinueFromCheckpointAsync(CheckpointDatabasePath, _runCts.Token);
            }
            else
            {
                await _runViewModel.StartAsync(config, judgeConfig, _runCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Eval generation cancelled from UI");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting eval generation");
            Error = ex.Message;
            IsRunning = false;
        }
    }

    private void TogglePause()
    {
        if (_runViewModel.IsRunning && !_runViewModel.IsCompleted)
        {
            if (_runViewModel.IsPaused)
                _runViewModel.PauseCommand.Execute(null);
            else
                _runViewModel.PauseCommand.Execute(null);
        }
    }

    private void Cancel()
    {
        if (_runViewModel.IsRunning && !_runViewModel.IsCompleted)
        {
            _runViewModel.CancelCommand.Execute(null);
        }
    }

    private async void BrowseOutputDirectory()
    {
        if (_filePickerService == null)
            return;

        var path = await _filePickerService.ShowOpenFolderDialogAsync("Select Output Directory", OutputDirectoryPath);
        if (!string.IsNullOrEmpty(path))
        {
            OutputDirectoryPath = path;
        }
    }

    private async void BrowseCheckpoint()
    {
        if (_filePickerService == null)
            return;

        var path = await _filePickerService.ShowOpenFileDialogAsync("Select Checkpoint Database", "Database Files|*.db");
        if (!string.IsNullOrEmpty(path))
        {
            CheckpointDatabasePath = path;
        }
    }

    private void OpenOutputDirectory()
    {
        if (!string.IsNullOrEmpty(OutputDirectoryPath) && Directory.Exists(OutputDirectoryPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = OutputDirectoryPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening output directory");
            }
        }
    }

    private void Reset()
    {
        ShowConfigurationForm = true;
        ShowProgress = false;
        IsRunning = false;
        IsCompleted = false;
        IsCancelled = false;
        Error = null;
        
        // Reset to new default directory
        OutputDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "generated_evals", $"eval_{DateTime.Now:yyyyMMdd_HHmmss}");
        CheckpointDatabasePath = "";
    }

    private void OnRunViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(EvalGenRunViewModel.ProgressPercent):
                case nameof(EvalGenRunViewModel.CategoriesGenerated):
                case nameof(EvalGenRunViewModel.TargetCategories):
                case nameof(EvalGenRunViewModel.ProblemsGenerated):
                case nameof(EvalGenRunViewModel.TargetProblems):
                case nameof(EvalGenRunViewModel.ProblemsFleshedOut):
                case nameof(EvalGenRunViewModel.StatusLine):
                case nameof(EvalGenRunViewModel.CurrentPhase):
                case nameof(EvalGenRunViewModel.Categories):
                    OnPropertyChanged(e.PropertyName);
                    break;
                case nameof(EvalGenRunViewModel.IsRunning):
                    IsRunning = _runViewModel.IsRunning;
                    break;
                case nameof(EvalGenRunViewModel.IsPaused):
                    IsPaused = _runViewModel.IsPaused;
                    break;
                case nameof(EvalGenRunViewModel.IsCompleted):
                    IsCompleted = _runViewModel.IsCompleted;
                    break;
                case nameof(EvalGenRunViewModel.IsCancelled):
                    IsCancelled = _runViewModel.IsCancelled;
                    break;
                case nameof(EvalGenRunViewModel.Error):
                    Error = _runViewModel.Error;
                    break;
            }
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

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _runCts?.Cancel();
            _runCts?.Dispose();
            _runViewModel.PropertyChanged -= OnRunViewModelPropertyChanged;
            await _runViewModel.DisposeAsync();
        }
    }
}
