using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.UI.Commands;
using Seevalocal.UI.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Seevalocal.UI.ViewModels;

// ─── Wizard steps ────────────────────────────────────────────────────────────

public enum WizardStepKind
{
    ContinueRun,
    PipelineSelection,
    ModelAndServer,
    PerformanceSettings,
    EvaluationDataset,
    FieldMapping,
    PipelineConfiguration,
    Scoring,
    Output,
    ReviewAndRun
}

/// <summary>
/// View model for the guided setup wizard.
/// All wizard state is stored in a single <see cref="WizardState"/> backing object
/// rather than hundreds of duplicated private fields.
/// </summary>
public sealed partial class WizardViewModel : IWizardViewModel
{
    private readonly IFilePickerService? _filePicker;
    private readonly IToastService? _toastService;
    private readonly ILogger<WizardViewModel>? _logger;
    private WizardStepKind _currentStep = WizardStepKind.ContinueRun;

    // Single backing object for all wizard state — replaces ~150 private fields.
    private WizardState _state = WizardState.CreateDefaults();

    // Track which fields have been explicitly edited by the user.
    private readonly HashSet<string> _editedFields = [];

    // Checkpoint metadata (not part of config state)
    private string? _checkpointEvalSetId;

    // Detected fields from data file (for ComboBox suggestions)
    private List<string>? _detectedFields;

    // ─── Constructor & commands ───────────────────────────────────────────────

    public ICommand GoBackCommand { get; }
    public ICommand GoForwardCommand { get; }
    public ICommand ExportScriptCommand { get; }
    public ICommand ResetToDefaultsCommand { get; }

    // Browse commands
    public ICommand BrowseLocalModelCommand { get; }
    public ICommand BrowseDataFileCommand { get; }
    public ICommand BrowsePromptDirCommand { get; }
    public ICommand BrowseExpectedDirCommand { get; }
    public ICommand BrowseOutputDirCommand { get; }
    public ICommand BrowseJudgeModelCommand { get; }
    public ICommand BrowseCheckpointDbCommand { get; }
    public ICommand BrowseBuildScriptCommand { get; }

    // Test connection commands
    public ICommand TestConnectionCommand { get; }
    public ICommand TestJudgeConnectionCommand { get; }

    // These are set by MainWindow after construction
    public Action? OnExportScript { get; set; }
    public Func<Task>? OnStartRun { get; set; }
    public Action<string>? OnShowNotification { get; set; }

    // IWizardViewModel interface properties
    string? IWizardViewModel.OutputDirectoryPath => OutputDir;
    string? IWizardViewModel.RunName => _state.RunName;
    ShellTarget? IWizardViewModel.ShellTarget => _state.ShellTarget;

    /// <summary>
    /// Direct access to the backing state object.
    /// Used by <see cref="WizardReflection"/> for bulk config apply operations.
    /// </summary>
    internal WizardState State => _state;

    public WizardViewModel(IFilePickerService? filePicker = null, IToastService? toastService = null, ILogger<WizardViewModel>? logger = null)
    {
        _filePicker = filePicker;
        _toastService = toastService;
        _logger = logger;

        GoBackCommand = new RelayCommand(GoBack, () => CanGoBack);
        GoForwardCommand = new RelayCommand(async () => await GoForwardAsync(), () => CanGoForward);
        ExportScriptCommand = new RelayCommand(() => OnExportScript?.Invoke());
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);

        BrowseLocalModelCommand = new RelayCommand<string>(async (param) => await BrowseLocalModelAsync(param));
        BrowseDataFileCommand = new RelayCommand(async () => await BrowseDataFileAsync());
        BrowsePromptDirCommand = new RelayCommand(async () => await BrowsePromptDirAsync());
        BrowseExpectedDirCommand = new RelayCommand(async () => await BrowseExpectedDirAsync());
        BrowseOutputDirCommand = new RelayCommand(async () => await BrowseOutputDirAsync());
        BrowseJudgeModelCommand = new RelayCommand<string>(async (param) => await BrowseJudgeModelAsync(param));

        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync());
        TestJudgeConnectionCommand = new RelayCommand(async () => await TestJudgeConnectionAsync());
        BrowseCheckpointDbCommand = new RelayCommand(async () => await BrowseCheckpointDbAsync());
        BrowseBuildScriptCommand = new RelayCommand(async () => await BrowseBuildScriptAsync());

        OnPropertyChanged(nameof(SelectedJudgeTemplateIndex));
    }

    // ─── Step navigation ──────────────────────────────────────────────────────

    public WizardStepKind CurrentStep
    {
        get => _currentStep;
        set => SetField(ref _currentStep, value);
    }

    public bool CanGoBack => CurrentStep != WizardStepKind.ContinueRun;
    public bool CanGoForward => ValidateCurrentStep().Count == 0;

    public event EventHandler? StepChanged;
    public event EventHandler? ResetToDefaultsCompleted;

    public void GoBack()
    {
        if (!CanGoBack) return;
        CurrentStep = (WizardStepKind)((int)CurrentStep - 1);
        RefreshNavigationState();
    }

    public async Task GoForwardAsync()
    {
        var validationErrors = ValidateCurrentStep();
        if (validationErrors.Count > 0)
        {
            // Show error toast for validation failures
            _toastService?.ShowError($"Configuration error: {validationErrors[0]}");
            return;
        }

        if (CurrentStep == WizardStepKind.ReviewAndRun)
        {
            if (OnStartRun != null) await OnStartRun();
            return;
        }

        var nextStep = (WizardStepKind)((int)CurrentStep + 1);

        // Auto-select shell dialect when navigating to Output step if not already set
        if (nextStep == WizardStepKind.Output && ShellTarget == null)
            ShellTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Core.Models.ShellTarget.PowerShell
                : Core.Models.ShellTarget.Bash;

        CurrentStep = nextStep;
        RefreshNavigationState();

        // Raise event for step change - MainWindow can sync settings on this
        StepChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshNavigationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        ((RelayCommand)GoBackCommand).NotifyCanExecuteChanged();
        ((RelayCommand)GoForwardCommand).NotifyCanExecuteChanged();
    }

    // ─── Validation ───────────────────────────────────────────────────────────

    public List<string> ValidateCurrentStep() => CurrentStep switch
    {
        WizardStepKind.ContinueRun => ValidateContinueRunStep(),
        WizardStepKind.ModelAndServer => ValidateServerStep(),
        WizardStepKind.PerformanceSettings => [],  // No validation - all optional
        WizardStepKind.PipelineSelection => [],  // No validation - pipeline always has a default
        WizardStepKind.EvaluationDataset => ValidateDatasetStep(),
        WizardStepKind.FieldMapping => [],  // No validation - all mappings optional
        WizardStepKind.PipelineConfiguration => [],  // No validation - all config optional
        WizardStepKind.Scoring => ValidateScoringStep(),
        WizardStepKind.Output => [],  // No validation - defaults are fine
        WizardStepKind.ReviewAndRun => [],  // No validation - just a summary
        _ => []
    };

    private List<string> ValidateContinueRunStep()
    {
        if (!_state.ContinueFromCheckpoint) return [];
        if (string.IsNullOrWhiteSpace(_state.CheckpointDatabasePath))
            return ["Checkpoint database file path is required when continuing from a checkpoint."];
        if (!File.Exists(_state.CheckpointDatabasePath))
            return [$"Checkpoint database file does not exist: {_state.CheckpointDatabasePath}"];
        return [];
    }

    private List<string> ValidateServerStep()
    {
        List<string> errors = [];
        if (_state.ManageServer)
        {
            if (_state.UseLocalFile && string.IsNullOrWhiteSpace(_state.LocalModelPath))
                errors.Add("Model file path is required when using a local file.");
            if (!_state.UseLocalFile && string.IsNullOrWhiteSpace(_state.HfRepo))
                errors.Add("HuggingFace repo is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_state.ServerUrl))
                errors.Add("Server URL is required when connecting to an existing server.");
        }
        return errors;
    }

    private List<string> ValidateDatasetStep()
    {
        List<string> errors = [];
        if (_state.UseSingleFileDataSource)
        {
            // Single file mode - file path is required and must exist
            if (string.IsNullOrWhiteSpace(_state.DataFilePath))
                errors.Add("Data file path is required when using single file mode.");
            else if (!File.Exists(_state.DataFilePath))
                errors.Add($"Data file does not exist: {_state.DataFilePath}");
        }
        else
        {
            // Directory mode - prompt directory is required and must exist
            if (string.IsNullOrWhiteSpace(_state.PromptDir))
                errors.Add("Prompt directory path is required when using directory mode.");
            else if (!Directory.Exists(_state.PromptDir))
                errors.Add($"Prompt directory does not exist: {_state.PromptDir}");

            // Expected directory is optional, but if provided must exist
            if (!string.IsNullOrWhiteSpace(_state.ExpectedDir) && !Directory.Exists(_state.ExpectedDir))
                errors.Add($"Expected output directory does not exist: {_state.ExpectedDir}");
        }
        return errors;
    }

    private List<string> ValidateScoringStep()
    {
        List<string> errors = [];
        if (_state.EnableJudge)
        {
            if (_state.JudgeManageServer)
            {
                // Managed judge - model is required
                if (_state.JudgeUseLocalFile && string.IsNullOrWhiteSpace(_state.JudgeLocalModelPath))
                    errors.Add("Judge model file path is required when using a local file.");
                if (!_state.JudgeUseLocalFile && string.IsNullOrWhiteSpace(_state.JudgeHfRepo))
                    errors.Add("Judge HuggingFace repo is required.");
            }
            else
            {
                // External judge - URL is required
                if (string.IsNullOrWhiteSpace(_state.JudgeServerUrl))
                    errors.Add("Judge server URL is required when connecting to an existing server.");
            }
        }
        return errors;
    }

    // ─── Bound properties — all delegate to WizardState ──────────────────────
    // Each setter marks the field as edited and fires change notification.

    // Server management
    public bool ManageServer { get => _state.ManageServer; set => SetState(ref _state.ManageServer, value); }
    public bool UseLocalFile { get => _state.UseLocalFile; set => SetState(ref _state.UseLocalFile, value); }
    public string? LocalModelPath { get => _state.LocalModelPath; set => SetState(ref _state.LocalModelPath, value); }
    public string? HfRepo { get => _state.HfRepo; set => SetState(ref _state.HfRepo, value); }
    public string? HfToken { get => _state.HfToken; set => SetState(ref _state.HfToken, value); }
    public string? ServerUrl { get => _state.ServerUrl; set => SetState(ref _state.ServerUrl, value); }
    public string? LlamaServerExecutablePath { get => _state.LlamaServerExecutablePath; set => SetState(ref _state.LlamaServerExecutablePath, value); }
    public string Host { get => _state.Host; set => SetState(ref _state.Host, value); }
    public int Port { get => _state.Port; set => SetState(ref _state.Port, value); }
    public string? ApiKey { get => _state.ApiKey; set => SetState(ref _state.ApiKey, value); }

    // ─── Step 2: Performance ─────────────────────────────────────────────────

    // Context / batching
    public int? ContextWindowTokens { get => _state.ContextWindowTokens; set => SetState(ref _state.ContextWindowTokens, value); }
    public int? BatchSizeTokens { get => _state.BatchSizeTokens; set => SetState(ref _state.BatchSizeTokens, value); }
    public int? UbatchSizeTokens { get => _state.UbatchSizeTokens; set => SetState(ref _state.UbatchSizeTokens, value); }
    public int? ParallelSlotCount { get => _state.ParallelSlotCount; set => SetState(ref _state.ParallelSlotCount, value); }
    public bool? EnableContinuousBatching { get => _state.EnableContinuousBatching; set => SetState(ref _state.EnableContinuousBatching, value); }
    public bool? EnableCachePrompt { get => _state.EnableCachePrompt; set => SetState(ref _state.EnableCachePrompt, value); }
    public bool? EnableContextShift { get => _state.EnableContextShift; set => SetState(ref _state.EnableContextShift, value); }

    // GPU
    public int? GpuLayerCount { get => _state.GpuLayerCount; set => SetState(ref _state.GpuLayerCount, value); }
    public string? SplitMode { get => _state.SplitMode; set => SetState(ref _state.SplitMode, value); }
    public string? KvCacheTypeK { get => _state.KvCacheTypeK; set => SetState(ref _state.KvCacheTypeK, value); }
    public string? KvCacheTypeV { get => _state.KvCacheTypeV; set => SetState(ref _state.KvCacheTypeV, value); }
    public bool? EnableKvOffload { get => _state.EnableKvOffload; set => SetState(ref _state.EnableKvOffload, value); }
    public bool? EnableFlashAttention { get => _state.EnableFlashAttention; set => SetState(ref _state.EnableFlashAttention, value); }

    // Sampling
    public double? SamplingTemperature { get => _state.SamplingTemperature; set => SetState(ref _state.SamplingTemperature, value); }
    public double? TopP { get => _state.TopP; set => SetState(ref _state.TopP, value); }
    public int? TopK { get => _state.TopK; set => SetState(ref _state.TopK, value); }
    public double? MinP { get => _state.MinP; set => SetState(ref _state.MinP, value); }
    public double? RepeatPenalty { get => _state.RepeatPenalty; set => SetState(ref _state.RepeatPenalty, value); }
    public int? RepeatLastNTokens { get => _state.RepeatLastNTokens; set => SetState(ref _state.RepeatLastNTokens, value); }
    public double? PresencePenalty { get => _state.PresencePenalty; set => SetState(ref _state.PresencePenalty, value); }
    public double? FrequencyPenalty { get => _state.FrequencyPenalty; set => SetState(ref _state.FrequencyPenalty, value); }
    public int? Seed { get => _state.Seed; set => SetState(ref _state.Seed, value); }

    // Threading
    public int? ThreadCount { get => _state.ThreadCount; set => SetState(ref _state.ThreadCount, value); }
    public int? HttpThreadCount { get => _state.HttpThreadCount; set => SetState(ref _state.HttpThreadCount, value); }

    // Model behavior
    public string? ChatTemplate { get => _state.ChatTemplate; set => SetState(ref _state.ChatTemplate, value); }
    public bool? EnableJinja { get => _state.EnableJinja; set => SetState(ref _state.EnableJinja, value); }
    public string? ReasoningFormat { get => _state.ReasoningFormat; set => SetState(ref _state.ReasoningFormat, value); }
    public string? ModelAlias { get => _state.ModelAlias; set => SetState(ref _state.ModelAlias, value); }
    public int? ReasoningBudget { get => _state.ReasoningBudget; set => SetState(ref _state.ReasoningBudget, value); }
    public string? ReasoningBudgetMessage { get => _state.ReasoningBudgetMessage; set => SetState(ref _state.ReasoningBudgetMessage, value); }

    // Logging & Memory
    public int? LogVerbosity { get => _state.LogVerbosity; set => SetState(ref _state.LogVerbosity, value); }
    public bool? EnableMlock { get => _state.EnableMlock; set => SetState(ref _state.EnableMlock, value); }
    public bool? EnableMmap { get => _state.EnableMmap; set => SetState(ref _state.EnableMmap, value); }
    public double? ServerTimeoutSeconds { get => _state.ServerTimeoutSeconds; set => SetState(ref _state.ServerTimeoutSeconds, value); }
    public string? ExtraLlamaArgs { get => _state.ExtraLlamaArgs; set => SetState(ref _state.ExtraLlamaArgs, value); }

    // ─── Step 3: Dataset ──────────────────────────────────────────────────────

    public string PipelineName
    {
        get => _state.PipelineName;
        set => SetState(ref _state.PipelineName, value);
    }

    public int SelectedPipelineIndex
    { //TODO: Use Reflection to get available (hard-coded) pipeline names
        get => PipelineName switch { "Translation" => 1, "CSharpCoding" => 2, _ => 0 };
        set
        {
            var newName = value switch { 1 => "Translation", 2 => "CSharpCoding", _ => "CasualQA" };
            if (_state.PipelineName == newName) return;
            SetState(ref _state.PipelineName, newName, nameof(PipelineName));
            OnPropertyChanged(nameof(SelectedPipelineIndex));

            // Auto-select translation judge template for Translation pipeline
            // Only auto-select if template hasn't been manually changed from default
            if (newName == "Translation" && !_editedFields.Contains(nameof(JudgeTemplate)))
            {
                _state.JudgeTemplate = "translation-judge-template";
                OnPropertyChanged(nameof(JudgeTemplate));
                OnPropertyChanged(nameof(SelectedJudgeTemplateIndex));
            }
        }
    }

    public bool UseSingleFileDataSource
    {
        get => _state.UseSingleFileDataSource;
        set
        {
            if (!SetState(ref _state.UseSingleFileDataSource, value)) return;
            OnPropertyChanged(nameof(UseDirectoryDataSource));
        }
    }

    public bool UseDirectoryDataSource
    {
        get => !_state.UseSingleFileDataSource;
        set
        {
            var newVal = !value;
            if (!SetState(ref _state.UseSingleFileDataSource, newVal, nameof(UseSingleFileDataSource))) return;
            OnPropertyChanged(nameof(UseDirectoryDataSource));
        }
    }

    public string? DataFilePath
    {
        get => _state.DataFilePath;
        set
        {
            if (!SetState(ref _state.DataFilePath, value)) return;
            DetectFieldsFromDataFile(value);
        }
    }

    public string? PromptDir { get => _state.PromptDir; set => SetState(ref _state.PromptDir, value); }
    public string? ExpectedDir { get => _state.ExpectedDir; set => SetState(ref _state.ExpectedDir, value); }

    /// <summary>List of field names detected in the selected data file.</summary>
    public List<string>? DetectedFields
    {
        get => _detectedFields;
        private set => SetField(ref _detectedFields, value);
    }

    /// <summary>True when translation pipeline uses per-item language fields (hides global settings).</summary>
    public bool ShouldHideGlobalTranslationSettings =>
        PipelineName == "Translation" &&
        (!string.IsNullOrEmpty(FieldMappingSourceLanguage) || !string.IsNullOrEmpty(FieldMappingTargetLanguage));

    // Scoring / Judge
    public bool EnableJudge { get => _state.EnableJudge; set => SetState(ref _state.EnableJudge, value); }
    public bool JudgeManageServer { get => _state.JudgeManageServer; set => SetState(ref _state.JudgeManageServer, value); }
    public bool JudgeUseLocalFile { get => _state.JudgeUseLocalFile; set => SetState(ref _state.JudgeUseLocalFile, value); } // TODO: I don't think this is supposed to exist.
    public string? JudgeLocalModelPath { get => _state.JudgeLocalModelPath; set => SetState(ref _state.JudgeLocalModelPath, value); }
    public string? JudgeHfRepo { get => _state.JudgeHfRepo; set => SetState(ref _state.JudgeHfRepo, value); }
    public string? JudgeHfToken { get => _state.JudgeHfToken; set => SetState(ref _state.JudgeHfToken, value); }
    public string? JudgeApiKey { get => _state.JudgeApiKey; set => SetState(ref _state.JudgeApiKey, value); }
    public string? JudgeServerUrl { get => _state.JudgeServerUrl; set => SetState(ref _state.JudgeServerUrl, value); }
    public string? JudgeExecutablePath { get => _state.JudgeExecutablePath; set => SetState(ref _state.JudgeExecutablePath, value); }
    
    // Judge performance — mirrors main server fields
    public int? JudgeContextWindowTokens { get => _state.JudgeContextWindowTokens; set => SetState(ref _state.JudgeContextWindowTokens, value); }
    public int? JudgeBatchSizeTokens { get => _state.JudgeBatchSizeTokens; set => SetState(ref _state.JudgeBatchSizeTokens, value); }
    public int? JudgeUbatchSizeTokens { get => _state.JudgeUbatchSizeTokens; set => SetState(ref _state.JudgeUbatchSizeTokens, value); }
    public bool? JudgeEnableContinuousBatching { get => _state.JudgeEnableContinuousBatching; set => SetState(ref _state.JudgeEnableContinuousBatching, value); }
    public bool? JudgeEnableCachePrompt { get => _state.JudgeEnableCachePrompt; set => SetState(ref _state.JudgeEnableCachePrompt, value); }
    public bool? JudgeEnableContextShift { get => _state.JudgeEnableContextShift; set => SetState(ref _state.JudgeEnableContextShift, value); }
    public bool? JudgeEnableKvOffload { get => _state.JudgeEnableKvOffload; set => SetState(ref _state.JudgeEnableKvOffload, value); }
    public int? JudgeParallelSlotCount { get => _state.JudgeParallelSlotCount; set => SetState(ref _state.JudgeParallelSlotCount, value); }
    public string? JudgeSplitMode { get => _state.JudgeSplitMode; set => SetState(ref _state.JudgeSplitMode, value); }
    public string? JudgeKvCacheTypeK { get => _state.JudgeKvCacheTypeK; set => SetState(ref _state.JudgeKvCacheTypeK, value); }
    public string? JudgeKvCacheTypeV { get => _state.JudgeKvCacheTypeV; set => SetState(ref _state.JudgeKvCacheTypeV, value); }
    public bool? JudgeEnableFlashAttention { get => _state.JudgeEnableFlashAttention; set => SetState(ref _state.JudgeEnableFlashAttention, value); }
    public int? JudgeGpuLayerCount { get => _state.JudgeGpuLayerCount; set => SetState(ref _state.JudgeGpuLayerCount, value); }

    // Judge sampling
    public double? JudgeSamplingTemperature { get => _state.JudgeSamplingTemperature; set => SetState(ref _state.JudgeSamplingTemperature, value); }
    public double? JudgeTopP { get => _state.JudgeTopP; set => SetState(ref _state.JudgeTopP, value); }
    public int? JudgeTopK { get => _state.JudgeTopK; set => SetState(ref _state.JudgeTopK, value); }
    public double? JudgeMinP { get => _state.JudgeMinP; set => SetState(ref _state.JudgeMinP, value); }
    public double? JudgeRepeatPenalty { get => _state.JudgeRepeatPenalty; set => SetState(ref _state.JudgeRepeatPenalty, value); }
    public int? JudgeRepeatLastNTokens { get => _state.JudgeRepeatLastNTokens; set => SetState(ref _state.JudgeRepeatLastNTokens, value); }
    public double? JudgePresencePenalty { get => _state.JudgePresencePenalty; set => SetState(ref _state.JudgePresencePenalty, value); }
    public double? JudgeFrequencyPenalty { get => _state.JudgeFrequencyPenalty; set => SetState(ref _state.JudgeFrequencyPenalty, value); }
    public int? JudgeSeed { get => _state.JudgeSeed; set => SetState(ref _state.JudgeSeed, value); }

    // Judge threading & memory
    public int? JudgeThreadCount { get => _state.JudgeThreadCount; set => SetState(ref _state.JudgeThreadCount, value); }
    public int? JudgeHttpThreadCount { get => _state.JudgeHttpThreadCount; set => SetState(ref _state.JudgeHttpThreadCount, value); }
    public bool? JudgeEnableMlock { get => _state.JudgeEnableMlock; set => SetState(ref _state.JudgeEnableMlock, value); }
    public bool? JudgeEnableMmap { get => _state.JudgeEnableMmap; set => SetState(ref _state.JudgeEnableMmap, value); }

    // Judge model behavior
    public string? JudgeChatTemplate { get => _state.JudgeChatTemplate; set => SetState(ref _state.JudgeChatTemplate, value); }
    public bool? JudgeEnableJinja { get => _state.JudgeEnableJinja; set => SetState(ref _state.JudgeEnableJinja, value); }
    public string? JudgeReasoningFormat { get => _state.JudgeReasoningFormat; set => SetState(ref _state.JudgeReasoningFormat, value); }
    public string? JudgeModelAlias { get => _state.JudgeModelAlias; set => SetState(ref _state.JudgeModelAlias, value); }
    public int? JudgeReasoningBudget { get => _state.JudgeReasoningBudget; set => SetState(ref _state.JudgeReasoningBudget, value); }
    public string? JudgeReasoningBudgetMessage { get => _state.JudgeReasoningBudgetMessage; set => SetState(ref _state.JudgeReasoningBudgetMessage, value); }
    public int? JudgeLogVerbosity { get => _state.JudgeLogVerbosity; set => SetState(ref _state.JudgeLogVerbosity, value); }
    public double? JudgeServerTimeoutSeconds { get => _state.JudgeServerTimeoutSeconds; set => SetState(ref _state.JudgeServerTimeoutSeconds, value); }
    public string? JudgeExtraLlamaArgs { get => _state.JudgeExtraLlamaArgs; set => SetState(ref _state.JudgeExtraLlamaArgs, value); }

    // Judge template
    public string JudgeTemplate
    {
        get => _state.JudgeTemplate;
        set => SetState(ref _state.JudgeTemplate, value);
    }

    public int SelectedJudgeTemplateIndex
    {
        get
        {
            var templates = GetJudgeTemplateNames();
            var index = Array.IndexOf(templates, JudgeTemplate);
            return index >= 0 ? index : 0;
        }
        set
        {
            var templates = GetJudgeTemplateNames();
            var newTemplate = (value >= 0 && value < templates.Length) ? templates[value] : templates[0];
            if (_state.JudgeTemplate == newTemplate) return;
            SetState(ref _state.JudgeTemplate, newTemplate, nameof(JudgeTemplate));
            OnPropertyChanged(nameof(SelectedJudgeTemplateIndex));
        }
    }

    // Output
    public string? CheckpointDatabasePath { get => _state.CheckpointDatabasePath; set => SetState(ref _state.CheckpointDatabasePath, value); }

    public string OutputDir
    {
        get => _state.OutputDir;
        set => SetState(ref _state.OutputDir, value);
    }

    public string? RunName
    {
        get => _state.RunName;
        set
        {
            if (!SetState(ref _state.RunName, value)) return;

            // When continuing from checkpoint and run name changed, clone the database
            if (_state.ContinueFromCheckpoint && !string.IsNullOrEmpty(_state.CheckpointDatabasePath) && !string.IsNullOrEmpty(value))
            {
                try
                {
                    var newDbPath = Path.Combine(
                        Path.GetDirectoryName(_state.CheckpointDatabasePath) ?? ".",
                        $"{value}_checkpoint.db");

                    if (_state.CheckpointDatabasePath != newDbPath && File.Exists(_state.CheckpointDatabasePath))
                    {
                        File.Copy(_state.CheckpointDatabasePath, newDbPath, overwrite: true);
                        _state.CheckpointDatabasePath = newDbPath;
                        OnPropertyChanged(nameof(CheckpointDatabasePath));
                        _editedFields.Add(nameof(CheckpointDatabasePath));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to clone checkpoint database for new run name");
                }
            }
        }
    }

    public ShellTarget? ShellTarget { get => _state.ShellTarget; set => SetState(ref _state.ShellTarget, value); }
    public bool WritePerEvalJson { get => _state.WritePerEvalJson; set => SetState(ref _state.WritePerEvalJson, value); }
    public bool WriteSummaryJson { get => _state.WriteSummaryJson; set => SetState(ref _state.WriteSummaryJson, value); }
    public bool WriteSummaryCsv { get => _state.WriteSummaryCsv; set => SetState(ref _state.WriteSummaryCsv, value); }
    public bool WriteResultsParquet { get => _state.WriteResultsParquet; set => SetState(ref _state.WriteResultsParquet, value); }
    public bool IncludeRawLlmResponse { get => _state.IncludeRawLlmResponse; set => SetState(ref _state.IncludeRawLlmResponse, value); }
    public bool ContinueOnEvalFailure { get => _state.ContinueOnEvalFailure; set => SetState(ref _state.ContinueOnEvalFailure, value); }
    public int? MaxConcurrentEvals { get => _state.MaxConcurrentEvals; set => SetState(ref _state.MaxConcurrentEvals, value); }
    public bool ContinueFromCheckpoint { get => _state.ContinueFromCheckpoint; set => SetState(ref _state.ContinueFromCheckpoint, value); }

    // Field mapping
    public string? FieldMappingId { get => _state.FieldMappingId; set => SetState(ref _state.FieldMappingId, value); }
    public string? FieldMappingUserPrompt { get => _state.FieldMappingUserPrompt; set => SetState(ref _state.FieldMappingUserPrompt, value); }
    public string? FieldMappingExpectedOutput { get => _state.FieldMappingExpectedOutput; set => SetState(ref _state.FieldMappingExpectedOutput, value); }
    public string? FieldMappingSystemPrompt { get => _state.FieldMappingSystemPrompt; set => SetState(ref _state.FieldMappingSystemPrompt, value); }
    public string? FieldMappingSourceLanguage { get => _state.FieldMappingSourceLanguage; set => SetState(ref _state.FieldMappingSourceLanguage, value); }
    public string? FieldMappingTargetLanguage { get => _state.FieldMappingTargetLanguage; set => SetState(ref _state.FieldMappingTargetLanguage, value); }
    public string? FieldMappingTestFile { get => _state.FieldMappingTestFile; set => SetState(ref _state.FieldMappingTestFile, value); }
    public string? FieldMappingBuildScript { get => _state.FieldMappingBuildScript; set => SetState(ref _state.FieldMappingBuildScript, value); }

    // Pipeline-specific configuration
    public string? TranslationSourceLanguage { get => _state.TranslationSourceLanguage; set => SetState(ref _state.TranslationSourceLanguage, value); }
    public string? TranslationTargetLanguage { get => _state.TranslationTargetLanguage; set => SetState(ref _state.TranslationTargetLanguage, value); }
    public string? TranslationSystemPrompt { get => _state.TranslationSystemPrompt; set => SetState(ref _state.TranslationSystemPrompt, value); }
    public string? CodeBuildScriptPath { get => _state.CodeBuildScriptPath; set => SetState(ref _state.CodeBuildScriptPath, value); }

    // ─── Build PartialConfig from wizard state ────────────────────────────────

    public PartialConfig BuildPartialConfig()
    {
        var s = _state;
        var ef = _editedFields;

        bool Edited(string name) => ef.Contains(name);

        // Server model source
        var hasModelField = !string.IsNullOrEmpty(s.LocalModelPath) || !string.IsNullOrEmpty(s.HfRepo)
            || Edited(nameof(ManageServer)) || Edited(nameof(UseLocalFile))
            || Edited(nameof(LocalModelPath)) || Edited(nameof(HfRepo)) || Edited(nameof(HfToken));

        ModelSource? model = null;
        if (hasModelField && s.ManageServer)
        {
            if (s.UseLocalFile && s.LocalModelPath != null)
                model = new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = s.LocalModelPath };
            else if (s.HfRepo != null)
                model = new ModelSource { Kind = ModelSourceKind.HuggingFace, HfRepo = s.HfRepo, HfToken = s.HfToken };
        }

        var hasServerField = !string.IsNullOrEmpty(s.Host) || s.Port != 8080 || !string.IsNullOrEmpty(s.ApiKey)
            || !string.IsNullOrEmpty(s.ServerUrl) || !string.IsNullOrEmpty(s.LlamaServerExecutablePath)
            || Edited(nameof(ManageServer)) || Edited(nameof(Host)) || Edited(nameof(Port))
            || Edited(nameof(ApiKey)) || Edited(nameof(ServerUrl)) || Edited(nameof(LlamaServerExecutablePath));

        var server = hasServerField || model != null
            ? new PartialServerConfig
            {
                Manage = s.ManageServer,
                Model = model,
                Host = Edited(nameof(Host)) && s.ManageServer ? s.Host : null,
                Port = Edited(nameof(Port)) && s.ManageServer ? s.Port : null,
                ApiKey = Edited(nameof(ApiKey)) ? s.ApiKey : null,
                ExecutablePath = Edited(nameof(LlamaServerExecutablePath)) ? s.LlamaServerExecutablePath : null,
                BaseUrl = Edited(nameof(ServerUrl)) && !s.ManageServer ? s.ServerUrl : null,
            }
            : null;

        var llamaSettings = BuildLlamaServerSettings();

        // Judge
        var hasJudgeField = s.EnableJudge
            || !string.IsNullOrEmpty(s.JudgeLocalModelPath) || !string.IsNullOrEmpty(s.JudgeHfRepo)
            || !string.IsNullOrEmpty(s.JudgeServerUrl) || !string.IsNullOrEmpty(s.JudgeApiKey)
            || Edited(nameof(EnableJudge)) || Edited(nameof(JudgeManageServer))
            || Edited(nameof(JudgeLocalModelPath)) || Edited(nameof(JudgeHfRepo))
            || Edited(nameof(JudgeApiKey)) || Edited(nameof(JudgeServerUrl))
            || Edited(nameof(SelectedJudgeTemplateIndex));

        var judge = hasJudgeField
            ? (s.EnableJudge ? BuildJudgeConfig() : new PartialJudgeConfig { Enable = false })
            : null;

        // Data source
        var dataSourceEdited = Edited(nameof(UseSingleFileDataSource)) || Edited(nameof(DataFilePath))
            || Edited(nameof(PromptDir)) || Edited(nameof(ExpectedDir));
        var hasValidDataSource = s.UseSingleFileDataSource
            ? !string.IsNullOrEmpty(s.DataFilePath)
            : !string.IsNullOrEmpty(s.PromptDir);

        DataSourceConfig? dataSource = null;
        if (dataSourceEdited || hasValidDataSource)
        {
            var fieldMapping = new FieldMapping
            {
                IdField = s.FieldMappingId,
                UserPromptField = s.FieldMappingUserPrompt,
                ExpectedOutputField = s.FieldMappingExpectedOutput,
                SystemPromptField = s.FieldMappingSystemPrompt,
                SourceLanguageField = string.IsNullOrEmpty(s.FieldMappingSourceLanguage) ? null : s.FieldMappingSourceLanguage,
                TargetLanguageField = string.IsNullOrEmpty(s.FieldMappingTargetLanguage) ? null : s.FieldMappingTargetLanguage,
            };

            string? defaultSystemPrompt = s.PipelineName == "Translation" && string.IsNullOrEmpty(s.FieldMappingSystemPrompt)
                ? $"You are a professional translator. Translate the following text from {s.TranslationSourceLanguage ?? "English"} to {s.TranslationTargetLanguage ?? "French"} accurately and naturally. Output only the translation, with no explanation or preamble."
                : null;

            dataSource = s.UseSingleFileDataSource
                ? new DataSourceConfig { Kind = DataSourceKind.SingleFile, FilePath = s.DataFilePath, FieldMapping = fieldMapping, DefaultSystemPrompt = defaultSystemPrompt }
                : new DataSourceConfig { Kind = DataSourceKind.SplitDirectories, PromptDirectory = s.PromptDir, ExpectedDirectory = s.ExpectedDir, FieldMapping = fieldMapping, DefaultSystemPrompt = defaultSystemPrompt };
        }

        var evalSet = dataSource != null || Edited(nameof(PipelineName))
            ? new EvalSetConfig
            {
                Id = _checkpointEvalSetId ?? Guid.NewGuid().ToString(),
                PipelineName = Edited(nameof(PipelineName)) ? s.PipelineName : "CasualQA",
                DataSource = dataSource ?? new DataSourceConfig { Kind = DataSourceKind.SingleFile },
                PipelineOptions = BuildPipelineOptions()
            }
            : null;

        // Run meta
        var runEdited = Edited(nameof(RunName)) || Edited(nameof(OutputDir)) || Edited(nameof(ShellTarget))
            || Edited(nameof(ContinueFromCheckpoint)) || Edited(nameof(ContinueOnEvalFailure))
            || Edited(nameof(MaxConcurrentEvals)) || Edited(nameof(CheckpointDatabasePath));

        var run = runEdited ? new PartialRunMeta
        {
            RunName = Edited(nameof(RunName)) ? s.RunName : null,
            OutputDirectoryPath = Edited(nameof(OutputDir)) ? s.OutputDir : null,
            ExportShellTarget = Edited(nameof(ShellTarget)) ? s.ShellTarget : null,
            ContinueFromCheckpoint = Edited(nameof(ContinueFromCheckpoint)) ? s.ContinueFromCheckpoint : null,
            CheckpointDatabasePath = Edited(nameof(CheckpointDatabasePath)) ? s.CheckpointDatabasePath : null,
            ContinueOnEvalFailure = Edited(nameof(ContinueOnEvalFailure)) ? (s.ContinueOnEvalFailure ? true : null) : null,
            MaxConcurrentEvals = Edited(nameof(MaxConcurrentEvals)) ? s.MaxConcurrentEvals : null,
        } : null;

        // Output config
        var outputEdited = Edited(nameof(WritePerEvalJson)) || Edited(nameof(WriteSummaryJson))
            || Edited(nameof(WriteSummaryCsv)) || Edited(nameof(WriteResultsParquet))
            || Edited(nameof(IncludeRawLlmResponse));

        OutputConfig? output = outputEdited ? new OutputConfig
        {
            WritePerEvalJson = !Edited(nameof(WritePerEvalJson)) || s.WritePerEvalJson,
            WriteSummaryJson = !Edited(nameof(WriteSummaryJson)) || s.WriteSummaryJson,
            WriteSummaryCsv = Edited(nameof(WriteSummaryCsv)) && s.WriteSummaryCsv,
            WriteResultsParquet = Edited(nameof(WriteResultsParquet)) && s.WriteResultsParquet,
            IncludeRawLlmResponse = !Edited(nameof(IncludeRawLlmResponse)) || s.IncludeRawLlmResponse,
            ShellTarget = Edited(nameof(ShellTarget)) ? s.ShellTarget : null,
            OutputDir = Edited(nameof(OutputDir)) ? s.OutputDir : null,
        } : null;

        return new PartialConfig
        {
            Server = server,
            LlamaServer = llamaSettings,
            EvalSets = evalSet != null ? [evalSet] : [],
            Judge = judge,
            Run = run,
            Output = output,
        };
    }

    private PartialLlamaServerSettings? BuildLlamaServerSettings()
    {
        // Uses reflection over WizardState to find which llama-server fields were edited,
        // avoiding the need to manually list every field twice.
        return BuildServerSettingsFromState(
            fieldPrefix: "",
            stateGetter: name => WizardState.GetLlamaField(_state, name),
            editedFields: _editedFields,
            extraArgs: _state.ExtraLlamaArgs);
    }

    private PartialLlamaServerSettings? BuildJudgeLlamaServerSettings()
    {
        return BuildServerSettingsFromState(
            fieldPrefix: "Judge",
            stateGetter: name => WizardState.GetJudgeLlamaField(_state, name),
            editedFields: _editedFields,
            extraArgs: _state.JudgeExtraLlamaArgs);
    }

    /// <summary>
    /// Builds a <see cref="PartialLlamaServerSettings"/> from state fields whose
    /// property names share a common prefix in <see cref="WizardState"/>.
    /// </summary>
    private static PartialLlamaServerSettings? BuildServerSettingsFromState(
        string fieldPrefix,
        Func<string, object?> stateGetter,
        HashSet<string> editedFields,
        string? extraArgs)
    {
        // These are the canonical llama-server setting names (without judge prefix).
        // We check editedFields using the actual VM property name (with prefix where needed).
        var fields = WizardState.LlamaSettingNames;
        bool hasAny = fields.Any(f => editedFields.Contains(fieldPrefix + f))
            || (!string.IsNullOrEmpty(extraArgs) && fieldPrefix == "");

        if (!hasAny) return null;

        T? Get<T>(string name) where T : struct
        {
            if (!editedFields.Contains(fieldPrefix + name)) return null;
            return stateGetter(name) is T v ? v : null;
        }

        string? GetStr(string name)
        {
            if (!editedFields.Contains(fieldPrefix + name)) return null;
            return stateGetter(name) as string;
        }

        return new PartialLlamaServerSettings
        {
            ContextWindowTokens = Get<int>(nameof(ContextWindowTokens)),
            BatchSizeTokens = Get<int>(nameof(BatchSizeTokens)),
            UbatchSizeTokens = Get<int>(nameof(UbatchSizeTokens)),
            ParallelSlotCount = Get<int>(nameof(ParallelSlotCount)),
            EnableContinuousBatching = Get<bool>(nameof(EnableContinuousBatching)),
            EnableCachePrompt = Get<bool>(nameof(EnableCachePrompt)),
            EnableContextShift = Get<bool>(nameof(EnableContextShift)),
            GpuLayerCount = Get<int>(nameof(GpuLayerCount)),
            SplitMode = GetStr(nameof(SplitMode)),
            KvCacheTypeK = GetStr(nameof(KvCacheTypeK)),
            KvCacheTypeV = GetStr(nameof(KvCacheTypeV)),
            EnableKvOffload = Get<bool>(nameof(EnableKvOffload)),
            EnableFlashAttention = Get<bool>(nameof(EnableFlashAttention)),
            SamplingTemperature = Get<double>(nameof(SamplingTemperature)),
            TopP = Get<double>(nameof(TopP)),
            TopK = Get<int>(nameof(TopK)),
            MinP = Get<double>(nameof(MinP)),
            RepeatPenalty = Get<double>(nameof(RepeatPenalty)),
            RepeatLastNTokens = Get<int>(nameof(RepeatLastNTokens)),
            PresencePenalty = Get<double>(nameof(PresencePenalty)),
            FrequencyPenalty = Get<double>(nameof(FrequencyPenalty)),
            Seed = Get<int>(nameof(Seed)),
            ThreadCount = Get<int>(nameof(ThreadCount)),
            HttpThreadCount = Get<int>(nameof(HttpThreadCount)),
            ChatTemplate = GetStr(nameof(ChatTemplate)),
            EnableJinja = Get<bool>(nameof(EnableJinja)),
            ReasoningFormat = GetStr(nameof(ReasoningFormat)),
            ModelAlias = GetStr(nameof(ModelAlias)),
            ReasoningBudget = Get<int>(nameof(ReasoningBudget)),
            ReasoningBudgetMessage = GetStr(nameof(ReasoningBudgetMessage)),
            LogVerbosity = Get<int>(nameof(LogVerbosity)),
            EnableMlock = Get<bool>(nameof(EnableMlock)),
            EnableMmap = Get<bool>(nameof(EnableMmap)),
            ServerTimeoutSeconds = Get<double>(nameof(ServerTimeoutSeconds)),
            ExtraArgs = !string.IsNullOrEmpty(extraArgs)
                ? extraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
                : null
        };
    }

    private Dictionary<string, object?>? BuildPipelineOptions()
    {
        var options = new Dictionary<string, object?>();
        var s = _state;

        switch (s.PipelineName)
        {
            case "Translation":
                // Per-item language fields are mutually exclusive with pipeline-level settings
                if (!string.IsNullOrEmpty(s.FieldMappingSourceLanguage) || !string.IsNullOrEmpty(s.FieldMappingTargetLanguage))
                {
                    if (!string.IsNullOrEmpty(s.FieldMappingSourceLanguage)) options["sourceLanguageField"] = s.FieldMappingSourceLanguage;
                    if (!string.IsNullOrEmpty(s.FieldMappingTargetLanguage)) options["targetLanguageField"] = s.FieldMappingTargetLanguage;
                }
                else
                {
                    if (!string.IsNullOrEmpty(s.TranslationSourceLanguage)) options["sourceLanguage"] = s.TranslationSourceLanguage;
                    if (!string.IsNullOrEmpty(s.TranslationTargetLanguage)) options["targetLanguage"] = s.TranslationTargetLanguage;
                }
                if (!string.IsNullOrEmpty(s.TranslationSystemPrompt)) options["systemPrompt"] = s.TranslationSystemPrompt;
                break;

            case "CSharpCoding":
                if (!string.IsNullOrEmpty(s.CodeBuildScriptPath)) options["buildScriptPath"] = s.CodeBuildScriptPath;
                if (!string.IsNullOrEmpty(s.FieldMappingTestFile)) options["testFilePath"] = s.FieldMappingTestFile;
                break;
        }

        return options.Count > 0 ? options : null;
    }

    private PartialJudgeConfig? BuildJudgeConfig()
    {
        var s = _state;
        var ef = _editedFields;
        bool Edited(string name) => ef.Contains(name);

        var hasJudgeModelField = !string.IsNullOrEmpty(s.JudgeLocalModelPath) || !string.IsNullOrEmpty(s.JudgeHfRepo)
            || Edited(nameof(JudgeManageServer)) || Edited(nameof(JudgeUseLocalFile))
            || Edited(nameof(JudgeLocalModelPath)) || Edited(nameof(JudgeHfRepo));

        ModelSource? judgeModel = null;
        if (hasJudgeModelField && s.JudgeManageServer)
        {
            if (s.JudgeUseLocalFile && !string.IsNullOrEmpty(s.JudgeLocalModelPath))
                judgeModel = new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = s.JudgeLocalModelPath };
            else if (!string.IsNullOrEmpty(s.JudgeHfRepo))
                judgeModel = new ModelSource { Kind = ModelSourceKind.HuggingFace, HfRepo = s.JudgeHfRepo, HfToken = s.JudgeHfToken };
        }

        var judgeExecutablePath = Edited(nameof(JudgeExecutablePath)) ? s.JudgeExecutablePath : s.LlamaServerExecutablePath;

        return new PartialJudgeConfig
        {
            Enable = EnableJudge,
            ServerConfig = new PartialServerConfig
            {
                Manage = s.JudgeManageServer,
                Model = judgeModel,
                Host = Edited(nameof(JudgeManageServer)) && s.JudgeManageServer ? "127.0.0.1" : null,
                Port = Edited(nameof(JudgeManageServer)) && s.JudgeManageServer ? 8081 : null,
                ApiKey = Edited(nameof(JudgeApiKey)) ? s.JudgeApiKey : null,
                ExecutablePath = judgeExecutablePath,
                BaseUrl = Edited(nameof(JudgeManageServer)) && !s.JudgeManageServer ? s.JudgeServerUrl : null
            },
            ServerSettings = BuildJudgeLlamaServerSettings(),
            BaseUrl = Edited(nameof(JudgeServerUrl)) && !s.JudgeManageServer ? s.JudgeServerUrl : null,
            JudgePromptTemplate = Edited(nameof(JudgeTemplate)) ? s.JudgeTemplate : null
        };
    }

    // ─── Browse actions ───────────────────────────────────────────────────────

    private async Task BrowseLocalModelAsync(string? parameter)
    {
        if (_filePicker == null) return;
        if (parameter == "executable")
        {
            var path = await _filePicker.ShowOpenFileDialogAsync("Select llama-server Executable", "Executable Files|llama-server;llama-server.exe|All Files|*.*");
            if (path != null) LlamaServerExecutablePath = path;
        }
        else
        {
            var path = await _filePicker.ShowOpenFileDialogAsync("Select Model File", "Model Files|*.gguf|All Files|*.*");
            if (path != null) LocalModelPath = path;
        }
    }

    private async Task BrowseDataFileAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFileDialogAsync("Select Data File", "Data Files|*.json;*.yaml;*.yml;*.csv;*.parquet;*.jsonl|All Files|*.*");
        DataFilePath = path;
    }

    private async Task BrowsePromptDirAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFolderDialogAsync("Select Prompt Directory");
        PromptDir = path;
    }

    private async Task BrowseExpectedDirAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFolderDialogAsync("Select Expected Output Directory");
        ExpectedDir = path;
    }

    private async Task BrowseOutputDirAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFolderDialogAsync("Select Output Directory");
        if (path != null) OutputDir = path;
    }

    private async Task BrowseJudgeModelAsync(string? parameter)
    {
        if (_filePicker == null) return;
        if (parameter == "executable")
        {
            var path = await _filePicker.ShowOpenFileDialogAsync("Select Judge llama-server Executable", "Executable Files|llama-server;llama-server.exe|All Files|*.*");
            if (path != null) JudgeExecutablePath = path;
        }
        else
        {
            var path = await _filePicker.ShowOpenFileDialogAsync("Select Judge Model File", "Model Files|*.gguf|All Files|*.*");
            if (path != null) JudgeLocalModelPath = path;
        }
    }

    private async Task BrowseBuildScriptAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFileDialogAsync("Select Build Script", "Script Files|*.sh;*.bat;*.ps1;*.cmd|All Files|*.*");
        if (path != null) CodeBuildScriptPath = path;
    }

    private async Task BrowseCheckpointDbAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFileDialogAsync("Select Checkpoint Database", "SQLite Database|*.db|All Files|*.*");
        if (path != null)
        {
            CheckpointDatabasePath = path;
            ContinueFromCheckpoint = true;
            await LoadCheckpointConfigAsync(path);
        }
    }

    // ─── Field detection ──────────────────────────────────────────────────────

    private void DetectFieldsFromDataFile(string? filePath)
    {
        DetectedFields = null;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var fields = new HashSet<string>();

            if (ext is ".json" or ".jsonl")
            {
                var content = ext == ".jsonl"
                    ? File.ReadLines(filePath).FirstOrDefault() ?? ""
                    : File.ReadAllText(filePath);

                using var doc = System.Text.Json.JsonDocument.Parse(content);
                var root = doc.RootElement;
                var obj = root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0
                    ? root[0] : root;
                if (obj.ValueKind == System.Text.Json.JsonValueKind.Object)
                    foreach (var prop in obj.EnumerateObject()) fields.Add(prop.Name);
            }
            else if (ext == ".csv")
            {
                var headerLine = File.ReadLines(filePath).FirstOrDefault();
                if (!string.IsNullOrEmpty(headerLine))
                    foreach (var field in headerLine.Split(',')) fields.Add(field.Trim());
            }
            else if (ext is ".yaml" or ".yml")
            {
                foreach (var line in File.ReadLines(filePath).Take(250))
                {
                    if (line.Trim().Length > 0 && !line.StartsWith(' ') && !line.StartsWith('-') && line.Contains(':'))
                    {
                        var fieldName = line.Split(':')[0].Trim();
                        if (!string.IsNullOrEmpty(fieldName)) fields.Add(fieldName);
                    }
                }
            }

            if (fields.Count > 0) DetectedFields = [.. fields.OrderBy(f => f)];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to detect fields from {FilePath}", filePath);
        }
    }

    // ─── Checkpoint loading ───────────────────────────────────────────────────

    private async Task LoadCheckpointConfigAsync(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath)) return;

            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Value FROM StartupParameters WHERE Key = @key";
            cmd.Parameters.AddWithValue("@key", "startup_config");

            var configJson = await cmd.ExecuteScalarAsync() as string;

            if (!string.IsNullOrEmpty(configJson))
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<ResolvedConfig>(configJson);
                if (config != null)
                {
                    PopulateFromCheckpointConfig(config);
                    return;
                }
            }

            // Fallback: EvalGen-style checkpoint
            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT Key, Value FROM StartupParameters";
            using var reader = await cmd.ExecuteReaderAsync();
            var values = new Dictionary<string, string>();
            while (await reader.ReadAsync())
                values[reader.GetString(0)] = reader.GetString(1);

            if (values.Count > 0) PopulateFromEvalGenCheckpoint(values);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load checkpoint configuration from {DbPath}", dbPath);
            OnShowNotification?.Invoke($"Warning: Could not load checkpoint configuration: {ex.Message}");
        }
    }

    private void PopulateFromEvalGenCheckpoint(Dictionary<string, string> values)
    {
        if (values.TryGetValue("RunName", out var runName) && !string.IsNullOrEmpty(runName))
        {
            _state.RunName = runName;
            _editedFields.Add(nameof(RunName));
        }
        if (values.TryGetValue("OutputDirectoryPath", out var outputDir) && !string.IsNullOrEmpty(outputDir))
        {
            _state.OutputDir = outputDir;
            _editedFields.Add(nameof(OutputDir));
        }

        if (values.TryGetValue("JudgeConfigJson", out var judgeConfigJson) && !string.IsNullOrEmpty(judgeConfigJson))
        {
            try
            {
                var judgeConfig = System.Text.Json.JsonSerializer.Deserialize<JudgeConfig>(judgeConfigJson);
                if (judgeConfig != null)
                {
                    _state.JudgeManageServer = judgeConfig.Manage;
                    _editedFields.Add(nameof(JudgeManageServer));

                    if (!string.IsNullOrEmpty(judgeConfig.ServerConfig?.ExecutablePath))
                    {
                        _state.JudgeExecutablePath = judgeConfig.ServerConfig.ExecutablePath;
                        _editedFields.Add(nameof(JudgeExecutablePath));
                    }
                    if (judgeConfig.ServerConfig?.Model?.Kind == ModelSourceKind.LocalFile)
                    {
                        _state.JudgeUseLocalFile = true;
                        _state.JudgeLocalModelPath = judgeConfig.ServerConfig.Model.FilePath;
                        _editedFields.Add(nameof(JudgeUseLocalFile));
                        _editedFields.Add(nameof(JudgeLocalModelPath));
                    }
                    if (judgeConfig.ServerSettings?.ContextWindowTokens.HasValue == true)
                    {
                        _state.JudgeContextWindowTokens = judgeConfig.ServerSettings.ContextWindowTokens;
                        _editedFields.Add(nameof(JudgeContextWindowTokens));
                    }
                }
            }
            catch { /* Ignore JSON errors */ }
        }
    }

    /// <summary>
    /// Populates wizard state from a loaded checkpoint <see cref="ResolvedConfig"/>.
    /// Uses reflection over the model types to avoid enumerating every field manually.
    /// </summary>
    private void PopulateFromCheckpointConfig(ResolvedConfig config)
    {
        WizardState.ApplyResolvedConfig(_state, config, _editedFields);
        _checkpointEvalSetId = config.EvalSets.Count > 0 ? config.EvalSets[0].Id : null;

        // Notify all properties first
        NotifyAllProperties();

        var missingFields = new List<string>();
        if (_state.ManageServer && string.IsNullOrEmpty(_state.LocalModelPath) && string.IsNullOrEmpty(_state.HfRepo))
            missingFields.Add("model file");
        if (_state.EnableJudge && _state.JudgeManageServer && string.IsNullOrEmpty(_state.JudgeLocalModelPath) && string.IsNullOrEmpty(_state.JudgeHfRepo))
            missingFields.Add("judge model file");

        var notification = "Checkpoint configuration loaded successfully.";
        if (missingFields.Count > 0) notification += $" Please re-select: {string.Join(", ", missingFields)}.";
        OnShowNotification?.Invoke(notification);
    }

    // ─── Test connections ─────────────────────────────────────────────────────

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_state.ServerUrl))
        {
            OnShowNotification?.Invoke("Please enter a server URL first.");
            return;
        }
        await TestEndpointAsync(_state.ServerUrl, "Connection");
    }

    private async Task TestJudgeConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_state.JudgeServerUrl))
        {
            OnShowNotification?.Invoke("Please enter a judge server URL first.");
            return;
        }
        await TestEndpointAsync(_state.JudgeServerUrl, "Judge connection");
    }

    private async Task TestEndpointAsync(string url, string label)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"{url.TrimEnd('/')}/health");
            OnShowNotification?.Invoke(response.IsSuccessStatusCode
                ? $"{label} successful! Server is healthy."
                : $"{label}: server responded with status {response.StatusCode}");
        }
        catch (Exception ex)
        {
            OnShowNotification?.Invoke($"{label} failed: {ex.Message}");
        }
    }

    // ─── Reset to defaults ────────────────────────────────────────────────────

    public void ResetToDefaults()
    {
        _state = WizardState.CreateDefaults();
        _currentStep = WizardStepKind.ContinueRun;
        _checkpointEvalSetId = null;
        _editedFields.Clear();

        ResetToDefaultsCompleted?.Invoke(this, EventArgs.Empty);
        NotifyAllProperties();
    }

    // ─── Sync defaults from settings ─────────────────────────────────────────

    /// <summary>
    /// Syncs default values from a resolved config into unedited wizard fields.
    /// </summary>
    public void SyncDefaultsFromSettings(ResolvedConfig config, SettingsViewModel? settingsVM = null)
    {
        WizardState.ApplyResolvedConfig(_state, config, _editedFields, onlyUnedited: true);

        NotifyAllProperties();

        _logger?.LogDebug("Synced defaults from settings (edited fields: {Count})", _editedFields.Count);
    }

    /// <summary>
    /// YAML preview of the final configuration that would be used if the user proceeds.
    /// This is shown on the Review step of the wizard.
    /// </summary>
    public string ConfigYamlPreview
    {
        get
        {
            var partialConfig = BuildPartialConfig();
            return SerializeToYaml(partialConfig);
        }
    }

    private static string SerializeToYaml(PartialConfig config)
    {
        var serializer = new YamlDotNet.Serialization.SerializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(YamlDotNet.Serialization.DefaultValuesHandling.OmitDefaults)
            .Build();

        var yaml = serializer.Serialize(config);
        return $"# Seevalocal settings\n# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{yaml}";
    }

    // ─── Judge template names ─────────────────────────────────────────────────

    private static string[] GetJudgeTemplateNames()
    {
        var regex = CapitalLetterSplitRegex();
        return typeof(Core.DefaultTemplates)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .Order()
            .Select(name => regex.Replace(name, "-$1").ToLowerInvariant())
            .ToArray();
    }

    [System.Text.RegularExpressions.GeneratedRegex("(?<!^)([A-Z])")]
    private static partial System.Text.RegularExpressions.Regex CapitalLetterSplitRegex();

    // ─── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    /// <summary>
    /// Standard field setter for non-state backing fields (e.g. _currentStep, _detectedFields).
    /// </summary>
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        if (GoBackCommand is RelayCommand b) b.NotifyCanExecuteChanged();
        if (GoForwardCommand is RelayCommand f) f.NotifyCanExecuteChanged();
        return true;
    }

    /// <summary>
    /// Setter for <see cref="WizardState"/> fields. Marks the property as edited and
    /// fires change notification using the caller's property name.
    /// </summary>
    private bool SetState<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        if (name != null) _editedFields.Add(name);
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        if (GoBackCommand is RelayCommand b) b.NotifyCanExecuteChanged();
        if (GoForwardCommand is RelayCommand f) f.NotifyCanExecuteChanged();
        return true;
    }

    private void NotifyAllProperties()
    {
        foreach (var prop in GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            OnPropertyChanged(prop.Name);
        ((RelayCommand)GoBackCommand).NotifyCanExecuteChanged();
        ((RelayCommand)GoForwardCommand).NotifyCanExecuteChanged();
    }
}