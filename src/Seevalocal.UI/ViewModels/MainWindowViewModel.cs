using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.UI.Commands;
using Seevalocal.UI.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Seevalocal.UI.ViewModels;

public enum AppView { Wizard, RunDashboard, Results, Settings }

/// <summary>
/// Top-level ViewModel for the Avalonia MainWindow.
/// Manages navigation, active run, and settings stack.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IRunnerService _runnerService;
    private readonly IShellScriptExporter _scriptExporter;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IFilePickerService _filePicker;
    private readonly IToastService _toastService;

    private AppView _currentView = AppView.Wizard;
    private string _titleBarText = "Seevalocal";
    private bool _isModified;
    private IEvalRunViewModel? _activeRun;

    // ─── Navigation ───────────────────────────────────────────────────────────

    public AppView CurrentView
    {
        get => _currentView;
        set => SetField(ref _currentView, value);
    }

    public string TitleBarText
    {
        get => _titleBarText;
        private set => SetField(ref _titleBarText, value);
    }

    public bool IsModified
    {
        get => _isModified;
        private set
        {
            _ = SetField(ref _isModified, value);
            UpdateTitle();
        }
    }

    // ─── Settings stack ───────────────────────────────────────────────────────

    public ObservableCollection<SettingsLayerViewModel> SettingsLayers { get; } = [];

    /// <summary>Session overrides (CLI-style values entered in the UI).</summary>
    public IWizardViewModel WizardState { get; }

    /// <summary>View model for the Settings view.</summary>
    public SettingsViewModel SettingsViewModel { get; }

    /// <summary>
    /// Gets the collection of active toast notifications for display in the UI.
    /// </summary>
    public ObservableCollection<ToastMessage> Toasts =>
        (_toastService as ToastService)?.Toasts ?? [];

    // ─── Active run ───────────────────────────────────────────────────────────

    public IEvalRunViewModel? ActiveRun
    {
        get => _activeRun;
        private set => SetField(ref _activeRun, value);
    }

    // ─── Navigation Commands ──────────────────────────────────────────────────

    public ICommand NavigateToWizardCommand { get; }
    public ICommand NavigateToDashboardCommand { get; }
    public ICommand NavigateToResultsCommand { get; }
    public ICommand NavigateToSettingsCommand { get; }
    public ICommand AddSettingsFileCommand { get; }

    // ─── Settings View Commands ───────────────────────────────────────────────

    public ICommand ResetSettingsToDefaultsCommand { get; }
    public ICommand LoadSettingsFileCommand { get; }
    public ICommand SaveSettingsFileCommand { get; }
    public ICommand SaveMaterializedSettingsFileCommand { get; }

    // ─── Results View Commands ────────────────────────────────────────────────

    public ICommand LoadResultsFileCommand { get; }
    public ICommand ExportResultsJsonCommand { get; }
    public ICommand ExportResultsShellScriptCommand { get; }
    public ICommand OpenResultsFolderCommand { get; }
    public ICommand CopyTextCommand { get; }

    // ─── Constructor ─────────────────────────────────────────────────────────

    public MainWindowViewModel(
        IConfigurationService configService,
        IRunnerService runnerService,
        IShellScriptExporter scriptExporter,
        ILogger<MainWindowViewModel> logger,
        IFilePickerService filePicker,
        IWizardViewModel wizardState,
        IToastService toastService)
    {
        _configService = configService;
        _runnerService = runnerService;
        _scriptExporter = scriptExporter;
        _logger = logger;
        _filePicker = filePicker;
        _toastService = toastService;
        WizardState = wizardState;
        SettingsViewModel = new SettingsViewModel();

        // Subscribe to settings layer changes to update the SettingsViewModel
        SettingsLayers.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                foreach (SettingsLayerViewModel newItem in e.NewItems!)
                {
                    newItem.PropertyChanged += (_, _) => RecalculateSettingsViewModel();
                }
            }
            RecalculateSettingsViewModel();
        };

        // Subscribe to browse requests from SettingsViewModel
        SettingsViewModel.BrowseRequested += async (_, args) => await HandleBrowseRequestAsync(args);

        // Subscribe to settings field value changes to sync them immediately
        foreach (var field in SettingsViewModel.SettingsFields)
        {
            field.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SettingsFieldViewModel.Value) && s is SettingsFieldViewModel vm)
                {
                    UpdateWizardStateFromSettingsField(vm);
                    IsModified = true;
                }
            };
        }

        // Subscribe to wizard step changes to sync settings to unedited fields
        WizardState.StepChanged += (_, _) =>
        {
            var config = ResolveCurrentConfig();
            if (config.IsSuccess)
            {
                WizardState.SyncDefaultsFromSettings(config.Value, SettingsViewModel);
            }
        };

        // Subscribe to wizard reset completion to sync settings after reset
        WizardState.ResetToDefaultsCompleted += (_, _) =>
        {
            var config = ResolveCurrentConfig();
            if (config.IsSuccess)
            {
                WizardState.SyncDefaultsFromSettings(config.Value, SettingsViewModel);
            }
        };

        NavigateToWizardCommand = new RelayCommand(() =>
        {
            // Sync defaults from loaded settings when navigating to wizard
            var config = ResolveCurrentConfig();
            if (config.IsSuccess)
            {
                WizardState.SyncDefaultsFromSettings(config.Value, SettingsViewModel);
            }
            CurrentView = AppView.Wizard;
        });
        NavigateToDashboardCommand = new RelayCommand(() => CurrentView = AppView.RunDashboard);
        NavigateToResultsCommand = new RelayCommand(() => CurrentView = AppView.Results);
        NavigateToSettingsCommand = new RelayCommand(() => CurrentView = AppView.Settings);
        AddSettingsFileCommand = new RelayCommand(async () => await AddSettingsFileAsync());

        // Settings view commands
        ResetSettingsToDefaultsCommand = new RelayCommand(ResetSettingsToDefaults);
        LoadSettingsFileCommand = new RelayCommand(async () => await AddSettingsFileAsync());
        SaveSettingsFileCommand = new RelayCommand(async () => await SaveSettingsFileAsync());
        SaveMaterializedSettingsFileCommand = new RelayCommand(async () => await SaveMaterializedSettingsFileAsync());

        // Results view commands
        LoadResultsFileCommand = new RelayCommand(async () => await LoadResultsFileAsync());
        ExportResultsJsonCommand = new RelayCommand(async () => await ExportResultsToJsonAsync());
        ExportResultsShellScriptCommand = new RelayCommand(async () => await ExportResultsToShellScriptAsync());
        OpenResultsFolderCommand = new RelayCommand(OpenResultsFolder);
        CopyTextCommand = new RelayCommand<string>(CopyText);

        WizardState.PropertyChanged += (_, _) => IsModified = true;

        _ = LoadDefaultSettingsAsync();
    }

    // ─── Settings file management ─────────────────────────────────────────────

    public async Task AddSettingsFileAsync(string? filePath = null)
    {
        if (filePath == null)
        {
            filePath = await _filePicker.ShowOpenFileDialogAsync(
                "Select Settings File",
                "Settings Files|*.yml;*.yaml;*.json;*.toml|All Files|*.*");

            if (filePath == null) return;
        }

        var result = await _configService.LoadPartialConfigAsync(filePath, default);
        if (result.IsFailed)
        {
            _logger.LogError("Failed to load settings file {Path}: {Error}",
                filePath, result.Errors[0].Message);
            return;
        }

        SettingsLayers.Add(new SettingsLayerViewModel(filePath, SettingsLayers.Count, result.Value));
        RecalculateSettingsViewModel();
        IsModified = true;

        // Sync wizard defaults from the newly loaded settings
        var config = ResolveCurrentConfig();
        if (config.IsSuccess)
        {
            WizardState.SyncDefaultsFromSettings(config.Value);
        }

        _logger.LogInformation("Loaded settings file {Path}", filePath);
    }

    /// <summary>Recalculates the SettingsViewModel based on current settings layers.</summary>
    private void RecalculateSettingsViewModel()
    {
        SettingsViewModel.ClearConfigLayers();

        // Add each enabled layer in order (ONLY settings files, NOT wizard state)
        // The Settings view should be independent of the wizard
        foreach (var layer in SettingsLayers.OrderBy(l => l.LayerIndex))
        {
            if (layer.IsEnabled)
            {
                SettingsViewModel.AddConfigLayer(layer.DisplayName, layer.Config);
            }
        }
    }

    private async Task HandleBrowseRequestAsync(SettingsViewModel.BrowseEventArgs args)
    {
        string? path = null;

        if (args.IsFolder)
        {
            path = await _filePicker.ShowOpenFolderDialogAsync("Select Folder", args.InitialPath);
        }
        else
        {
            path = await _filePicker.ShowOpenFileDialogAsync("Select File", args.Filter ?? "All Files|*.*", args.InitialPath);
        }

        if (path != null)
        {
            // Find the field and update its value
            var field = SettingsViewModel.SettingsFields.FirstOrDefault(f => f.Key == args.FieldKey);
            if (field != null)
            {
                field.Value = path;
            }
        }
    }

    private void UpdateWizardStateFromSettingsField(SettingsFieldViewModel field)
    {
        // Update the corresponding property in WizardState based on the field key
        if (WizardState is not WizardViewModel state) return;

        // Use MaterializedValue (which includes values from all layers) instead of Value (user edits only)
        var materializedValue = field.MaterializedValue;

        switch (field.Key)
        {
            // Server settings
            case "server.manage":
                state.ManageServer = materializedValue?.ToLowerInvariant() == "true";
                break;
            case "server.executablePath":
                state.LlamaServerExecutablePath = materializedValue;
                break;
            case "server.host":
                state.Host = materializedValue ?? "127.0.0.1";
                break;
            case "server.port":
                state.Port = int.TryParse(materializedValue, out var port) ? port : 8080;
                break;
            case "server.apiKey":
                state.ApiKey = materializedValue;
                break;
            case "server.baseUrl":
                state.ServerUrl = materializedValue;
                break;

            // Llama server settings
            case "llama.contextWindowTokens":
                state.ContextWindowTokens = int.TryParse(materializedValue, out var context) ? context : null;
                break;
            case "llama.batchSizeTokens":
                state.BatchSizeTokens = int.TryParse(materializedValue, out var batch) ? batch : null;
                break;
            case "llama.ubatchSizeTokens":
                state.UbatchSizeTokens = int.TryParse(materializedValue, out var ubatch) ? ubatch : null;
                break;
            case "llama.parallelSlotCount":
                state.ParallelSlotCount = int.TryParse(materializedValue, out var parallel) ? parallel : null;
                break;
            case "llama.enableContinuousBatching":
                state.EnableContinuousBatching = ParseBool(materializedValue);
                break;
            case "llama.enableCachePrompt":
                state.EnableCachePrompt = ParseBool(materializedValue);
                break;
            case "llama.enableContextShift":
                state.EnableContextShift = ParseBool(materializedValue);
                break;
            case "llama.gpuLayerCount":
                state.GpuLayerCount = int.TryParse(materializedValue, out var gpuLayers) ? gpuLayers : null;
                break;
            case "llama.splitMode":
                state.SplitMode = materializedValue == "Unspecified" ? null : materializedValue;
                break;
            case "llama.kvCacheTypeK":
                state.KvCacheTypeK = materializedValue;
                break;
            case "llama.kvCacheTypeV":
                state.KvCacheTypeV = materializedValue;
                break;
            case "llama.enableKvOffload":
                state.EnableKvOffload = ParseBool(materializedValue);
                break;
            case "llama.enableFlashAttention":
                state.EnableFlashAttention = ParseBool(materializedValue);
                break;
            case "llama.samplingTemperature":
                state.SamplingTemperature = double.TryParse(materializedValue, out var temp) ? temp : null;
                break;
            case "llama.topP":
                state.TopP = double.TryParse(materializedValue, out var topP) ? topP : null;
                break;
            case "llama.topK":
                state.TopK = int.TryParse(materializedValue, out var topK) ? topK : null;
                break;
            case "llama.minP":
                state.MinP = double.TryParse(materializedValue, out var minP) ? minP : null;
                break;
            case "llama.repeatPenalty":
                state.RepeatPenalty = double.TryParse(materializedValue, out var penalty) ? penalty : null;
                break;
            case "llama.repeatLastNTokens":
                state.RepeatLastNTokens = int.TryParse(materializedValue, out var repeatN) ? repeatN : null;
                break;
            case "llama.presencePenalty":
                state.PresencePenalty = double.TryParse(materializedValue, out var presence) ? presence : null;
                break;
            case "llama.frequencyPenalty":
                state.FrequencyPenalty = double.TryParse(materializedValue, out var frequency) ? frequency : null;
                break;
            case "llama.seed":
                state.Seed = int.TryParse(materializedValue, out var seed) ? seed : null;
                break;
            case "llama.threadCount":
                state.ThreadCount = int.TryParse(materializedValue, out var threads) ? threads : null;
                break;
            case "llama.httpThreadCount":
                state.HttpThreadCount = int.TryParse(materializedValue, out var httpThreads) ? httpThreads : null;
                break;
            case "llama.chatTemplate":
                state.ChatTemplate = materializedValue;
                break;
            case "llama.enableJinja":
                state.EnableJinja = ParseBool(materializedValue);
                break;
            case "llama.reasoningFormat":
                state.ReasoningFormat = materializedValue == "Unspecified" ? null : materializedValue;
                break;
            case "llama.modelAlias":
                state.ModelAlias = materializedValue;
                break;
            case "llama.logVerbosity":
                state.LogVerbosity = int.TryParse(materializedValue, out var verbosity) ? verbosity : null;
                break;
            case "llama.enableMlock":
                state.EnableMlock = ParseBool(materializedValue);
                break;
            case "llama.enableMmap":
                state.EnableMmap = ParseBool(materializedValue);
                break;
            case "llama.serverTimeoutSeconds":
                state.ServerTimeoutSeconds = double.TryParse(materializedValue, out var timeout) ? timeout : null;
                break;

            // Judge settings
            case "judge.manage":
                state.JudgeManageServer = materializedValue?.ToLowerInvariant() == "true";
                break;
            case "judge.executablePath":
                state.JudgeExecutablePath = materializedValue;
                break;
            case "judge.baseUrl":
                state.JudgeServerUrl = materializedValue;
                break;
            case "judge.modelFile":
                state.JudgeLocalModelPath = materializedValue;
                break;
            case "judge.hfRepo":
                state.JudgeHfRepo = materializedValue;
                break;
            case "judge.apiKey":
                state.JudgeApiKey = materializedValue;
                break;
            case "judge.template":
                state.JudgeTemplate = materializedValue == "Unspecified" ? "standard" : (materializedValue ?? "standard");
                break;
            case "judge.scoreMin":
                state.JudgeScoreMin = double.TryParse(materializedValue, out var min) ? min : 0;
                break;
            case "judge.scoreMax":
                state.JudgeScoreMax = double.TryParse(materializedValue, out var max) ? max : 10;
                break;

            // Judge llama-server settings
            case "judge.contextWindowTokens":
                state.JudgeContextWindowTokens = int.TryParse(materializedValue, out var jContext) ? jContext : null;
                break;
            case "judge.batchSizeTokens":
                state.JudgeBatchSizeTokens = int.TryParse(materializedValue, out var jBatch) ? jBatch : null;
                break;
            case "judge.parallelSlotCount":
                state.JudgeParallelSlotCount = int.TryParse(materializedValue, out var jParallel) ? jParallel : null;
                break;
            case "judge.gpuLayerCount":
                state.JudgeGpuLayerCount = int.TryParse(materializedValue, out var jGpuLayers) ? jGpuLayers : null;
                break;
            case "judge.splitMode":
                state.JudgeSplitMode = materializedValue == "Unspecified" ? null : materializedValue;
                break;
            case "judge.kvCacheTypeK":
                state.JudgeKvCacheTypeK = materializedValue;
                break;
            case "judge.kvCacheTypeV":
                state.JudgeKvCacheTypeV = materializedValue;
                break;
            case "judge.enableFlashAttention":
                state.JudgeEnableFlashAttention = ParseBool(materializedValue);
                break;
            case "judge.samplingTemperature":
                state.JudgeSamplingTemperature = double.TryParse(materializedValue, out var jTemp) ? jTemp : null;
                break;
            case "judge.topP":
                state.JudgeTopP = double.TryParse(materializedValue, out var jTopP) ? jTopP : null;
                break;
            case "judge.topK":
                state.JudgeTopK = int.TryParse(materializedValue, out var jTopK) ? jTopK : null;
                break;
            case "judge.minP":
                state.JudgeMinP = double.TryParse(materializedValue, out var jMinP) ? jMinP : null;
                break;
            case "judge.repeatPenalty":
                state.JudgeRepeatPenalty = double.TryParse(materializedValue, out var jPenalty) ? jPenalty : null;
                break;
            case "judge.seed":
                state.JudgeSeed = int.TryParse(materializedValue, out var jSeed) ? jSeed : null;
                break;
            case "judge.threadCount":
                state.JudgeThreadCount = int.TryParse(materializedValue, out var jThreads) ? jThreads : null;
                break;
            case "judge.httpThreadCount":
                state.JudgeHttpThreadCount = int.TryParse(materializedValue, out var jHttpThreads) ? jHttpThreads : null;
                break;
            case "judge.chatTemplate":
                state.JudgeChatTemplate = materializedValue;
                break;
            case "judge.enableJinja":
                state.JudgeEnableJinja = ParseBool(materializedValue);
                break;
            case "judge.logVerbosity":
                state.JudgeLogVerbosity = int.TryParse(materializedValue, out var jLog) ? jLog : null;
                break;
            case "judge.enableMlock":
                state.JudgeEnableMlock = ParseBool(materializedValue);
                break;
            case "judge.enableMmap":
                state.JudgeEnableMmap = ParseBool(materializedValue);
                break;
            case "judge.serverTimeoutSeconds":
                state.JudgeServerTimeoutSeconds = double.TryParse(materializedValue, out var jTimeout) ? jTimeout : null;
                break;

            // Output settings
            case "output.writePerEvalJson":
                state.WritePerEvalJson = materializedValue?.ToLowerInvariant() == "true";
                break;
            case "output.writeSummaryJson":
                state.WriteSummaryJson = materializedValue?.ToLowerInvariant() == "true";
                break;
            case "output.writeSummaryCsv":
                state.WriteSummaryCsv = materializedValue?.ToLowerInvariant() == "true";
                break;
            case "output.writeParquet":
                state.WriteResultsParquet = materializedValue?.ToLowerInvariant() == "true";
                break;
            case "output.includeRawResponse":
                state.IncludeRawLlmResponse = materializedValue?.ToLowerInvariant() == "true";
                break;

            // Run settings
            case "run.name":
                state.RunName = materializedValue;
                break;
            case "run.outputDirectoryPath":
                state.OutputDir = materializedValue ?? "";
                break;
            case "run.exportShellTarget":
                state.ShellTarget = materializedValue == "Unspecified" ? null : ParseShellTarget(materializedValue);
                break;
            case "run.continueOnEvalFailure":
                state.ContinueOnEvalFailure = materializedValue?.ToLowerInvariant() == "true";
                break;
            case "run.maxConcurrentEvals":
                state.MaxConcurrentEvals = int.TryParse(materializedValue, out var maxConcurrent) ? maxConcurrent : null;
                break;
            case "run.dataFilePath":
                state.DataFilePath = materializedValue;
                break;
            case "run.promptDirectoryPath":
                state.PromptDir = materializedValue;
                break;
            case "run.expectedDirectoryPath":
                state.ExpectedDir = materializedValue;
                break;
        }

        static bool? ParseBool(string? value) => value?.ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => null
        };

        static ShellTarget? ParseShellTarget(string? value) => value?.ToLowerInvariant() switch
        {
            "bash" => ShellTarget.Bash,
            "powershell" => ShellTarget.PowerShell,
            _ => null
        };
    }

    /// <summary>
    /// Syncs all SettingsViewModel field values to WizardViewModel.
    /// </summary>
    private void SyncAllSettingsFieldsToWizard()
    {
        foreach (var field in SettingsViewModel.SettingsFields)
        {
            UpdateWizardStateFromSettingsField(field);
        }

        // If continuing from checkpoint, load the original EvalSetId from the checkpoint database
        if (WizardState is WizardViewModel wizardVm &&
            wizardVm.ContinueFromCheckpoint &&
            !string.IsNullOrEmpty(wizardVm.CheckpointDatabasePath) &&
            File.Exists(wizardVm.CheckpointDatabasePath))
        {
            LoadCheckpointEvalSetId(wizardVm.CheckpointDatabasePath);
        }
    }

    /// <summary>
    /// Loads the original EvalSetId from a checkpoint database.
    /// </summary>
    private void LoadCheckpointEvalSetId(string dbPath)
    {
        try
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT EvalSetId FROM EvalResults LIMIT 1";
            var result = cmd.ExecuteScalar();

            if (result is string evalSetId && !string.IsNullOrEmpty(evalSetId))
            {
                // Use reflection to set the private _checkpointEvalSetId field
                var field = typeof(WizardViewModel).GetField("_checkpointEvalSetId",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(WizardState, evalSetId);

                _logger.LogInformation("Loaded checkpoint EvalSetId: {EvalSetId}", evalSetId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load EvalSetId from checkpoint database {DbPath}", dbPath);
        }
    }

    public void RemoveSettingsLayer(SettingsLayerViewModel layer)
    {
        _ = SettingsLayers.Remove(layer);
        IsModified = true;
    }

    /// <summary>Resolves the current settings stack + wizard state into a <see cref="ResolvedConfig"/>.</summary>
    public Result<ResolvedConfig> ResolveCurrentConfig()
    {
        var partials = SettingsLayers
            .Where(static l => l.IsEnabled)
            .OrderBy(static l => l.LayerIndex)
            .Select(static l => l.Config)
            .ToList();

        // Add Settings view field values as session overrides (higher priority than loaded files)
        partials.Add(SettingsViewModel.BuildPartialConfigFromFields());

        // Add wizard state as highest priority
        partials.Add(WizardState.BuildPartialConfig());
        return _configService.Resolve(partials);
    }

    public async Task SaveCurrentConfigAsync(string path)
    {
        // Sync all settings fields from SettingsViewModel to WizardViewModel
        SyncAllSettingsFieldsToWizard();

        var resolveResult = ResolveCurrentConfig();
        if (resolveResult.IsFailed) return;

        // Delegate to Config project serializer
        await File.WriteAllTextAsync(path, SerializeToYaml(WizardState.BuildPartialConfig()));
        IsModified = false;
        UpdateTitle();
    }

    // ─── Run management ───────────────────────────────────────────────────────

    public async Task StartRunAsync()
    {
        var resolveResult = ResolveCurrentConfig();
        if (resolveResult.IsFailed)
        {
            _toastService.ShowError($"Configuration error: {resolveResult.Errors[0].Message}");
            _logger.LogError("Cannot start run: config resolution failed");
            return;
        }

        var config = resolveResult.Value;
        var errors = _configService.Validate(config);
        if (errors.Count > 0)
        {
            // Show all validation errors in a toast
            var errorMessage = errors.Count == 1
                ? errors[0].MessageText
                : $"{errors.Count} configuration errors:\n• " + string.Join("\n• ", errors.Select(e => e.MessageText));
            _toastService.ShowError(errorMessage, 8000);
            _logger.LogWarning("Validation errors: {Count}", errors.Count);
            return;
        }

        // Switch to Run Dashboard BEFORE creating the VM so users can see llama-server loading progress
        CurrentView = AppView.RunDashboard;

        // Create the VM (this prepares everything but doesn't start the server yet)
        ActiveRun = await _runnerService.CreateViewModelAsync(config);

        // Start the run in the background so the UI can update
        _ = RunInBackgroundAsync();
    }

    private async Task RunInBackgroundAsync()
    {
        try
        {
            if (ActiveRun != null)
            {
                await ActiveRun.StartAsync();

                // Run completed - switch to results
                CurrentView = AppView.Results;
            }
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Error during run: {ex.Message}", 8000);
            _logger.LogError(ex, "Error during run");
        }
    }

    // ─── Script export ────────────────────────────────────────────────────────

    public string ExportScript(ShellTarget target)
    {
        // Sync all settings fields from SettingsViewModel to WizardViewModel
        SyncAllSettingsFieldsToWizard();

        var result = ResolveCurrentConfig();
        return result.IsFailed ? $"# Error: {result.Errors[0].Message}" : _scriptExporter.Export(result.Value, target);
    }

    // ─── Settings View Methods ────────────────────────────────────────────────

    private void ResetSettingsToDefaults()
    {
        SettingsLayers.Clear();
        WizardState.ResetToDefaults();
        IsModified = true;
        _logger.LogInformation("Settings reset to defaults");
    }

    private async Task SaveSettingsFileAsync()
    {
        var path = await _filePicker.ShowSaveFileDialogAsync(
            "Save Settings File",
            "YAML Files|*.yml;*.yaml|JSON Files|*.json|All Files|*.*");

        if (string.IsNullOrEmpty(path)) return;

        // Build PartialConfig from SettingsViewModel fields ONLY (not wizard state)
        var partialConfig = BuildPartialConfigFromSettingsFields();

        await File.WriteAllTextAsync(path, SerializeToYaml(partialConfig));
        IsModified = false;
        UpdateTitle();
        _logger.LogInformation("Settings saved to {Path}", path);
    }

    private async Task SaveMaterializedSettingsFileAsync()
    {
        var path = await _filePicker.ShowSaveFileDialogAsync(
            "Save Materialized Settings File",
            "YAML Files|*.yml;*.yaml|JSON Files|*.json|All Files|*.*");

        if (string.IsNullOrEmpty(path)) return;

        // Build PartialConfig from materialized values (merged config layers)
        var partialConfig = BuildPartialConfigFromMaterializedValues();

        await File.WriteAllTextAsync(path, SerializeToYaml(partialConfig));
        _logger.LogInformation("Materialized settings saved to {Path}", path);
    }

    /// <summary>
    /// Builds a PartialConfig from the materialized values shown in the Settings UI.
    /// This includes values from all loaded config layers, not just user edits.
    /// </summary>
    private PartialConfig BuildPartialConfigFromMaterializedValues()
    {
        // Helper to get materialized field value - empty strings are treated as null
        string? F(string key)
        {
            var val = SettingsViewModel.SettingsFields.FirstOrDefault(f => f.Key == key)?.MaterializedValue;
            return string.IsNullOrEmpty(val) ? null : val;
        }
        bool? Fb(string key) => bool.TryParse(F(key), out var v) ? v : null;
        int? Fi(string key) => int.TryParse(F(key), out var v) ? v : null;
        double? Fd(string key) => double.TryParse(F(key), out var v) ? v : null;

        var llamaServerSettings = new PartialLlamaServerSettings
        {
            ContextWindowTokens = Fi("llama.contextWindowTokens"),
            BatchSizeTokens = Fi("llama.batchSizeTokens"),
            UbatchSizeTokens = Fi("llama.ubatchSizeTokens"),
            ParallelSlotCount = Fi("llama.parallelSlotCount"),
            EnableContinuousBatching = Fb("llama.enableContinuousBatching"),
            EnableCachePrompt = Fb("llama.enableCachePrompt"),
            EnableContextShift = Fb("llama.enableContextShift"),
            GpuLayerCount = Fi("llama.gpuLayerCount"),
            SplitMode = F("llama.splitMode") is var sm && sm != "Unspecified" ? sm : null,
            KvCacheTypeK = F("llama.kvCacheTypeK"),
            KvCacheTypeV = F("llama.kvCacheTypeV"),
            EnableKvOffload = Fb("llama.enableKvOffload"),
            EnableFlashAttention = Fb("llama.enableFlashAttention"),
            SamplingTemperature = Fd("llama.samplingTemperature"),
            TopP = Fd("llama.topP"),
            TopK = Fi("llama.topK"),
            MinP = Fd("llama.minP"),
            RepeatPenalty = Fd("llama.repeatPenalty"),
            RepeatLastNTokens = Fi("llama.repeatLastNTokens"),
            PresencePenalty = Fd("llama.presencePenalty"),
            FrequencyPenalty = Fd("llama.frequencyPenalty"),
            Seed = Fi("llama.seed"),
            ThreadCount = Fi("llama.threadCount"),
            HttpThreadCount = Fi("llama.httpThreadCount"),
            ChatTemplate = F("llama.chatTemplate"),
            EnableJinja = Fb("llama.enableJinja"),
            ReasoningFormat = F("llama.reasoningFormat") is var rf && rf != "Unspecified" ? rf : null,
            ModelAlias = F("llama.modelAlias"),
            LogVerbosity = Fi("llama.logVerbosity"),
            EnableMlock = Fb("llama.enableMlock"),
            EnableMmap = Fb("llama.enableMmap"),
            ServerTimeoutSeconds = Fd("llama.serverTimeoutSeconds"),
        };

        var judgeServerSettings = new PartialLlamaServerSettings
        {
            ContextWindowTokens = Fi("judge.contextWindowTokens"),
            BatchSizeTokens = Fi("judge.batchSizeTokens"),
            UbatchSizeTokens = Fi("judge.ubatchSizeTokens"),
            ParallelSlotCount = Fi("judge.parallelSlotCount"),
            EnableContinuousBatching = Fb("judge.enableContinuousBatching"),
            EnableCachePrompt = Fb("judge.enableCachePrompt"),
            EnableContextShift = Fb("judge.enableContextShift"),
            GpuLayerCount = Fi("judge.gpuLayerCount"),
            SplitMode = F("judge.splitMode") is var jsm && jsm != "Unspecified" ? jsm : null,
            KvCacheTypeK = F("judge.kvCacheTypeK"),
            KvCacheTypeV = F("judge.kvCacheTypeV"),
            EnableKvOffload = Fb("judge.enableKvOffload"),
            EnableFlashAttention = Fb("judge.enableFlashAttention"),
            SamplingTemperature = Fd("judge.samplingTemperature"),
            TopP = Fd("judge.topP"),
            TopK = Fi("judge.topK"),
            MinP = Fd("judge.minP"),
            RepeatPenalty = Fd("judge.repeatPenalty"),
            RepeatLastNTokens = Fi("judge.repeatLastNTokens"),
            PresencePenalty = Fd("judge.presencePenalty"),
            FrequencyPenalty = Fd("judge.frequencyPenalty"),
            Seed = Fi("judge.seed"),
            ThreadCount = Fi("judge.threadCount"),
            HttpThreadCount = Fi("judge.httpThreadCount"),
            ChatTemplate = F("judge.chatTemplate"),
            EnableJinja = Fb("judge.enableJinja"),
            ReasoningFormat = F("judge.reasoningFormat") is var jrf && jrf != "Unspecified" ? jrf : null,
            ModelAlias = F("judge.modelAlias"),
            LogVerbosity = Fi("judge.logVerbosity"),
            EnableMlock = Fb("judge.enableMlock"),
            EnableMmap = Fb("judge.enableMmap"),
            ServerTimeoutSeconds = Fd("judge.serverTimeoutSeconds"),
        };

        // Build Judge config - always include, even if not enabled
        var judge = new PartialJudgeConfig
        {
            Enable = Fb("judge.enable"),
            ServerConfig = new PartialServerConfig
            {
                Manage = Fb("judge.manage"),
                ExecutablePath = F("judge.executablePath"),
                Host = F("judge.host"),
                Port = Fi("judge.port"),
                ApiKey = F("judge.apiKey"),
                BaseUrl = F("judge.baseUrl"),
                Model = new ModelSource
                {
                    FilePath = F("judge.modelFile"),
                    HfRepo = F("judge.hfRepo")
                }
            },
            ServerSettings = judgeServerSettings,
            JudgePromptTemplate = F("judge.template"),
            ScoreMinValue = Fd("judge.scoreMin") ?? 0,
            ScoreMaxValue = Fd("judge.scoreMax") ?? 10,
        };

        // Build DataSource config
        var dataSourceKindStr = F("dataSource.kind");
        DataSourceKind? dataSourceKind = dataSourceKindStr?.ToLowerInvariant() switch
        {
            "singlefile" => DataSourceKind.SingleFile,
            "jsonlfile" => DataSourceKind.JsonlFile,
            "splitdirectories" => DataSourceKind.SplitDirectories,
            "directory" => DataSourceKind.Directory,
            _ => null
        };

        var dataSource = new PartialDataSourceConfig
        {
            Kind = dataSourceKind,
            FilePath = F("dataSource.filePath"),
            PromptDirectoryPath = F("dataSource.promptDirectory"),
            ExpectedOutputDirectoryPath = F("dataSource.expectedDirectory"),
        };

        return new PartialConfig
        {
            Server = new PartialServerConfig
            {
                Manage = Fb("server.manage"),
                ExecutablePath = F("server.executablePath"),
                Host = F("server.host"),
                Port = Fi("server.port"),
                ApiKey = F("server.apiKey"),
                BaseUrl = F("server.baseUrl"),
            },
            LlamaServer = llamaServerSettings,
            Judge = judge,
            Run = new PartialRunMeta
            {
                RunName = F("run.name"),
                OutputDirectoryPath = F("run.outputDirectoryPath"),
                ExportShellTarget = F("run.exportShellTarget") is var st && st != "Unspecified" ? ParseShellTarget(st) : null,
                ContinueOnEvalFailure = Fb("run.continueOnEvalFailure"),
                MaxConcurrentEvals = Fi("run.maxConcurrentEvals"),
            },
            Output = new OutputConfig
            {
                WritePerEvalJson = Fb("output.writePerEvalJson") ?? false,
                WriteSummaryJson = Fb("output.writeSummaryJson") ?? true,
                WriteSummaryCsv = Fb("output.writeSummaryCsv") ?? false,
                WriteResultsParquet = Fb("output.writeParquet") ?? false,
                IncludeRawLlmResponse = Fb("output.includeRawResponse") ?? true,
            },
            EvalSets = [],
            DataSource = dataSource,
        };
    }

    /// <summary>
    /// Builds a PartialConfig from SettingsViewModel field values only.
    /// Does NOT include wizard state - the Settings view is independent.
    /// Note: Empty strings are treated as null throughout settings. If you need
    /// to allow an explicit empty string for any setting, add a "use empty string"
    /// checkbox at that time.
    /// </summary>
    private PartialConfig BuildPartialConfigFromSettingsFields()
    {
        // Helper to get field value - empty strings are treated as null
        string? F(string key)
        {
            var val = SettingsViewModel.SettingsFields.FirstOrDefault(f => f.Key == key)?.Value;
            return string.IsNullOrEmpty(val) ? null : val;
        }
        bool? Fb(string key) => bool.TryParse(F(key), out var v) ? v : null;
        int? Fi(string key) => int.TryParse(F(key), out var v) ? v : null;
        double? Fd(string key) => double.TryParse(F(key), out var v) ? v : null;

        var llamaServerSettings = new PartialLlamaServerSettings
        {
            ContextWindowTokens = Fi("llama.contextWindowTokens"),
            BatchSizeTokens = Fi("llama.batchSizeTokens"),
            UbatchSizeTokens = Fi("llama.ubatchSizeTokens"),
            ParallelSlotCount = Fi("llama.parallelSlotCount"),
            EnableContinuousBatching = Fb("llama.enableContinuousBatching"),
            EnableCachePrompt = Fb("llama.enableCachePrompt"),
            EnableContextShift = Fb("llama.enableContextShift"),
            GpuLayerCount = Fi("llama.gpuLayerCount"),
            SplitMode = F("llama.splitMode") is var sm && sm != "Unspecified" ? sm : null,
            KvCacheTypeK = F("llama.kvCacheTypeK"),
            KvCacheTypeV = F("llama.kvCacheTypeV"),
            EnableKvOffload = Fb("llama.enableKvOffload"),
            EnableFlashAttention = Fb("llama.enableFlashAttention"),
            SamplingTemperature = Fd("llama.samplingTemperature"),
            TopP = Fd("llama.topP"),
            TopK = Fi("llama.topK"),
            MinP = Fd("llama.minP"),
            RepeatPenalty = Fd("llama.repeatPenalty"),
            RepeatLastNTokens = Fi("llama.repeatLastNTokens"),
            PresencePenalty = Fd("llama.presencePenalty"),
            FrequencyPenalty = Fd("llama.frequencyPenalty"),
            Seed = Fi("llama.seed"),
            ThreadCount = Fi("llama.threadCount"),
            HttpThreadCount = Fi("llama.httpThreadCount"),
            ChatTemplate = F("llama.chatTemplate"),
            EnableJinja = Fb("llama.enableJinja"),
            ReasoningFormat = F("llama.reasoningFormat") is var rf && rf != "Unspecified" ? rf : null,
            ModelAlias = F("llama.modelAlias"),
            LogVerbosity = Fi("llama.logVerbosity"),
            EnableMlock = Fb("llama.enableMlock"),
            EnableMmap = Fb("llama.enableMmap"),
            ServerTimeoutSeconds = Fd("llama.serverTimeoutSeconds"),
        };

        var judgeServerSettings = new PartialLlamaServerSettings
        {
            ContextWindowTokens = Fi("judge.contextWindowTokens"),
            BatchSizeTokens = Fi("judge.batchSizeTokens"),
            UbatchSizeTokens = Fi("judge.ubatchSizeTokens"),
            ParallelSlotCount = Fi("judge.parallelSlotCount"),
            EnableContinuousBatching = Fb("judge.enableContinuousBatching"),
            EnableCachePrompt = Fb("judge.enableCachePrompt"),
            EnableContextShift = Fb("judge.enableContextShift"),
            GpuLayerCount = Fi("judge.gpuLayerCount"),
            SplitMode = F("judge.splitMode") is var jsm && jsm != "Unspecified" ? jsm : null,
            KvCacheTypeK = F("judge.kvCacheTypeK"),
            KvCacheTypeV = F("judge.kvCacheTypeV"),
            EnableKvOffload = Fb("judge.enableKvOffload"),
            EnableFlashAttention = Fb("judge.enableFlashAttention"),
            SamplingTemperature = Fd("judge.samplingTemperature"),
            TopP = Fd("judge.topP"),
            TopK = Fi("judge.topK"),
            MinP = Fd("judge.minP"),
            RepeatPenalty = Fd("judge.repeatPenalty"),
            RepeatLastNTokens = Fi("judge.repeatLastNTokens"),
            PresencePenalty = Fd("judge.presencePenalty"),
            FrequencyPenalty = Fd("judge.frequencyPenalty"),
            Seed = Fi("judge.seed"),
            ThreadCount = Fi("judge.threadCount"),
            HttpThreadCount = Fi("judge.httpThreadCount"),
            ChatTemplate = F("judge.chatTemplate"),
            EnableJinja = Fb("judge.enableJinja"),
            ReasoningFormat = F("judge.reasoningFormat") is var jrf && jrf != "Unspecified" ? jrf : null,
            ModelAlias = F("judge.modelAlias"),
            LogVerbosity = Fi("judge.logVerbosity"),
            EnableMlock = Fb("judge.enableMlock"),
            EnableMmap = Fb("judge.enableMmap"),
            ServerTimeoutSeconds = Fd("judge.serverTimeoutSeconds"),
        };

        // Build Judge config - always include, even if not enabled
        var judge = new PartialJudgeConfig
        {
            Enable = Fb("judge.enable"),
            ServerConfig = new PartialServerConfig
            {
                Manage = Fb("judge.manage"),
                ExecutablePath = F("judge.executablePath"),
                Host = F("judge.host"),
                Port = Fi("judge.port"),
                ApiKey = F("judge.apiKey"),
                BaseUrl = F("judge.baseUrl"),
                Model = new ModelSource
                {
                    FilePath = F("judge.modelFile"),
                    HfRepo = F("judge.hfRepo")
                }
            },
            ServerSettings = judgeServerSettings,
            JudgePromptTemplate = F("judge.template"),
            ScoreMinValue = Fd("judge.scoreMin") ?? 0,
            ScoreMaxValue = Fd("judge.scoreMax") ?? 10,
        };

        // Build DataSource config
        var dataSourceKindStr = F("dataSource.kind");
        DataSourceKind? dataSourceKind = dataSourceKindStr?.ToLowerInvariant() switch
        {
            "singlefile" => DataSourceKind.SingleFile,
            "jsonlfile" => DataSourceKind.JsonlFile,
            "splitdirectories" => DataSourceKind.SplitDirectories,
            "directory" => DataSourceKind.Directory,
            _ => null
        };

        var dataSource = new PartialDataSourceConfig
        {
            Kind = dataSourceKind,
            FilePath = F("dataSource.filePath"),
            PromptDirectoryPath = F("dataSource.promptDirectory"),
            ExpectedOutputDirectoryPath = F("dataSource.expectedDirectory"),
        };

        return new PartialConfig
        {
            Server = new PartialServerConfig
            {
                Manage = Fb("server.manage"),
                ExecutablePath = F("server.executablePath"),
                Host = F("server.host"),
                Port = Fi("server.port"),
                ApiKey = F("server.apiKey"),
                BaseUrl = F("server.baseUrl"),
            },
            LlamaServer = llamaServerSettings,
            Judge = judge,
            Run = new PartialRunMeta
            {
                RunName = F("run.name"),
                OutputDirectoryPath = F("run.outputDirectoryPath"),
                ExportShellTarget = F("run.exportShellTarget") is var st && st != "Unspecified" ? ParseShellTarget(st) : null,
                ContinueOnEvalFailure = Fb("run.continueOnEvalFailure"),
                MaxConcurrentEvals = Fi("run.maxConcurrentEvals"),
            },
            Output = new OutputConfig
            {
                WritePerEvalJson = Fb("output.writePerEvalJson") ?? false,
                WriteSummaryJson = Fb("output.writeSummaryJson") ?? true,
                WriteSummaryCsv = Fb("output.writeSummaryCsv") ?? false,
                WriteResultsParquet = Fb("output.writeParquet") ?? false,
                IncludeRawLlmResponse = Fb("output.includeRawResponse") ?? true,
            },
            EvalSets = [],
            DataSource = dataSource,
        };
    }

    private static ShellTarget? ParseShellTarget(string? value) => value?.ToLowerInvariant() switch
    {
        "bash" => ShellTarget.Bash,
        "powershell" => ShellTarget.PowerShell,
        _ => null
    };

    // ─── Results View Methods ─────────────────────────────────────────────────

    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            TopLevel.GetTopLevel(Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null)?
                .Clipboard?.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy text to clipboard");
        }
    }

    private async Task LoadResultsFileAsync()
    {
        var path = await _filePicker.ShowOpenFileDialogAsync(
            "Load Results File",
            "JSON Files|*.json|All Files|*.*");

        if (string.IsNullOrEmpty(path)) return;

        // TODO: Implement results file loading logic
        _logger.LogInformation("Results file selected: {Path}", path);
    }

    private async Task ExportResultsToJsonAsync()
    {
        if (ActiveRun == null)
        {
            _logger.LogWarning("No active run to export");
            return;
        }

        // Export results to JSON
        var results = ActiveRun.Results;
        if (results.Count == 0)
        {
            _logger.LogWarning("No results to export");
            return;
        }

        var suggestedName = $"{WizardState.RunName ?? "results"}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var path = await _filePicker.ShowSaveFileDialogAsync(
            "Export Results JSON",
            "JSON Files|*.json|All Files|*.*",
            suggestedName);

        if (path != null)
        {
            var exportData = results.Select(r => new
            {
                r.Id,
                r.EvalSetId,
                r.Succeeded,
                Status = r.Status,
                r.FailureReason,
                r.DurationSeconds,
                r.StartedAt,
                r.UserPrompt,
                r.ExpectedOutput,
                r.JudgeRationale,
                r.JudgeScore,
                Metrics = r.MetricDisplay.Select(m => new { m.Name, m.Value })
            }).ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
            _logger.LogInformation("Results exported to JSON: {Path}", path);
        }
    }

    private async Task ExportResultsToShellScriptAsync()
    {
        var target = WizardState.ShellTarget ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ShellTarget.PowerShell
            : ShellTarget.Bash);
        var script = ExportScript(target);

        // Show save dialog
        var suggestedName = target == ShellTarget.PowerShell ? "run.ps1" : "run.sh";
        var path = await _filePicker.ShowSaveFileDialogAsync(
            "Save Shell Script",
            target == ShellTarget.PowerShell ? "PowerShell Script|*.ps1|All Files|*.*" : "Shell Script|*.sh|All Files|*.*",
            suggestedName);

        if (path != null)
        {
            File.WriteAllText(path, script);
            _logger.LogInformation("Shell script saved to: {Path}", path);
        }
    }

    private void OpenResultsFolder()
    {
        var outputDir = WizardState.OutputDirectoryPath ?? "./results";
        var fullPath = Path.GetFullPath(outputDir);

        if (Directory.Exists(fullPath))
        {
            // Open in file explorer
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", fullPath);
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", fullPath);
            }
            else
            {
                System.Diagnostics.Process.Start("xdg-open", fullPath);
            }
        }
        else
        {
            _logger.LogWarning("Results folder does not exist: {Path}", fullPath);
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task LoadDefaultSettingsAsync()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Seevalocal.yml"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".Seevalocal", "default.yml")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                await AddSettingsFileAsync(candidate);
                _logger.LogInformation("Auto-loaded settings from {Path}", candidate);
            }
        }
    }

    private void UpdateTitle()
    {
        var runName = WizardState.RunName ?? "(unnamed)";
        TitleBarText = IsModified ? $"Seevalocal — {runName} •" : $"Seevalocal — {runName}";
    }

    private static string SerializeToYaml(PartialConfig config)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();

        var yaml = serializer.Serialize(config);
        return $"# Seevalocal settings\n# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{yaml}";
    }

    // ─── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private bool SetField<T>(ref T f, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(f, v)) return false;
        f = v; OnPropertyChanged(n); return true;
    }

    public void Dispose() => ActiveRun?.Dispose();
}
