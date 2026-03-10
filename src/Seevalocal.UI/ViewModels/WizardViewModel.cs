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
    ModelAndServer,
    PerformanceSettings,
    EvaluationDataset,
    Scoring,
    Output,
    ReviewAndRun
}

/// <summary>
/// View model for the guided setup wizard.
/// </summary>
public sealed partial class WizardViewModel : IWizardViewModel
{
    private readonly IFilePickerService? _filePicker;
    private readonly IToastService? _toastService;
    private WizardStepKind _currentStep = WizardStepKind.ContinueRun;
    private string? _checkpointDatabasePath;

    // Track which fields have been edited by the user in the wizard
    private readonly HashSet<string> _editedFields = [];

    // Server management
    private bool _manageServer = true;
    private bool _useLocalFile = true;
    private string? _localModelPath;
    private string? _hfRepo;
    private string? _hfToken;
    private string? _serverUrl;
    private string? _llamaServerExecutablePath;  // Local path to llama-server binary

    // Server connection
    private string _host = "127.0.0.1";
    private int _port = 8080;
    private string? _apiKey;

    // Context / batching
    private int? _contextWindowTokens;
    private int? _batchSizeTokens;
    private int? _ubatchSizeTokens;
    private int? _parallelSlotCount;
    private bool? _enableContinuousBatching;
    private bool? _enableCachePrompt;
    private bool? _enableContextShift;

    // GPU
    private int? _gpuLayerCount;
    private string? _splitMode;
    private string? _kvCacheTypeK;
    private string? _kvCacheTypeV;
    private bool? _enableKvOffload;
    private bool? _enableFlashAttention;

    // Sampling
    private double? _samplingTemperature;
    private double? _topP;
    private int? _topK;
    private double? _minP;
    private double? _repeatPenalty;
    private int? _repeatLastNTokens;
    private double? _presencePenalty;
    private double? _frequencyPenalty;
    private int? _seed;

    // Threading
    private int? _threadCount;
    private int? _httpThreadCount;

    // Model behavior
    private string? _chatTemplate;
    private bool? _enableJinja;
    private string? _reasoningFormat;
    private string? _modelAlias;

    // Logging & Memory
    private int? _logVerbosity;
    private bool? _enableMlock;
    private bool? _enableMmap;
    private double? _serverTimeoutSeconds;

    // Dataset
    private string _pipelineName = "CasualQA";
    private string? _dataFilePath;
    private string? _promptDir;
    private string? _expectedDir;
    private bool _useSingleFileDataSource = true;

    // Judge
    private bool _enableJudge;
    private bool _judgeManageServer = true;
    private bool _judgeUseLocalFile = true;
    private string? _judgeLocalModelPath;
    private string? _judgeHfRepo;
    private string? _judgeHfToken;
    private string? _judgeApiKey;
    private string? _judgeServerUrl;
    private string? _judgeExecutablePath;  // Local path to judge llama-server binary
    private int? _judgeContextWindowTokens;
    private int? _judgeParallelSlotCount;
    private int? _judgeGpuLayerCount;
    private int? _judgeBatchSizeTokens;
    private string? _judgeSplitMode;
    private string? _judgeKvCacheTypeK;
    private string? _judgeKvCacheTypeV;
    private bool? _judgeEnableFlashAttention;
    private double? _judgeSamplingTemperature;
    private double? _judgeTopP;
    private int? _judgeTopK;
    private double? _judgeMinP;
    private double? _judgeRepeatPenalty;
    private int? _judgeSeed;
    private int? _judgeThreadCount;
    private int? _judgeHttpThreadCount;
    private bool? _judgeEnableMlock;
    private bool? _judgeEnableMmap;
    private string? _judgeChatTemplate;
    private bool? _judgeEnableJinja;
    private int? _judgeLogVerbosity;
    private double? _judgeServerTimeoutSeconds;
    private string _judgeTemplate = "standard";
    private double _judgeScoreMin = 0;
    private double _judgeScoreMax = 10;

    // Output
    private string _outputDir = "./results";
    private string? _runName;
    private ShellTarget? _shellTarget;
    private bool _continueFromCheckpoint;

    // Output settings (from Settings screen)
    private bool _writePerEvalJson;
    private bool _writeSummaryJson = true;
    private bool _writeSummaryCsv;
    private bool _writeResultsParquet;
    private bool _includeRawLlmResponse;
    private bool _continueOnEvalFailure = true;
    private int? _maxConcurrentEvals;

    // ─── Step navigation ──────────────────────────────────────────────────────

    public WizardStepKind CurrentStep
    {
        get => _currentStep;
        set => SetField(ref _currentStep, value);
    }

    public bool CanGoBack => CurrentStep != WizardStepKind.ContinueRun;

    public bool CanGoForward => ValidateCurrentStep().Count == 0; //TODO: block if an eval is already running

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

    // Test connection commands
    public ICommand TestConnectionCommand { get; }
    public ICommand TestJudgeConnectionCommand { get; }

    // These are set by MainWindow after construction
    public Action? OnExportScript { get; set; }
    public Func<Task>? OnStartRun { get; set; }
    public Action<string>? OnShowNotification { get; set; }

    // IWizardViewModel interface properties
    string? IWizardViewModel.OutputDirectoryPath => OutputDir;
    string? IWizardViewModel.RunName => _runName;
    ShellTarget? IWizardViewModel.ShellTarget => _shellTarget;

    public WizardViewModel(IFilePickerService? filePicker = null, IToastService? toastService = null)
    {
        _filePicker = filePicker;
        _toastService = toastService;
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

        // Auto-select shell dialect based on OS (default for when user reaches Output step)
        _shellTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Core.Models.ShellTarget.PowerShell
            : Core.Models.ShellTarget.Bash;

        // Notify UI of initial index-based property values
        OnPropertyChanged(nameof(SelectedJudgeTemplateIndex));
    }

    private async Task OnStartRunAsync()
    {
        if (OnStartRun != null)
        {
            await OnStartRun();
        }
    }

    public void GoBack()
    {
        if (!CanGoBack) return;
        CurrentStep = (WizardStepKind)((int)CurrentStep - 1);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        ((RelayCommand)GoBackCommand).NotifyCanExecuteChanged();
        ((RelayCommand)GoForwardCommand).NotifyCanExecuteChanged();
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
            await OnStartRunAsync();
            return;
        }

        var nextStep = (WizardStepKind)((int)CurrentStep + 1);

        // Auto-select shell dialect when navigating to Output step if not already set
        if (nextStep == WizardStepKind.Output && ShellTarget == null)
        {
            ShellTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Core.Models.ShellTarget.PowerShell
                : Core.Models.ShellTarget.Bash;
        }

        CurrentStep = nextStep;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        ((RelayCommand)GoBackCommand).NotifyCanExecuteChanged();
        ((RelayCommand)GoForwardCommand).NotifyCanExecuteChanged();

        // Raise event for step change - MainWindow can sync settings on this
        StepChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Event raised when the wizard step changes.
    /// MainWindow listens to this to sync settings to unedited wizard fields.
    /// </summary>
    public event EventHandler? StepChanged;

    public List<string> ValidateCurrentStep() => CurrentStep switch
    {
        WizardStepKind.ContinueRun => ValidateContinueRunStep(),
        WizardStepKind.ModelAndServer => ValidateServerStep(),
        WizardStepKind.EvaluationDataset => ValidateDatasetStep(),
        WizardStepKind.Scoring => ValidateScoringStep(),
        _ => []
    };

    private List<string> ValidateContinueRunStep()
    {
        List<string> errors = [];
        if (_continueFromCheckpoint && string.IsNullOrWhiteSpace(_checkpointDatabasePath))
        {
            errors.Add("Checkpoint database file path is required when continuing from a checkpoint.");
        }
        else if (_continueFromCheckpoint && !string.IsNullOrWhiteSpace(_checkpointDatabasePath) && !File.Exists(_checkpointDatabasePath))
        {
            errors.Add($"Checkpoint database file does not exist: {_checkpointDatabasePath}");
        }
        return errors;
    }

    private List<string> ValidateServerStep()
    {
        List<string> errors = [];
        if (_manageServer)
        {
            if (_useLocalFile && string.IsNullOrWhiteSpace(_localModelPath))
                errors.Add("Model file path is required when using a local file.");
            if (!_useLocalFile && string.IsNullOrWhiteSpace(_hfRepo))
                errors.Add("HuggingFace repo is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_serverUrl))
                errors.Add("Server URL is required when connecting to an existing server.");
        }
        return errors;
    }

    private List<string> ValidateDatasetStep()
    {
        List<string> errors = [];

        if (_useSingleFileDataSource)
        {
            // Single file mode - file path is required and must exist
            if (string.IsNullOrWhiteSpace(_dataFilePath))
                errors.Add("Data file path is required when using single file mode.");
            else if (!File.Exists(_dataFilePath))
                errors.Add($"Data file does not exist: {_dataFilePath}");
        }
        else
        {
            // Directory mode - prompt directory is required and must exist
            if (string.IsNullOrWhiteSpace(_promptDir))
                errors.Add("Prompt directory path is required when using directory mode.");
            else if (!Directory.Exists(_promptDir))
                errors.Add($"Prompt directory does not exist: {_promptDir}");

            // Expected directory is optional, but if provided must exist
            if (!string.IsNullOrWhiteSpace(_expectedDir) && !Directory.Exists(_expectedDir))
                errors.Add($"Expected output directory does not exist: {_expectedDir}");
        }

        return errors;
    }

    private List<string> ValidateScoringStep()
    {
        List<string> errors = [];

        if (_enableJudge)
        {
            if (_judgeManageServer)
            {
                // Managed judge - model is required
                if (_judgeUseLocalFile && string.IsNullOrWhiteSpace(_judgeLocalModelPath))
                    errors.Add("Judge model file path is required when using a local file.");
                if (!_judgeUseLocalFile && string.IsNullOrWhiteSpace(_judgeHfRepo))
                    errors.Add("Judge HuggingFace repo is required.");
            }
            else
            {
                // External judge - URL is required
                if (string.IsNullOrWhiteSpace(_judgeServerUrl))
                    errors.Add("Judge server URL is required when connecting to an existing server.");
            }
        }

        return errors;
    }

    // ─── Step 1: Model & Server ───────────────────────────────────────────────

    public bool ManageServer { get => _manageServer; set => SetField(ref _manageServer, value); }
    public bool UseLocalFile { get => _useLocalFile; set => SetField(ref _useLocalFile, value); }
    public string? LocalModelPath { get => _localModelPath; set => SetField(ref _localModelPath, value); }
    public string? HfRepo { get => _hfRepo; set => SetField(ref _hfRepo, value); }
    public string? HfToken { get => _hfToken; set => SetField(ref _hfToken, value); }
    public string? ServerUrl { get => _serverUrl; set => SetField(ref _serverUrl, value); }

    /// <summary>Path to local llama-server executable (bypasses auto-update).</summary>
    public string? LlamaServerExecutablePath { get => _llamaServerExecutablePath; set => SetField(ref _llamaServerExecutablePath, value); }

    /// <summary>Server host address.</summary>
    public string Host { get => _host; set => SetField(ref _host, value); }

    /// <summary>Server port.</summary>
    public int Port { get => _port; set => SetField(ref _port, value); }

    /// <summary>API key for server authentication.</summary>
    public string? ApiKey { get => _apiKey; set => SetField(ref _apiKey, value); }

    // ─── Step 2: Performance ─────────────────────────────────────────────────

    // Context / batching
    public int? ContextWindowTokens { get => _contextWindowTokens; set => SetField(ref _contextWindowTokens, value); }
    public int? BatchSizeTokens { get => _batchSizeTokens; set => SetField(ref _batchSizeTokens, value); }
    public int? UbatchSizeTokens { get => _ubatchSizeTokens; set => SetField(ref _ubatchSizeTokens, value); }
    public int? ParallelSlotCount { get => _parallelSlotCount; set => SetField(ref _parallelSlotCount, value); }
    public bool? EnableContinuousBatching { get => _enableContinuousBatching; set => SetField(ref _enableContinuousBatching, value); }
    public bool? EnableCachePrompt { get => _enableCachePrompt; set => SetField(ref _enableCachePrompt, value); }
    public bool? EnableContextShift { get => _enableContextShift; set => SetField(ref _enableContextShift, value); }

    // GPU
    public int? GpuLayerCount { get => _gpuLayerCount; set => SetField(ref _gpuLayerCount, value); }
    public string? SplitMode { get => _splitMode; set => SetField(ref _splitMode, value); }
    public string? KvCacheTypeK { get => _kvCacheTypeK; set => SetField(ref _kvCacheTypeK, value); }
    public string? KvCacheTypeV { get => _kvCacheTypeV; set => SetField(ref _kvCacheTypeV, value); }
    public bool? EnableKvOffload { get => _enableKvOffload; set => SetField(ref _enableKvOffload, value); }
    public bool? EnableFlashAttention { get => _enableFlashAttention; set => SetField(ref _enableFlashAttention, value); }

    // Sampling
    public double? SamplingTemperature { get => _samplingTemperature; set => SetField(ref _samplingTemperature, value); }
    public double? TopP { get => _topP; set => SetField(ref _topP, value); }
    public int? TopK { get => _topK; set => SetField(ref _topK, value); }
    public double? MinP { get => _minP; set => SetField(ref _minP, value); }
    public double? RepeatPenalty { get => _repeatPenalty; set => SetField(ref _repeatPenalty, value); }
    public int? RepeatLastNTokens { get => _repeatLastNTokens; set => SetField(ref _repeatLastNTokens, value); }
    public double? PresencePenalty { get => _presencePenalty; set => SetField(ref _presencePenalty, value); }
    public double? FrequencyPenalty { get => _frequencyPenalty; set => SetField(ref _frequencyPenalty, value); }
    public int? Seed { get => _seed; set => SetField(ref _seed, value); }

    // Threading
    public int? ThreadCount { get => _threadCount; set => SetField(ref _threadCount, value); }
    public int? HttpThreadCount { get => _httpThreadCount; set => SetField(ref _httpThreadCount, value); }

    // Model behavior
    public string? ChatTemplate { get => _chatTemplate; set => SetField(ref _chatTemplate, value); }
    public bool? EnableJinja { get => _enableJinja; set => SetField(ref _enableJinja, value); }
    public string? ReasoningFormat { get => _reasoningFormat; set => SetField(ref _reasoningFormat, value); }
    public string? ModelAlias { get => _modelAlias; set => SetField(ref _modelAlias, value); }

    // Logging & Memory
    public int? LogVerbosity { get => _logVerbosity; set => SetField(ref _logVerbosity, value); }
    public bool? EnableMlock { get => _enableMlock; set => SetField(ref _enableMlock, value); }
    public bool? EnableMmap { get => _enableMmap; set => SetField(ref _enableMmap, value); }
    public double? ServerTimeoutSeconds { get => _serverTimeoutSeconds; set => SetField(ref _serverTimeoutSeconds, value); }

    // ─── Step 3: Dataset ──────────────────────────────────────────────────────

    public string PipelineName { get => _pipelineName; set => SetField(ref _pipelineName, value); }

    /// <summary>Selected index for the Pipeline Type ComboBox (0=CasualQA, 1=Translation, 2=CSharpCoding).</summary>
    public int SelectedPipelineIndex
    {
        get => PipelineName switch
        {
            "Translation" => 1,
            "CSharpCoding" => 2,
            _ => 0
        };
        set
        {
            var newName = value switch
            {
                1 => "Translation",
                2 => "CSharpCoding",
                _ => "CasualQA"
            };
            if (_pipelineName != newName)
            {
                SetField(ref _pipelineName, newName);
                OnPropertyChanged(nameof(PipelineName));
                OnPropertyChanged(nameof(SelectedPipelineIndex));
            }
        }
    }

    /// <summary>True if single file data source mode is selected.</summary>
    public bool UseSingleFileDataSource
    {
        get => _useSingleFileDataSource;
        set
        {
            if (SetField(ref _useSingleFileDataSource, value))
            {
                OnPropertyChanged(nameof(UseDirectoryDataSource));
                // Note: We do NOT clear path fields when switching modes
                // This allows users to switch back and forth without losing their paths
            }
        }
    }

    /// <summary>True if directory data source mode is selected.</summary>
    public bool UseDirectoryDataSource
    {
        get => !_useSingleFileDataSource;
        set
        {
            if (SetField(ref _useSingleFileDataSource, !value))
            {
                OnPropertyChanged(nameof(UseSingleFileDataSource));
                // Note: We do NOT clear path fields when switching modes
                // This allows users to switch back and forth without losing their paths
            }
        }
    }

    public string? DataFilePath { get => _dataFilePath; set => SetField(ref _dataFilePath, value); }
    public string? PromptDir { get => _promptDir; set => SetField(ref _promptDir, value); }
    public string? ExpectedDir { get => _expectedDir; set => SetField(ref _expectedDir, value); }

    // ─── Step 4: Scoring ─────────────────────────────────────────────────────

    public bool EnableJudge { get => _enableJudge; set => SetField(ref _enableJudge, value); }

    // Judge server management
    public bool JudgeManageServer { get => _judgeManageServer; set => SetField(ref _judgeManageServer, value); }
    public bool JudgeUseLocalFile { get => _judgeUseLocalFile; set => SetField(ref _judgeUseLocalFile, value); }
    public string? JudgeLocalModelPath { get => _judgeLocalModelPath; set => SetField(ref _judgeLocalModelPath, value); }
    public string? JudgeHfRepo { get => _judgeHfRepo; set => SetField(ref _judgeHfRepo, value); }
    public string? JudgeHfToken { get => _judgeHfToken; set => SetField(ref _judgeHfToken, value); }
    public string? JudgeApiKey { get => _judgeApiKey; set => SetField(ref _judgeApiKey, value); }
    public string? JudgeServerUrl { get => _judgeServerUrl; set => SetField(ref _judgeServerUrl, value); }

    /// <summary>Path to local judge llama-server executable (bypasses auto-update).</summary>
    public string? JudgeExecutablePath { get => _judgeExecutablePath; set => SetField(ref _judgeExecutablePath, value); }

    // Judge performance settings - FULL FEATURE PARITY WITH MAIN SERVER
    public int? JudgeContextWindowTokens { get => _judgeContextWindowTokens; set => SetField(ref _judgeContextWindowTokens, value); }
    public int? JudgeBatchSizeTokens { get => _judgeBatchSizeTokens; set => SetField(ref _judgeBatchSizeTokens, value); }
    public int? JudgeParallelSlotCount { get => _judgeParallelSlotCount; set => SetField(ref _judgeParallelSlotCount, value); }
    public string? JudgeSplitMode { get => _judgeSplitMode; set => SetField(ref _judgeSplitMode, value); }
    public string? JudgeKvCacheTypeK { get => _judgeKvCacheTypeK; set => SetField(ref _judgeKvCacheTypeK, value); }
    public string? JudgeKvCacheTypeV { get => _judgeKvCacheTypeV; set => SetField(ref _judgeKvCacheTypeV, value); }
    public bool? JudgeEnableFlashAttention { get => _judgeEnableFlashAttention; set => SetField(ref _judgeEnableFlashAttention, value); }

    // Judge sampling settings
    public double? JudgeSamplingTemperature { get => _judgeSamplingTemperature; set => SetField(ref _judgeSamplingTemperature, value); }
    public double? JudgeTopP { get => _judgeTopP; set => SetField(ref _judgeTopP, value); }
    public int? JudgeTopK { get => _judgeTopK; set => SetField(ref _judgeTopK, value); }
    public double? JudgeMinP { get => _judgeMinP; set => SetField(ref _judgeMinP, value); }
    public double? JudgeRepeatPenalty { get => _judgeRepeatPenalty; set => SetField(ref _judgeRepeatPenalty, value); }
    public int? JudgeSeed { get => _judgeSeed; set => SetField(ref _judgeSeed, value); }

    // Judge threading & memory
    public int? JudgeGpuLayerCount { get => _judgeGpuLayerCount; set => SetField(ref _judgeGpuLayerCount, value); }
    public int? JudgeThreadCount { get => _judgeThreadCount; set => SetField(ref _judgeThreadCount, value); }
    public int? JudgeHttpThreadCount { get => _judgeHttpThreadCount; set => SetField(ref _judgeHttpThreadCount, value); }
    public bool? JudgeEnableMlock { get => _judgeEnableMlock; set => SetField(ref _judgeEnableMlock, value); }
    public bool? JudgeEnableMmap { get => _judgeEnableMmap; set => SetField(ref _judgeEnableMmap, value); }

    // Judge model behavior & logging
    public string? JudgeChatTemplate { get => _judgeChatTemplate; set => SetField(ref _judgeChatTemplate, value); }
    public bool? JudgeEnableJinja { get => _judgeEnableJinja; set => SetField(ref _judgeEnableJinja, value); }
    public int? JudgeLogVerbosity { get => _judgeLogVerbosity; set => SetField(ref _judgeLogVerbosity, value); }
    public double? JudgeServerTimeoutSeconds { get => _judgeServerTimeoutSeconds; set => SetField(ref _judgeServerTimeoutSeconds, value); }

    // Judge scoring settings
    public string? JudgeUrl { get => _judgeServerUrl; set => SetField(ref _judgeServerUrl, value); }
    public string JudgeTemplate { get => _judgeTemplate; set => SetField(ref _judgeTemplate, value); }

    /// <summary>Selected index for the Judge Template ComboBox.</summary>
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
            if (_judgeTemplate != newTemplate)
            {
                SetField(ref _judgeTemplate, newTemplate);
                OnPropertyChanged(nameof(JudgeTemplate));
                OnPropertyChanged(nameof(SelectedJudgeTemplateIndex));
            }
        }
    }

    /// <summary>
    /// Gets the available judge template names using reflection from DefaultTemplates class.
    /// Returns kebab-case names (e.g., "standard", "pass-fail").
    /// </summary>
    public static string[] JudgeTemplateNames { get; } = GetJudgeTemplateNamesStatic();

    private static string[] GetJudgeTemplateNamesStatic()
    {
        var templateType = typeof(Core.DefaultTemplates);
        var constants = templateType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .Order()
            .ToArray();

        // Convert PascalCase names to kebab-case for UI (e.g., "PassFail" -> "pass-fail")
        return constants.Select(name =>
            System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", "-$1").ToLowerInvariant()
        ).ToArray();
    }

    /// <summary>
    /// Gets the available judge template names using reflection from DefaultTemplates class.
    /// Returns kebab-case names (e.g., "standard", "pass-fail").
    /// </summary>
    private static string[] GetJudgeTemplateNames()
    {
        var templateType = typeof(Core.DefaultTemplates);
        var constants = templateType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .Order()
            .ToArray();

        // Convert PascalCase names to kebab-case for UI (e.g., "PassFail" -> "pass-fail")
        return constants.Select(name =>
            CapitalLetterSplitRegex().Replace(name, "-$1").ToLowerInvariant()
        ).ToArray();
    }

    public double JudgeScoreMin { get => _judgeScoreMin; set => SetField(ref _judgeScoreMin, value); }
    public double JudgeScoreMax { get => _judgeScoreMax; set => SetField(ref _judgeScoreMax, value); }

    // ─── Step 5: Output ───────────────────────────────────────────────────────

    public string? CheckpointDatabasePath { get => _checkpointDatabasePath; set => SetField(ref _checkpointDatabasePath, value); }
    public string OutputDir { get => _outputDir; set => SetField(ref _outputDir, value); }
    public string? RunName { get => _runName; set => SetField(ref _runName, value); }
    public ShellTarget? ShellTarget { get => _shellTarget; set => SetField(ref _shellTarget, value); }

    // Output settings (from Settings screen)
    public bool WritePerEvalJson { get => _writePerEvalJson; set => SetField(ref _writePerEvalJson, value); }
    public bool WriteSummaryJson { get => _writeSummaryJson; set => SetField(ref _writeSummaryJson, value); }
    public bool WriteSummaryCsv { get => _writeSummaryCsv; set => SetField(ref _writeSummaryCsv, value); }
    public bool WriteResultsParquet { get => _writeResultsParquet; set => SetField(ref _writeResultsParquet, value); }
    public bool IncludeRawLlmResponse { get => _includeRawLlmResponse; set => SetField(ref _includeRawLlmResponse, value); }
    public bool ContinueOnEvalFailure { get => _continueOnEvalFailure; set => SetField(ref _continueOnEvalFailure, value); }
    public int? MaxConcurrentEvals { get => _maxConcurrentEvals; set => SetField(ref _maxConcurrentEvals, value); }
    public bool ContinueFromCheckpoint { get => _continueFromCheckpoint; set => SetField(ref _continueFromCheckpoint, value); }

    // ─── Build config from wizard state ──────────────────────────────────────

    public PartialConfig BuildPartialConfig()
    {
        // Only include fields that the user has explicitly modified
        var model = _editedFields.Contains(nameof(ManageServer)) || _editedFields.Contains(nameof(UseLocalFile)) ||
                    _editedFields.Contains(nameof(LocalModelPath)) || _editedFields.Contains(nameof(HfRepo))
            ? (ManageServer
                ? (UseLocalFile && LocalModelPath != null
                    ? new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = LocalModelPath }
                    : HfRepo != null
                        ? new ModelSource { Kind = ModelSourceKind.HuggingFace, HfRepo = HfRepo, HfToken = HfToken }
                        : null)
                : null)
            : null;

        var server = _editedFields.Contains(nameof(ManageServer)) || _editedFields.Contains(nameof(Host)) ||
                     _editedFields.Contains(nameof(Port)) || _editedFields.Contains(nameof(ApiKey)) ||
                     _editedFields.Contains(nameof(ServerUrl)) || _editedFields.Contains(nameof(LlamaServerExecutablePath)) ||
                     _editedFields.Contains(nameof(LocalModelPath)) || _editedFields.Contains(nameof(HfRepo)) ||
                     _editedFields.Contains(nameof(HfToken))
            ? new PartialServerConfig
            {
                Manage = _editedFields.Contains(nameof(ManageServer)) ? ManageServer : null,
                Model = model,
                Host = _editedFields.Contains(nameof(Host)) && ManageServer ? Host : null,
                Port = _editedFields.Contains(nameof(Port)) && ManageServer ? Port : null,
                ApiKey = _editedFields.Contains(nameof(ApiKey)) ? ApiKey : null,
                ExecutablePath = _editedFields.Contains(nameof(LlamaServerExecutablePath)) ? _llamaServerExecutablePath : null,
                BaseUrl = _editedFields.Contains(nameof(ServerUrl)) && !ManageServer ? ServerUrl : null
            }
            : null;

        // Build llama-server settings - only include edited fields
        var llamaSettings = BuildLlamaServerSettings();

        // Build judge config - only include if judge-related fields were edited
        var judge = _editedFields.Contains(nameof(EnableJudge)) || _editedFields.Contains(nameof(JudgeManageServer)) ||
                    _editedFields.Contains(nameof(JudgeLocalModelPath)) || _editedFields.Contains(nameof(JudgeHfRepo)) ||
                    _editedFields.Contains(nameof(JudgeApiKey)) || _editedFields.Contains(nameof(JudgeServerUrl)) ||
                    _editedFields.Contains(nameof(SelectedJudgeTemplateIndex)) || _editedFields.Contains(nameof(JudgeScoreMin)) ||
                    _editedFields.Contains(nameof(JudgeScoreMax))
            ? (EnableJudge ? BuildJudgeConfig() : new PartialJudgeConfig { Enable = false })
            : null;

        // Build data source - only include if data source fields were edited
        var dataSourceEdited = _editedFields.Contains(nameof(UseSingleFileDataSource)) ||
                               _editedFields.Contains(nameof(DataFilePath)) ||
                               _editedFields.Contains(nameof(PromptDir)) ||
                               _editedFields.Contains(nameof(ExpectedDir));
        var dataSource = dataSourceEdited ? (UseSingleFileDataSource ?
            new DataSourceConfig { Kind = DataSourceKind.SingleFile, FilePath = DataFilePath } :
            new DataSourceConfig { Kind = DataSourceKind.SplitDirectories, PromptDirectoryPath = PromptDir, ExpectedOutputDirectoryPath = ExpectedDir }) : null;

        var evalSet = dataSource != null || _editedFields.Contains(nameof(PipelineName))
            ? new EvalSetConfig
            {
                PipelineName = _editedFields.Contains(nameof(PipelineName)) ? PipelineName : "CasualQA",
                DataSource = dataSource ?? new DataSourceConfig { Kind = DataSourceKind.SingleFile }
            }
            : null;

        // Build run meta - only include edited fields
        var runEdited = _editedFields.Contains(nameof(RunName)) || _editedFields.Contains(nameof(OutputDir)) ||
                        _editedFields.Contains(nameof(ShellTarget)) || _editedFields.Contains(nameof(ContinueFromCheckpoint)) ||
                        _editedFields.Contains(nameof(ContinueOnEvalFailure)) || _editedFields.Contains(nameof(MaxConcurrentEvals));
        var run = runEdited ? new PartialRunMeta
        {
            RunName = _editedFields.Contains(nameof(RunName)) ? RunName : null,
            OutputDirectoryPath = _editedFields.Contains(nameof(OutputDir)) ? OutputDir : null,
            ExportShellTarget = _editedFields.Contains(nameof(ShellTarget)) ? ShellTarget : null,
            ContinueFromCheckpoint = _editedFields.Contains(nameof(ContinueFromCheckpoint)) ? _continueFromCheckpoint : null,
            ContinueOnEvalFailure = _editedFields.Contains(nameof(ContinueOnEvalFailure)) ? (_continueOnEvalFailure ? true : null) : null,
            MaxConcurrentEvals = _editedFields.Contains(nameof(MaxConcurrentEvals)) ? _maxConcurrentEvals : null,
        } : null;

        // Build output config - only include if output-related fields were edited
        var outputEdited = _editedFields.Contains(nameof(WritePerEvalJson)) || _editedFields.Contains(nameof(WriteSummaryJson)) ||
                           _editedFields.Contains(nameof(WriteSummaryCsv)) || _editedFields.Contains(nameof(WriteResultsParquet)) ||
                           _editedFields.Contains(nameof(IncludeRawLlmResponse));
        OutputConfig? output = outputEdited ? new OutputConfig
        {
            WritePerEvalJson = _editedFields.Contains(nameof(WritePerEvalJson)) ? _writePerEvalJson : true,
            WriteSummaryJson = _editedFields.Contains(nameof(WriteSummaryJson)) ? _writeSummaryJson : true,
            WriteSummaryCsv = _editedFields.Contains(nameof(WriteSummaryCsv)) ? _writeSummaryCsv : false,
            WriteResultsParquet = _editedFields.Contains(nameof(WriteResultsParquet)) ? _writeResultsParquet : false,
            IncludeRawLlmResponse = _editedFields.Contains(nameof(IncludeRawLlmResponse)) ? _includeRawLlmResponse : true,
            ShellTarget = _editedFields.Contains(nameof(ShellTarget)) ? ShellTarget : null,
            OutputDir = _editedFields.Contains(nameof(OutputDir)) ? OutputDir : null,
        } : null;

        return new PartialConfig
        {
            Server = server,
            LlamaServer = llamaSettings,
            EvalSets = evalSet != null ? [evalSet] : [],
            Judge = judge,
            Run = run,
            Output = output,
            DataSource = dataSourceEdited ? new PartialDataSourceConfig
            {
                Kind = _editedFields.Contains(nameof(UseSingleFileDataSource)) ? (UseSingleFileDataSource ? DataSourceKind.SingleFile : DataSourceKind.SplitDirectories) : null,
                FilePath = _editedFields.Contains(nameof(DataFilePath)) && UseSingleFileDataSource ? DataFilePath : null,
                PromptDirectoryPath = _editedFields.Contains(nameof(PromptDir)) && !UseSingleFileDataSource ? PromptDir : null,
                ExpectedOutputDirectoryPath = _editedFields.Contains(nameof(ExpectedDir)) && !UseSingleFileDataSource ? ExpectedDir : null,
            } : null,
        };
    }

    private PartialLlamaServerSettings? BuildLlamaServerSettings()
    {
        // Only include fields that were explicitly edited
        var hasAnyValue = _editedFields.Contains(nameof(ContextWindowTokens)) || _editedFields.Contains(nameof(BatchSizeTokens)) ||
            _editedFields.Contains(nameof(UbatchSizeTokens)) || _editedFields.Contains(nameof(ParallelSlotCount)) ||
            _editedFields.Contains(nameof(EnableContinuousBatching)) || _editedFields.Contains(nameof(EnableCachePrompt)) ||
            _editedFields.Contains(nameof(EnableContextShift)) || _editedFields.Contains(nameof(GpuLayerCount)) ||
            _editedFields.Contains(nameof(SplitMode)) || _editedFields.Contains(nameof(KvCacheTypeK)) ||
            _editedFields.Contains(nameof(KvCacheTypeV)) || _editedFields.Contains(nameof(EnableKvOffload)) ||
            _editedFields.Contains(nameof(EnableFlashAttention)) || _editedFields.Contains(nameof(SamplingTemperature)) ||
            _editedFields.Contains(nameof(TopP)) || _editedFields.Contains(nameof(TopK)) ||
            _editedFields.Contains(nameof(MinP)) || _editedFields.Contains(nameof(RepeatPenalty)) ||
            _editedFields.Contains(nameof(RepeatLastNTokens)) || _editedFields.Contains(nameof(PresencePenalty)) ||
            _editedFields.Contains(nameof(FrequencyPenalty)) || _editedFields.Contains(nameof(Seed)) ||
            _editedFields.Contains(nameof(ThreadCount)) || _editedFields.Contains(nameof(HttpThreadCount)) ||
            _editedFields.Contains(nameof(ChatTemplate)) || _editedFields.Contains(nameof(EnableJinja)) ||
            _editedFields.Contains(nameof(ReasoningFormat)) || _editedFields.Contains(nameof(ModelAlias)) ||
            _editedFields.Contains(nameof(LogVerbosity)) || _editedFields.Contains(nameof(EnableMlock)) ||
            _editedFields.Contains(nameof(EnableMmap)) || _editedFields.Contains(nameof(ServerTimeoutSeconds));

        if (!hasAnyValue) return null;

        return new PartialLlamaServerSettings
        {
            ContextWindowTokens = _editedFields.Contains(nameof(ContextWindowTokens)) ? ContextWindowTokens : null,
            BatchSizeTokens = _editedFields.Contains(nameof(BatchSizeTokens)) ? BatchSizeTokens : null,
            UbatchSizeTokens = _editedFields.Contains(nameof(UbatchSizeTokens)) ? UbatchSizeTokens : null,
            ParallelSlotCount = _editedFields.Contains(nameof(ParallelSlotCount)) ? ParallelSlotCount : null,
            EnableContinuousBatching = _editedFields.Contains(nameof(EnableContinuousBatching)) ? EnableContinuousBatching : null,
            EnableCachePrompt = _editedFields.Contains(nameof(EnableCachePrompt)) ? EnableCachePrompt : null,
            EnableContextShift = _editedFields.Contains(nameof(EnableContextShift)) ? EnableContextShift : null,
            GpuLayerCount = _editedFields.Contains(nameof(GpuLayerCount)) ? GpuLayerCount : null,
            SplitMode = _editedFields.Contains(nameof(SplitMode)) ? SplitMode : null,
            KvCacheTypeK = _editedFields.Contains(nameof(KvCacheTypeK)) ? KvCacheTypeK : null,
            KvCacheTypeV = _editedFields.Contains(nameof(KvCacheTypeV)) ? KvCacheTypeV : null,
            EnableKvOffload = _editedFields.Contains(nameof(EnableKvOffload)) ? EnableKvOffload : null,
            EnableFlashAttention = _editedFields.Contains(nameof(EnableFlashAttention)) ? EnableFlashAttention : null,
            SamplingTemperature = _editedFields.Contains(nameof(SamplingTemperature)) ? SamplingTemperature : null,
            TopP = _editedFields.Contains(nameof(TopP)) ? TopP : null,
            TopK = _editedFields.Contains(nameof(TopK)) ? TopK : null,
            MinP = _editedFields.Contains(nameof(MinP)) ? MinP : null,
            RepeatPenalty = _editedFields.Contains(nameof(RepeatPenalty)) ? RepeatPenalty : null,
            RepeatLastNTokens = _editedFields.Contains(nameof(RepeatLastNTokens)) ? RepeatLastNTokens : null,
            PresencePenalty = _editedFields.Contains(nameof(PresencePenalty)) ? PresencePenalty : null,
            FrequencyPenalty = _editedFields.Contains(nameof(FrequencyPenalty)) ? FrequencyPenalty : null,
            Seed = _editedFields.Contains(nameof(Seed)) ? Seed : null,
            ThreadCount = _editedFields.Contains(nameof(ThreadCount)) ? ThreadCount : null,
            HttpThreadCount = _editedFields.Contains(nameof(HttpThreadCount)) ? HttpThreadCount : null,
            ChatTemplate = _editedFields.Contains(nameof(ChatTemplate)) ? ChatTemplate : null,
            EnableJinja = _editedFields.Contains(nameof(EnableJinja)) ? EnableJinja : null,
            ReasoningFormat = _editedFields.Contains(nameof(ReasoningFormat)) ? ReasoningFormat : null,
            ModelAlias = _editedFields.Contains(nameof(ModelAlias)) ? ModelAlias : null,
            LogVerbosity = _editedFields.Contains(nameof(LogVerbosity)) ? LogVerbosity : null,
            EnableMlock = _editedFields.Contains(nameof(EnableMlock)) ? EnableMlock : null,
            EnableMmap = _editedFields.Contains(nameof(EnableMmap)) ? EnableMmap : null,
            ServerTimeoutSeconds = _editedFields.Contains(nameof(ServerTimeoutSeconds)) ? ServerTimeoutSeconds : null
        };
    }

    private PartialJudgeConfig? BuildJudgeConfig()
    {
        var judgeModel = _editedFields.Contains(nameof(JudgeManageServer)) || _editedFields.Contains(nameof(JudgeUseLocalFile)) ||
                         _editedFields.Contains(nameof(JudgeLocalModelPath)) || _editedFields.Contains(nameof(JudgeHfRepo))
            ? (JudgeManageServer
                ? (JudgeUseLocalFile && !string.IsNullOrEmpty(JudgeLocalModelPath)
                    ? new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = JudgeLocalModelPath }
                    : !string.IsNullOrEmpty(JudgeHfRepo)
                        ? new ModelSource { Kind = ModelSourceKind.HuggingFace, HfRepo = JudgeHfRepo, HfToken = JudgeHfToken }
                        : null)
                : null)
            : null;

        // Build judge-specific llama-server settings with full feature parity
        var judgeServerSettings = BuildJudgeLlamaServerSettings();

        // Default judge executable path to primary server's executable path if not specified
        var judgeExecutablePath = _editedFields.Contains(nameof(JudgeExecutablePath)) ? _judgeExecutablePath : _llamaServerExecutablePath;

        return new PartialJudgeConfig
        {
            Enable = true,
            ServerConfig = new PartialServerConfig
            {
                Manage = _editedFields.Contains(nameof(JudgeManageServer)) ? JudgeManageServer : null,
                Model = judgeModel,
                Host = _editedFields.Contains(nameof(JudgeManageServer)) && JudgeManageServer ? "127.0.0.1" : null,
                Port = _editedFields.Contains(nameof(JudgeManageServer)) && JudgeManageServer ? 8081 : null,
                ApiKey = _editedFields.Contains(nameof(JudgeApiKey)) ? JudgeApiKey : null,
                ExecutablePath = judgeExecutablePath,
                BaseUrl = _editedFields.Contains(nameof(JudgeManageServer)) && !JudgeManageServer ? JudgeServerUrl : null
            },
            ServerSettings = judgeServerSettings,
            BaseUrl = _editedFields.Contains(nameof(JudgeServerUrl)) && !JudgeManageServer ? JudgeServerUrl : null,
            JudgePromptTemplate = _editedFields.Contains(nameof(JudgeTemplate)) ? JudgeTemplate : null,
            ScoreMinValue = _editedFields.Contains(nameof(JudgeScoreMin)) ? JudgeScoreMin : 0,
            ScoreMaxValue = _editedFields.Contains(nameof(JudgeScoreMax)) ? JudgeScoreMax : 10
        };
    }

    private PartialLlamaServerSettings? BuildJudgeLlamaServerSettings()
    {
        // Only include fields that were explicitly edited
        var hasAnyValue = _editedFields.Contains(nameof(JudgeContextWindowTokens)) || _editedFields.Contains(nameof(JudgeBatchSizeTokens)) ||
            _editedFields.Contains(nameof(JudgeParallelSlotCount)) || _editedFields.Contains(nameof(JudgeSplitMode)) ||
            _editedFields.Contains(nameof(JudgeKvCacheTypeK)) || _editedFields.Contains(nameof(JudgeKvCacheTypeV)) ||
            _editedFields.Contains(nameof(JudgeEnableFlashAttention)) || _editedFields.Contains(nameof(JudgeSamplingTemperature)) ||
            _editedFields.Contains(nameof(JudgeTopP)) || _editedFields.Contains(nameof(JudgeTopK)) ||
            _editedFields.Contains(nameof(JudgeMinP)) || _editedFields.Contains(nameof(JudgeRepeatPenalty)) ||
            _editedFields.Contains(nameof(JudgeSeed)) || _editedFields.Contains(nameof(JudgeThreadCount)) ||
            _editedFields.Contains(nameof(JudgeHttpThreadCount)) || _editedFields.Contains(nameof(JudgeChatTemplate)) ||
            _editedFields.Contains(nameof(JudgeEnableJinja)) || _editedFields.Contains(nameof(JudgeLogVerbosity)) ||
            _editedFields.Contains(nameof(JudgeEnableMlock)) || _editedFields.Contains(nameof(JudgeEnableMmap)) ||
            _editedFields.Contains(nameof(JudgeServerTimeoutSeconds));

        if (!hasAnyValue) return null;

        return new PartialLlamaServerSettings
        {
            ContextWindowTokens = _editedFields.Contains(nameof(JudgeContextWindowTokens)) ? JudgeContextWindowTokens : null,
            BatchSizeTokens = _editedFields.Contains(nameof(JudgeBatchSizeTokens)) ? JudgeBatchSizeTokens : null,
            ParallelSlotCount = _editedFields.Contains(nameof(JudgeParallelSlotCount)) ? JudgeParallelSlotCount : null,
            SplitMode = _editedFields.Contains(nameof(JudgeSplitMode)) ? JudgeSplitMode : null,
            KvCacheTypeK = _editedFields.Contains(nameof(JudgeKvCacheTypeK)) ? JudgeKvCacheTypeK : null,
            KvCacheTypeV = _editedFields.Contains(nameof(JudgeKvCacheTypeV)) ? JudgeKvCacheTypeV : null,
            EnableFlashAttention = _editedFields.Contains(nameof(JudgeEnableFlashAttention)) ? JudgeEnableFlashAttention : null,
            SamplingTemperature = _editedFields.Contains(nameof(JudgeSamplingTemperature)) ? JudgeSamplingTemperature : null,
            TopP = _editedFields.Contains(nameof(JudgeTopP)) ? JudgeTopP : null,
            TopK = _editedFields.Contains(nameof(JudgeTopK)) ? JudgeTopK : null,
            MinP = _editedFields.Contains(nameof(JudgeMinP)) ? JudgeMinP : null,
            RepeatPenalty = _editedFields.Contains(nameof(JudgeRepeatPenalty)) ? JudgeRepeatPenalty : null,
            Seed = _editedFields.Contains(nameof(JudgeSeed)) ? JudgeSeed : null,
            ThreadCount = _editedFields.Contains(nameof(JudgeThreadCount)) ? JudgeThreadCount : null,
            HttpThreadCount = _editedFields.Contains(nameof(JudgeHttpThreadCount)) ? JudgeHttpThreadCount : null,
            ChatTemplate = _editedFields.Contains(nameof(JudgeChatTemplate)) ? JudgeChatTemplate : null,
            EnableJinja = _editedFields.Contains(nameof(JudgeEnableJinja)) ? JudgeEnableJinja : null,
            LogVerbosity = _editedFields.Contains(nameof(JudgeLogVerbosity)) ? JudgeLogVerbosity : null,
            EnableMlock = _editedFields.Contains(nameof(JudgeEnableMlock)) ? JudgeEnableMlock : null,
            EnableMmap = _editedFields.Contains(nameof(JudgeEnableMmap)) ? JudgeEnableMmap : null,
            ServerTimeoutSeconds = _editedFields.Contains(nameof(JudgeServerTimeoutSeconds)) ? JudgeServerTimeoutSeconds : null
        };
    }

    // ─── Browse actions ───────────────────────────────────────────────────────

    private async Task BrowseLocalModelAsync(string? parameter)
    {
        if (_filePicker == null) return;

        if (parameter == "executable")
        {
            // Browse for llama-server executable
            var path = await _filePicker.ShowOpenFileDialogAsync(
                "Select llama-server Executable",
                "Executable Files|llama-server;llama-server.exe|All Files|*.*");
            if (path != null)
            {
                LlamaServerExecutablePath = path;
            }
        }
        else
        {
            // Browse for model file (default behavior)
            var path = await _filePicker.ShowOpenFileDialogAsync("Select Model File", "Model Files|*.gguf|All Files|*.*");
            if (path != null)
            {
                LocalModelPath = path;
            }
        }
    }

    private async Task BrowseDataFileAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFileDialogAsync("Select Data File", "Data Files|*.json;*.yaml;*.yml;*.csv;*.parquet;*.jsonl|All Files|*.*");
        if (path != null)
        {
            DataFilePath = path;
        }
    }

    private async Task BrowsePromptDirAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFolderDialogAsync("Select Prompt Directory");
        if (path != null)
        {
            PromptDir = path;
        }
    }

    private async Task BrowseExpectedDirAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFolderDialogAsync("Select Expected Output Directory");
        if (path != null)
        {
            ExpectedDir = path;
        }
    }

    private async Task BrowseOutputDirAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFolderDialogAsync("Select Output Directory");
        if (path != null)
        {
            OutputDir = path;
        }
    }

    private async Task BrowseJudgeModelAsync(string? parameter)
    {
        if (_filePicker == null) return;

        if (parameter == "executable")
        {
            // Browse for judge llama-server executable
            var path = await _filePicker.ShowOpenFileDialogAsync(
                "Select Judge llama-server Executable",
                "Executable Files|llama-server;llama-server.exe|All Files|*.*");
            if (path != null)
            {
                JudgeExecutablePath = path;
            }
        }
        else
        {
            // Browse for judge model file (default behavior)
            var path = await _filePicker.ShowOpenFileDialogAsync("Select Judge Model File", "Model Files|*.gguf|All Files|*.*");
            if (path != null)
            {
                JudgeLocalModelPath = path;
            }
        }
    }

    private async Task BrowseCheckpointDbAsync()
    {
        if (_filePicker == null) return;
        var path = await _filePicker.ShowOpenFileDialogAsync("Select Checkpoint Database", "SQLite Database|*.db|All Files|*.*");
        if (path != null)
        {
            CheckpointDatabasePath = path;
            // When a checkpoint is selected, automatically set ContinueFromCheckpoint to true
            ContinueFromCheckpoint = true;
        }
    }

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            OnShowNotification?.Invoke("Please enter a server URL first.");
            return;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"{ServerUrl.TrimEnd('/')}/health");
            if (response.IsSuccessStatusCode)
            {
                OnShowNotification?.Invoke("Connection successful! Server is healthy.");
            }
            else
            {
                OnShowNotification?.Invoke($"Server responded with status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            OnShowNotification?.Invoke($"Connection failed: {ex.Message}");
        }
    }

    private async Task TestJudgeConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(JudgeUrl))
        {
            OnShowNotification?.Invoke("Please enter a judge server URL first.");
            return;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"{JudgeUrl.TrimEnd('/')}/health");
            if (response.IsSuccessStatusCode)
            {
                OnShowNotification?.Invoke("Judge connection successful! Server is healthy.");
            }
            else
            {
                OnShowNotification?.Invoke($"Judge server responded with status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            OnShowNotification?.Invoke($"Judge connection failed: {ex.Message}");
        }
    }

    // ─── Reset to defaults ────────────────────────────────────────────────────

    public void ResetToDefaults()
    {
        _currentStep = WizardStepKind.ContinueRun;
        _checkpointDatabasePath = null;

        // Server management
        _manageServer = true;
        _useLocalFile = true;
        _localModelPath = null;
        _hfRepo = null;
        _hfToken = null;
        _serverUrl = null;
        _llamaServerExecutablePath = null;

        // Server connection
        _host = "127.0.0.1";
        _port = 8080;
        _apiKey = null;

        // Context / batching
        _contextWindowTokens = null;
        _batchSizeTokens = null;
        _ubatchSizeTokens = null;
        _parallelSlotCount = null;
        _enableContinuousBatching = null;
        _enableCachePrompt = null;
        _enableContextShift = null;

        // GPU
        _gpuLayerCount = null;
        _splitMode = null;
        _kvCacheTypeK = null;
        _kvCacheTypeV = null;
        _enableKvOffload = true;
        _enableFlashAttention = null;

        // Sampling
        _samplingTemperature = null;
        _topP = null;
        _topK = null;
        _minP = null;
        _repeatPenalty = null;
        _repeatLastNTokens = null;
        _presencePenalty = null;
        _frequencyPenalty = null;
        _seed = null;

        // Threading
        _threadCount = null;
        _httpThreadCount = null;

        // Model behavior
        _chatTemplate = null;
        _enableJinja = null;
        _reasoningFormat = null;
        _modelAlias = null;

        // Logging & Memory
        _logVerbosity = null;
        _enableMlock = null;
        _enableMmap = true;
        _serverTimeoutSeconds = null;

        // Dataset
        _pipelineName = "CasualQA";
        _dataFilePath = null;
        _promptDir = null;
        _expectedDir = null;
        _useSingleFileDataSource = true;

        // Judge
        _enableJudge = false;
        _judgeManageServer = true;
        _judgeUseLocalFile = true;
        _judgeLocalModelPath = null;
        _judgeHfRepo = null;
        _judgeHfToken = null;
        _judgeServerUrl = null;
        _judgeExecutablePath = null;
        _judgeContextWindowTokens = null;
        _judgeBatchSizeTokens = null;
        _judgeParallelSlotCount = null;
        _judgeSplitMode = null;
        _judgeKvCacheTypeK = null;
        _judgeKvCacheTypeV = null;
        _judgeEnableFlashAttention = null;
        _judgeSamplingTemperature = null;
        _judgeTopP = null;
        _judgeTopK = null;
        _judgeMinP = null;
        _judgeRepeatPenalty = null;
        _judgeSeed = null;
        _judgeThreadCount = null;
        _judgeHttpThreadCount = null;
        _judgeChatTemplate = null;
        _judgeEnableJinja = null;
        _judgeLogVerbosity = null;
        _judgeEnableMlock = null;
        _judgeEnableMmap = null;
        _judgeServerTimeoutSeconds = null;
        _judgeGpuLayerCount = null;
        JudgeTemplate = "standard";  // Use property setter to trigger notifications
        _judgeScoreMin = 0;
        _judgeScoreMax = 10;

        // Output settings
        _writePerEvalJson = false;
        _writeSummaryJson = true;
        _writeSummaryCsv = false;
        _writeResultsParquet = false;
        _includeRawLlmResponse = true;
        _continueOnEvalFailure = true;
        _maxConcurrentEvals = null;

        // Run settings
        _runName = null;
        _outputDir = "./results";
        _shellTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Core.Models.ShellTarget.PowerShell : Core.Models.ShellTarget.Bash;
        _continueFromCheckpoint = false;

        // Clear the edited fields set after resetting
        _editedFields.Clear();

        // After reset, signal MainWindow to sync settings from loaded config files. Before OnPropertyChanged because the handler changes them without calling these events.
        ResetToDefaultsCompleted?.Invoke(this, EventArgs.Empty);

        // Notify all properties changed
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));

        // Server properties
        OnPropertyChanged(nameof(ManageServer));
        OnPropertyChanged(nameof(UseLocalFile));
        OnPropertyChanged(nameof(LocalModelPath));
        OnPropertyChanged(nameof(HfRepo));
        OnPropertyChanged(nameof(HfToken));
        OnPropertyChanged(nameof(ServerUrl));
        OnPropertyChanged(nameof(LlamaServerExecutablePath));
        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(ApiKey));

        // Performance properties
        OnPropertyChanged(nameof(ContextWindowTokens));
        OnPropertyChanged(nameof(BatchSizeTokens));
        OnPropertyChanged(nameof(UbatchSizeTokens));
        OnPropertyChanged(nameof(ParallelSlotCount));
        OnPropertyChanged(nameof(EnableContinuousBatching));
        OnPropertyChanged(nameof(EnableCachePrompt));
        OnPropertyChanged(nameof(EnableContextShift));
        OnPropertyChanged(nameof(GpuLayerCount));
        OnPropertyChanged(nameof(SplitMode));
        OnPropertyChanged(nameof(KvCacheTypeK));
        OnPropertyChanged(nameof(KvCacheTypeV));
        OnPropertyChanged(nameof(EnableKvOffload));
        OnPropertyChanged(nameof(EnableFlashAttention));
        OnPropertyChanged(nameof(SamplingTemperature));
        OnPropertyChanged(nameof(TopP));
        OnPropertyChanged(nameof(TopK));
        OnPropertyChanged(nameof(MinP));
        OnPropertyChanged(nameof(RepeatPenalty));
        OnPropertyChanged(nameof(RepeatLastNTokens));
        OnPropertyChanged(nameof(PresencePenalty));
        OnPropertyChanged(nameof(FrequencyPenalty));
        OnPropertyChanged(nameof(Seed));
        OnPropertyChanged(nameof(ThreadCount));
        OnPropertyChanged(nameof(HttpThreadCount));
        OnPropertyChanged(nameof(ChatTemplate));
        OnPropertyChanged(nameof(EnableJinja));
        OnPropertyChanged(nameof(ReasoningFormat));
        OnPropertyChanged(nameof(ModelAlias));
        OnPropertyChanged(nameof(LogVerbosity));
        OnPropertyChanged(nameof(EnableMlock));
        OnPropertyChanged(nameof(EnableMmap));
        OnPropertyChanged(nameof(ServerTimeoutSeconds));

        // Dataset properties
        OnPropertyChanged(nameof(PipelineName));
        OnPropertyChanged(nameof(SelectedPipelineIndex));
        OnPropertyChanged(nameof(DataFilePath));
        OnPropertyChanged(nameof(PromptDir));
        OnPropertyChanged(nameof(ExpectedDir));
        OnPropertyChanged(nameof(UseSingleFileDataSource));
        OnPropertyChanged(nameof(UseDirectoryDataSource));

        // Judge properties
        OnPropertyChanged(nameof(EnableJudge));
        OnPropertyChanged(nameof(JudgeManageServer));
        OnPropertyChanged(nameof(JudgeUseLocalFile));
        OnPropertyChanged(nameof(JudgeLocalModelPath));
        OnPropertyChanged(nameof(JudgeHfRepo));
        OnPropertyChanged(nameof(JudgeHfToken));
        OnPropertyChanged(nameof(JudgeServerUrl));
        OnPropertyChanged(nameof(JudgeExecutablePath));
        OnPropertyChanged(nameof(JudgeContextWindowTokens));
        OnPropertyChanged(nameof(JudgeBatchSizeTokens));
        OnPropertyChanged(nameof(JudgeParallelSlotCount));
        OnPropertyChanged(nameof(JudgeSplitMode));
        OnPropertyChanged(nameof(JudgeKvCacheTypeK));
        OnPropertyChanged(nameof(JudgeKvCacheTypeV));
        OnPropertyChanged(nameof(JudgeEnableFlashAttention));
        OnPropertyChanged(nameof(JudgeSamplingTemperature));
        OnPropertyChanged(nameof(JudgeTopP));
        OnPropertyChanged(nameof(JudgeTopK));
        OnPropertyChanged(nameof(JudgeMinP));
        OnPropertyChanged(nameof(JudgeRepeatPenalty));
        OnPropertyChanged(nameof(JudgeSeed));
        OnPropertyChanged(nameof(JudgeThreadCount));
        OnPropertyChanged(nameof(JudgeHttpThreadCount));
        OnPropertyChanged(nameof(JudgeChatTemplate));
        OnPropertyChanged(nameof(JudgeEnableJinja));
        OnPropertyChanged(nameof(JudgeLogVerbosity));
        OnPropertyChanged(nameof(JudgeEnableMlock));
        OnPropertyChanged(nameof(JudgeEnableMmap));
        OnPropertyChanged(nameof(JudgeServerTimeoutSeconds));
        OnPropertyChanged(nameof(JudgeGpuLayerCount));
        OnPropertyChanged(nameof(JudgeTemplate));
        OnPropertyChanged(nameof(SelectedJudgeTemplateIndex));
        OnPropertyChanged(nameof(JudgeScoreMin));
        OnPropertyChanged(nameof(JudgeScoreMax));

        // Output properties
        OnPropertyChanged(nameof(WritePerEvalJson));
        OnPropertyChanged(nameof(WriteSummaryJson));
        OnPropertyChanged(nameof(WriteSummaryCsv));
        OnPropertyChanged(nameof(WriteResultsParquet));
        OnPropertyChanged(nameof(IncludeRawLlmResponse));
        OnPropertyChanged(nameof(ContinueOnEvalFailure));
        OnPropertyChanged(nameof(MaxConcurrentEvals));
        OnPropertyChanged(nameof(RunName));
        OnPropertyChanged(nameof(OutputDir));
        OnPropertyChanged(nameof(ShellTarget));
        OnPropertyChanged(nameof(CheckpointDatabasePath));
        OnPropertyChanged(nameof(ContinueFromCheckpoint));
    }

    /// <summary>
    /// Event raised after ResetToDefaults completes.
    /// MainWindow listens to this to sync settings from loaded config files.
    /// </summary>
    public event EventHandler? ResetToDefaultsCompleted;

    // ─── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private bool SetField<T>(ref T f, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(f, v)) return false;
        f = v; OnPropertyChanged(n);

        // Track this field as edited by the user
        if (n != null)
        {
            _editedFields.Add(n);
        }

        // Navigation validity depends on multiple properties (current step and some fields).
        // Ensure CanGoBack/CanGoForward are re-evaluated and commands get updated whenever any bound property changes.
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));

        if (GoBackCommand is RelayCommand backCmd) backCmd.NotifyCanExecuteChanged();
        if (GoForwardCommand is RelayCommand fwdCmd) fwdCmd.NotifyCanExecuteChanged();

        return true;
    }

    // ─── Sync from loaded settings ────────────────────────────────────────────

    /// <summary>
    /// Syncs default values from resolved config (which includes Settings view fields) without marking fields as edited.
    /// </summary>
    public void SyncDefaultsFromSettings(ResolvedConfig config, SettingsViewModel? settingsVM = null)
    {
        // Server settings
        if (config.Server != null)
        {
            if (!_editedFields.Contains(nameof(ManageServer)))
                _manageServer = config.Server.Manage ?? true;
            if (!_editedFields.Contains(nameof(LlamaServerExecutablePath)))
                _llamaServerExecutablePath = config.Server.ExecutablePath;
            if (!_editedFields.Contains(nameof(Host)))
                _host = config.Server.Host ?? "127.0.0.1";
            if (!_editedFields.Contains(nameof(Port)))
                _port = config.Server.Port ?? 8080;
            if (!_editedFields.Contains(nameof(ApiKey)))
                _apiKey = config.Server.ApiKey;
        }

        // Llama server settings
        if (config.LlamaServer != null)
        {
            var llama = config.LlamaServer;
            if (!_editedFields.Contains(nameof(ContextWindowTokens)))
                _contextWindowTokens = llama.ContextWindowTokens;
            if (!_editedFields.Contains(nameof(BatchSizeTokens)))
                _batchSizeTokens = llama.BatchSizeTokens;
            if (!_editedFields.Contains(nameof(UbatchSizeTokens)))
                _ubatchSizeTokens = llama.UbatchSizeTokens;
            if (!_editedFields.Contains(nameof(ParallelSlotCount)))
                _parallelSlotCount = llama.ParallelSlotCount;
            if (!_editedFields.Contains(nameof(EnableContinuousBatching)))
                _enableContinuousBatching = llama.EnableContinuousBatching;
            if (!_editedFields.Contains(nameof(EnableCachePrompt)))
                _enableCachePrompt = llama.EnableCachePrompt;
            if (!_editedFields.Contains(nameof(EnableContextShift)))
                _enableContextShift = llama.EnableContextShift;
            if (!_editedFields.Contains(nameof(GpuLayerCount)))
                _gpuLayerCount = llama.GpuLayerCount;
            if (!_editedFields.Contains(nameof(SplitMode)))
                _splitMode = llama.SplitMode;
            if (!_editedFields.Contains(nameof(KvCacheTypeK)))
                _kvCacheTypeK = llama.KvCacheTypeK;
            if (!_editedFields.Contains(nameof(KvCacheTypeV)))
                _kvCacheTypeV = llama.KvCacheTypeV;
            if (!_editedFields.Contains(nameof(EnableKvOffload)))
                _enableKvOffload = llama.EnableKvOffload;
            if (!_editedFields.Contains(nameof(EnableFlashAttention)))
                _enableFlashAttention = llama.EnableFlashAttention;
            if (!_editedFields.Contains(nameof(SamplingTemperature)))
                _samplingTemperature = llama.SamplingTemperature;
            if (!_editedFields.Contains(nameof(TopP)))
                _topP = llama.TopP;
            if (!_editedFields.Contains(nameof(TopK)))
                _topK = llama.TopK;
            if (!_editedFields.Contains(nameof(MinP)))
                _minP = llama.MinP;
            if (!_editedFields.Contains(nameof(RepeatPenalty)))
                _repeatPenalty = llama.RepeatPenalty;
            if (!_editedFields.Contains(nameof(RepeatLastNTokens)))
                _repeatLastNTokens = llama.RepeatLastNTokens;
            if (!_editedFields.Contains(nameof(PresencePenalty)))
                _presencePenalty = llama.PresencePenalty;
            if (!_editedFields.Contains(nameof(FrequencyPenalty)))
                _frequencyPenalty = llama.FrequencyPenalty;
            if (!_editedFields.Contains(nameof(Seed)))
                _seed = llama.Seed;
            if (!_editedFields.Contains(nameof(ThreadCount)))
                _threadCount = llama.ThreadCount;
            if (!_editedFields.Contains(nameof(HttpThreadCount)))
                _httpThreadCount = llama.HttpThreadCount;
            if (!_editedFields.Contains(nameof(ChatTemplate)))
                _chatTemplate = llama.ChatTemplate;
            if (!_editedFields.Contains(nameof(EnableJinja)))
                _enableJinja = llama.EnableJinja;
            if (!_editedFields.Contains(nameof(ReasoningFormat)))
                _reasoningFormat = llama.ReasoningFormat;
            if (!_editedFields.Contains(nameof(ModelAlias)))
                _modelAlias = llama.ModelAlias;
            if (!_editedFields.Contains(nameof(LogVerbosity)))
                _logVerbosity = llama.LogVerbosity;
            if (!_editedFields.Contains(nameof(EnableMlock)))
                _enableMlock = llama.EnableMlock;
            if (!_editedFields.Contains(nameof(EnableMmap)))
                _enableMmap = llama.EnableMmap;
            if (!_editedFields.Contains(nameof(ServerTimeoutSeconds)))
                _serverTimeoutSeconds = llama.ServerTimeoutSeconds;
        }

        // Judge settings
        if (config.Judge != null)
        {
            var judge = config.Judge;
            if (!_editedFields.Contains(nameof(EnableJudge)))
                _enableJudge = judge.Enable;
            if (!_editedFields.Contains(nameof(JudgeManageServer)))
                _judgeManageServer = judge.Manage;
            if (!_editedFields.Contains(nameof(JudgeExecutablePath)))
                _judgeExecutablePath = judge.ServerConfig.ExecutablePath;
            if (!_editedFields.Contains(nameof(JudgeLocalModelPath)))
                _judgeLocalModelPath = judge.ServerConfig?.Model?.FilePath;
            if (!_editedFields.Contains(nameof(JudgeHfRepo)))
                _judgeHfRepo = judge.ServerConfig?.Model?.HfRepo;
            if (!_editedFields.Contains(nameof(JudgeHfToken)))
                _judgeHfToken = judge.ServerConfig?.Model?.HfToken;
            if (!_editedFields.Contains(nameof(JudgeApiKey)))
                _judgeApiKey = judge.ServerConfig?.ApiKey;
            if (!_editedFields.Contains(nameof(JudgeServerUrl)))
                _judgeServerUrl = judge.BaseUrl;
            if (!_editedFields.Contains(nameof(SelectedJudgeTemplateIndex)))
                _judgeTemplate = judge.JudgePromptTemplate ?? "standard";
            if (!_editedFields.Contains(nameof(JudgeScoreMin)))
                _judgeScoreMin = (int)judge.ScoreMinValue;
            if (!_editedFields.Contains(nameof(JudgeScoreMax)))
                _judgeScoreMax = (int)judge.ScoreMaxValue;

            // Judge llama-server settings
            if (judge.ServerSettings != null)
            {
                var js = judge.ServerSettings;
                if (!_editedFields.Contains(nameof(JudgeContextWindowTokens)))
                    _judgeContextWindowTokens = js.ContextWindowTokens;
                if (!_editedFields.Contains(nameof(JudgeBatchSizeTokens)))
                    _judgeBatchSizeTokens = js.BatchSizeTokens;
                if (!_editedFields.Contains(nameof(JudgeParallelSlotCount)))
                    _judgeParallelSlotCount = js.ParallelSlotCount;
                if (!_editedFields.Contains(nameof(JudgeGpuLayerCount)))
                    _judgeGpuLayerCount = js.GpuLayerCount;
                if (!_editedFields.Contains(nameof(JudgeSplitMode)))
                    _judgeSplitMode = js.SplitMode;
                if (!_editedFields.Contains(nameof(JudgeKvCacheTypeK)))
                    _judgeKvCacheTypeK = js.KvCacheTypeK;
                if (!_editedFields.Contains(nameof(JudgeKvCacheTypeV)))
                    _judgeKvCacheTypeV = js.KvCacheTypeV;
                if (!_editedFields.Contains(nameof(JudgeEnableFlashAttention)))
                    _judgeEnableFlashAttention = js.EnableFlashAttention;
                if (!_editedFields.Contains(nameof(JudgeSamplingTemperature)))
                    _judgeSamplingTemperature = js.SamplingTemperature;
                if (!_editedFields.Contains(nameof(JudgeTopP)))
                    _judgeTopP = js.TopP;
                if (!_editedFields.Contains(nameof(JudgeTopK)))
                    _judgeTopK = js.TopK;
                if (!_editedFields.Contains(nameof(JudgeMinP)))
                    _judgeMinP = js.MinP;
                if (!_editedFields.Contains(nameof(JudgeRepeatPenalty)))
                    _judgeRepeatPenalty = js.RepeatPenalty;
                if (!_editedFields.Contains(nameof(JudgeSeed)))
                    _judgeSeed = js.Seed;
                if (!_editedFields.Contains(nameof(JudgeThreadCount)))
                    _judgeThreadCount = js.ThreadCount;
                if (!_editedFields.Contains(nameof(JudgeHttpThreadCount)))
                    _judgeHttpThreadCount = js.HttpThreadCount;
                if (!_editedFields.Contains(nameof(JudgeChatTemplate)))
                    _judgeChatTemplate = js.ChatTemplate;
                if (!_editedFields.Contains(nameof(JudgeEnableJinja)))
                    _judgeEnableJinja = js.EnableJinja;
                if (!_editedFields.Contains(nameof(JudgeLogVerbosity)))
                    _judgeLogVerbosity = js.LogVerbosity;
                if (!_editedFields.Contains(nameof(JudgeEnableMlock)))
                    _judgeEnableMlock = js.EnableMlock;
                if (!_editedFields.Contains(nameof(JudgeEnableMmap)))
                    _judgeEnableMmap = js.EnableMmap;
                if (!_editedFields.Contains(nameof(JudgeServerTimeoutSeconds)))
                    _judgeServerTimeoutSeconds = js.ServerTimeoutSeconds;
            }
        }

        // Run settings
        if (config.Run != null)
        {
            var run = config.Run;
            if (!_editedFields.Contains(nameof(RunName)))
                _runName = run.RunName;
            if (!_editedFields.Contains(nameof(OutputDir)))
                _outputDir = run.OutputDirectoryPath ?? "./results";
            if (!_editedFields.Contains(nameof(ShellTarget)))
                _shellTarget = run.ExportShellTarget;
            if (!_editedFields.Contains(nameof(ContinueOnEvalFailure)))
                _continueOnEvalFailure = run.ContinueOnEvalFailure ?? true;
            if (!_editedFields.Contains(nameof(MaxConcurrentEvals)))
                _maxConcurrentEvals = run.MaxConcurrentEvals;
        }

        // Data source settings - read from top-level DataSource config
        if (config.DataSource != null)
        {
            var ds = config.DataSource;
            if (!_editedFields.Contains(nameof(DataFilePath)))
                _dataFilePath = ds.FilePath;
            if (!_editedFields.Contains(nameof(PromptDir)))
                _promptDir = ds.PromptDirectoryPath;
            if (!_editedFields.Contains(nameof(ExpectedDir)))
                _expectedDir = ds.ExpectedOutputDirectoryPath;

            // Data source mode
            var newUseSingleFileDataSource = ds.Kind == DataSourceKind.SingleFile ||
                                             ds.Kind == DataSourceKind.JsonFile ||
                                             ds.Kind == DataSourceKind.JsonlFile ||
                                             ds.Kind == DataSourceKind.YamlFile ||
                                             ds.Kind == DataSourceKind.CsvFile ||
                                             ds.Kind == DataSourceKind.ParquetFile ||
                                             ds.Kind == DataSourceKind.File;
            if (!_editedFields.Contains(nameof(UseSingleFileDataSource)))
            {
                _useSingleFileDataSource = newUseSingleFileDataSource;
            }
        }
        // Fallback to EvalSets[0].DataSource for backwards compatibility
        else if (config.EvalSets.Count > 0)
        {
            var evalSet = config.EvalSets[0];
            if (!_editedFields.Contains(nameof(PipelineName)))
                _pipelineName = evalSet.PipelineName ?? "CasualQA";
            if (!_editedFields.Contains(nameof(DataFilePath)))
                _dataFilePath = evalSet.DataSource.FilePath;
            if (!_editedFields.Contains(nameof(PromptDir)))
                _promptDir = evalSet.DataSource.PromptDirectoryPath;
            if (!_editedFields.Contains(nameof(ExpectedDir)))
                _expectedDir = evalSet.DataSource.ExpectedOutputDirectoryPath;

            // Data source mode
            var newUseSingleFileDataSource = evalSet.DataSource.Kind == DataSourceKind.SingleFile ||
                                             evalSet.DataSource.Kind == DataSourceKind.JsonFile ||
                                             evalSet.DataSource.Kind == DataSourceKind.JsonlFile ||
                                             evalSet.DataSource.Kind == DataSourceKind.YamlFile ||
                                             evalSet.DataSource.Kind == DataSourceKind.CsvFile ||
                                             evalSet.DataSource.Kind == DataSourceKind.ParquetFile ||
                                             evalSet.DataSource.Kind == DataSourceKind.File;
            if (!_editedFields.Contains(nameof(UseSingleFileDataSource)))
            {
                _useSingleFileDataSource = newUseSingleFileDataSource;
            }
        }

        // Notify the Avalonia UI to repaint the bound variables that were just updated in the background
        //TODO: OnPropertyChanged(string.Empty); but that doesn't work for every field
    }

    [System.Text.RegularExpressions.GeneratedRegex("(?<!^)([A-Z])")]
    private static partial System.Text.RegularExpressions.Regex CapitalLetterSplitRegex();
}
