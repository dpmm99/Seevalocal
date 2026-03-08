using Seevalocal.Core.Models;
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
public sealed class WizardViewModel : IWizardViewModel
{
    private readonly IFilePickerService? _filePicker;
    private WizardStepKind _currentStep = WizardStepKind.ContinueRun;
    private string? _checkpointDatabasePath;

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

    public WizardViewModel(IFilePickerService? filePicker = null)
    {
        _filePicker = filePicker;
        GoBackCommand = new RelayCommand(GoBack, () => CanGoBack);
        GoForwardCommand = new RelayCommand(async () => await GoForwardAsync(), () => CanGoForward);
        ExportScriptCommand = new RelayCommand(() => OnExportScript?.Invoke());

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
        if (!CanGoForward) return;

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
    }

    public IReadOnlyList<string> ValidateCurrentStep() => CurrentStep switch
    {
        WizardStepKind.ContinueRun => ValidateContinueRunStep(),
        WizardStepKind.ModelAndServer => ValidateServerStep(),
        WizardStepKind.EvaluationDataset => ValidateDatasetStep(),
        WizardStepKind.Scoring => ValidateScoringStep(),
        _ => []
    };

    private IReadOnlyList<string> ValidateContinueRunStep()
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

    private IReadOnlyList<string> ValidateServerStep()
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

    private IReadOnlyList<string> ValidateDatasetStep()
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

    private IReadOnlyList<string> ValidateScoringStep()
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
                _pipelineName = newName;
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
                // Clear the opposite mode's fields when switching
                if (value)
                {
                    _promptDir = null;
                    _expectedDir = null;
                    OnPropertyChanged(nameof(PromptDir));
                    OnPropertyChanged(nameof(ExpectedDir));
                }
                else
                {
                    _dataFilePath = null;
                    OnPropertyChanged(nameof(DataFilePath));
                }
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
                // Clear the opposite mode's fields when switching
                if (value)
                {
                    _dataFilePath = null;
                    OnPropertyChanged(nameof(DataFilePath));
                }
                else
                {
                    _promptDir = null;
                    _expectedDir = null;
                    OnPropertyChanged(nameof(PromptDir));
                    OnPropertyChanged(nameof(ExpectedDir));
                }
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

    /// <summary>Selected index for the Judge Template ComboBox (0=standard, 1=pass-fail, 2=json).</summary>
    public int SelectedJudgeTemplateIndex
    {
        get => JudgeTemplate switch
        {
            "pass-fail" => 1,
            "json" => 2,
            _ => 0
        };
        set
        {
            var newTemplate = value switch
            {
                1 => "pass-fail",
                2 => "json",
                _ => "standard"
            };
            if (_judgeTemplate != newTemplate)
            {
                _judgeTemplate = newTemplate;
                OnPropertyChanged(nameof(JudgeTemplate));
                OnPropertyChanged(nameof(SelectedJudgeTemplateIndex));
            }
        }
    }

    public double JudgeScoreMin { get => _judgeScoreMin; set => SetField(ref _judgeScoreMin, value); }
    public double JudgeScoreMax { get => _judgeScoreMax; set => SetField(ref _judgeScoreMax, value); }

    // ─── Step 5: Output ───────────────────────────────────────────────────────

    public string? CheckpointDatabasePath { get => _checkpointDatabasePath; set => SetField(ref _checkpointDatabasePath, value); }
    public string OutputDir { get => _outputDir; set => SetField(ref _outputDir, value); }
    public string? RunName { get => _runName; set => SetField(ref _runName, value); }
    public ShellTarget? ShellTarget { get => _shellTarget; set => SetField(ref _shellTarget, value); }
    public bool ContinueFromCheckpoint { get => _continueFromCheckpoint; set => SetField(ref _continueFromCheckpoint, value); }

    // ─── Build config from wizard state ──────────────────────────────────────

    public PartialConfig BuildPartialConfig()
    {
        var model = ManageServer
            ? UseLocalFile && LocalModelPath != null
                ? new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = LocalModelPath }
                : HfRepo != null
                    ? new ModelSource { Kind = ModelSourceKind.HuggingFace, HfRepo = HfRepo, HfToken = HfToken }
                    : null
            : null;

        var server = new PartialServerConfig
        {
            Manage = ManageServer,
            Model = model,
            Host = ManageServer ? Host : null,
            Port = ManageServer ? Port : null,
            ApiKey = ApiKey,
            ExecutablePath = _llamaServerExecutablePath,
            BaseUrl = ManageServer ? null : ServerUrl
        };

        // Build llama-server settings with all fields
        var llamaSettings = BuildLlamaServerSettings();

        // Build judge server settings
        var judge = EnableJudge ? BuildJudgeConfig() : null;

        var dataSource = DataFilePath != null
            ? new DataSourceConfig { Kind = DataSourceKind.SingleFile, FilePath = DataFilePath }
            : new DataSourceConfig { Kind = DataSourceKind.SplitDirectories, PromptDirectoryPath = PromptDir, ExpectedOutputDirectoryPath = ExpectedDir };

        var evalSet = new EvalSetConfig
        {
            PipelineName = PipelineName,
            DataSource = dataSource,
        };

        var run = new PartialRunMeta
        {
            RunName = RunName,
            OutputDirectoryPath = OutputDir,
            ExportShellTarget = ShellTarget,
            ContinueFromCheckpoint = _continueFromCheckpoint,
        };

        var output = new OutputConfig
        {
            ShellTarget = ShellTarget,
            OutputDir = OutputDir,
        };

        return new PartialConfig
        {
            Server = server,
            LlamaServer = llamaSettings,
            EvalSets = [evalSet],
            Judge = judge,
            Run = run,
            Output = output,
        };
    }

    private PartialLlamaServerSettings? BuildLlamaServerSettings()
    {
        var hasAnyValue = ContextWindowTokens != null || BatchSizeTokens != null || UbatchSizeTokens != null ||
            ParallelSlotCount != null || EnableContinuousBatching != null || EnableCachePrompt != null ||
            EnableContextShift != null || GpuLayerCount != null || !string.IsNullOrEmpty(SplitMode) ||
            !string.IsNullOrEmpty(KvCacheTypeK) || !string.IsNullOrEmpty(KvCacheTypeV) ||
            EnableKvOffload != null || EnableFlashAttention != null || SamplingTemperature != null ||
            TopP != null || TopK != null || MinP != null || RepeatPenalty != null ||
            RepeatLastNTokens != null || PresencePenalty != null || FrequencyPenalty != null ||
            Seed != null || ThreadCount != null || HttpThreadCount != null ||
            !string.IsNullOrEmpty(ChatTemplate) || EnableJinja != null ||
            !string.IsNullOrEmpty(ReasoningFormat) || !string.IsNullOrEmpty(ModelAlias) ||
            LogVerbosity != null || EnableMlock != null || EnableMmap != null ||
            ServerTimeoutSeconds != null;

        if (!hasAnyValue) return null;

        return new PartialLlamaServerSettings
        {
            ContextWindowTokens = ContextWindowTokens,
            BatchSizeTokens = BatchSizeTokens,
            UbatchSizeTokens = UbatchSizeTokens,
            ParallelSlotCount = ParallelSlotCount,
            EnableContinuousBatching = EnableContinuousBatching,
            EnableCachePrompt = EnableCachePrompt,
            EnableContextShift = EnableContextShift,
            GpuLayerCount = GpuLayerCount,
            SplitMode = SplitMode,
            KvCacheTypeK = KvCacheTypeK,
            KvCacheTypeV = KvCacheTypeV,
            EnableKvOffload = EnableKvOffload,
            EnableFlashAttention = EnableFlashAttention,
            SamplingTemperature = SamplingTemperature,
            TopP = TopP,
            TopK = TopK,
            MinP = MinP,
            RepeatPenalty = RepeatPenalty,
            RepeatLastNTokens = RepeatLastNTokens,
            PresencePenalty = PresencePenalty,
            FrequencyPenalty = FrequencyPenalty,
            Seed = Seed,
            ThreadCount = ThreadCount,
            HttpThreadCount = HttpThreadCount,
            ChatTemplate = ChatTemplate,
            EnableJinja = EnableJinja,
            ReasoningFormat = ReasoningFormat,
            ModelAlias = ModelAlias,
            LogVerbosity = LogVerbosity,
            EnableMlock = EnableMlock,
            EnableMmap = EnableMmap,
            ServerTimeoutSeconds = ServerTimeoutSeconds
        };
    }

    private PartialJudgeConfig? BuildJudgeConfig()
    {
        var judgeModel = JudgeManageServer
            ? (JudgeUseLocalFile && !string.IsNullOrEmpty(JudgeLocalModelPath)
                ? new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = JudgeLocalModelPath }
                : !string.IsNullOrEmpty(JudgeHfRepo)
                    ? new ModelSource { Kind = ModelSourceKind.HuggingFace, HfRepo = JudgeHfRepo, HfToken = JudgeHfToken }
                    : null)
            : null;

        // Build judge-specific llama-server settings with full feature parity
        var judgeServerSettings = BuildJudgeLlamaServerSettings();

        // Default judge executable path to primary server's executable path if not specified
        var judgeExecutablePath = _judgeExecutablePath ?? _llamaServerExecutablePath;

        return new PartialJudgeConfig
        {
            Manage = JudgeManageServer,
            ServerConfig = new PartialServerConfig
            {
                Manage = JudgeManageServer,
                Model = judgeModel,
                Host = JudgeManageServer ? "127.0.0.1" : null,
                Port = JudgeManageServer ? 8081 : null,
                ApiKey = null,
                ExecutablePath = judgeExecutablePath,
                BaseUrl = JudgeManageServer ? null : JudgeServerUrl
            },
            ServerSettings = judgeServerSettings,
            BaseUrl = JudgeManageServer ? null : JudgeServerUrl,
            JudgePromptTemplate = JudgeTemplate,
            ScoreMinValue = JudgeScoreMin,
            ScoreMaxValue = JudgeScoreMax
        };
    }

    private PartialLlamaServerSettings? BuildJudgeLlamaServerSettings()
    {
        var hasAnyValue = JudgeContextWindowTokens != null || JudgeBatchSizeTokens != null ||
            JudgeParallelSlotCount != null || JudgeSplitMode != null ||
            !string.IsNullOrEmpty(JudgeKvCacheTypeK) || !string.IsNullOrEmpty(JudgeKvCacheTypeV) ||
            JudgeEnableFlashAttention != null || JudgeSamplingTemperature != null ||
            JudgeTopP != null || JudgeTopK != null || JudgeMinP != null ||
            JudgeRepeatPenalty != null || JudgeSeed != null ||
            JudgeThreadCount != null || JudgeHttpThreadCount != null ||
            !string.IsNullOrEmpty(JudgeChatTemplate) || JudgeEnableJinja != null ||
            JudgeLogVerbosity != null || JudgeEnableMlock != null ||
            JudgeEnableMmap != null || JudgeServerTimeoutSeconds != null;

        if (!hasAnyValue) return null;

        return new PartialLlamaServerSettings
        {
            ContextWindowTokens = JudgeContextWindowTokens,
            BatchSizeTokens = JudgeBatchSizeTokens,
            ParallelSlotCount = JudgeParallelSlotCount,
            SplitMode = JudgeSplitMode,
            KvCacheTypeK = JudgeKvCacheTypeK,
            KvCacheTypeV = JudgeKvCacheTypeV,
            EnableFlashAttention = JudgeEnableFlashAttention,
            SamplingTemperature = JudgeSamplingTemperature,
            TopP = JudgeTopP,
            TopK = JudgeTopK,
            MinP = JudgeMinP,
            RepeatPenalty = JudgeRepeatPenalty,
            Seed = JudgeSeed,
            ThreadCount = JudgeThreadCount,
            HttpThreadCount = JudgeHttpThreadCount,
            ChatTemplate = JudgeChatTemplate,
            EnableJinja = JudgeEnableJinja,
            LogVerbosity = JudgeLogVerbosity,
            EnableMlock = JudgeEnableMlock,
            EnableMmap = JudgeEnableMmap,
            ServerTimeoutSeconds = JudgeServerTimeoutSeconds
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
                "Executables|llama-server*;*.exe|All Files|*.*");
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
                "Executables|llama-server*;*.exe|All Files|*.*");
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

        // Output
        _outputDir = "./results";
        _runName = null;
        // Reset shell target to OS default
        _shellTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Core.Models.ShellTarget.PowerShell
            : Core.Models.ShellTarget.Bash;
        _continueFromCheckpoint = false;

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
        OnPropertyChanged(nameof(OutputDir));
        OnPropertyChanged(nameof(RunName));
        OnPropertyChanged(nameof(ShellTarget));
        OnPropertyChanged(nameof(CheckpointDatabasePath));
        OnPropertyChanged(nameof(ContinueFromCheckpoint));
    }

    // ─── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private bool SetField<T>(ref T f, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(f, v)) return false;
        f = v; OnPropertyChanged(n);

        // Navigation validity depends on multiple properties (current step and some fields).
        // Ensure CanGoBack/CanGoForward are re-evaluated and commands get updated whenever any bound property changes.
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));

        if (GoBackCommand is RelayCommand backCmd) backCmd.NotifyCanExecuteChanged();
        if (GoForwardCommand is RelayCommand fwdCmd) fwdCmd.NotifyCanExecuteChanged();

        return true;
    }
}
