using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Metrics.Models;
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

public enum AppView { Wizard, RunDashboard, Results, Settings, EvalGen }

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

    /// <summary>View model for the Generate Eval Set view.</summary>
    public EvalGenViewModel EvalGenViewModel { get; }

    /// <summary>
    /// Gets the collection of active toast notifications for display in the UI.
    /// </summary>
    public ObservableCollection<ToastMessage> Toasts =>
        (_toastService as ToastService)?.Toasts ?? [];

    // ─── Active run ───────────────────────────────────────────────────────────

    public IEvalRunViewModel? ActiveRun
    {
        get => _activeRun;
        private set
        {
            // Unsubscribe from old run's property changes
            _activeRun?.PropertyChanged -= OnActiveRunPropertyChanged;

            SetField(ref _activeRun, value);
            OnPropertyChanged(nameof(CanNavigateToEvalGen));
            // Update metric stats when active run changes
            OnPropertyChanged(nameof(MetricStats));
            // Notify that CurrentResultsRun has changed (important for Run Dashboard visibility)
            OnPropertyChanged(nameof(CurrentResultsRun));

            // Subscribe to new run's property changes
            _activeRun?.PropertyChanged += OnActiveRunPropertyChanged;
        }
    }

    /// <summary>
    /// Temporarily loaded results from a checkpoint database or JSON file.
    /// Used when viewing results without an active run.
    /// </summary>
    public TempEvalRunViewModel? LoadedResultsRun
    {
        get => _loadedResultsRun;
        private set
        {
            _loadedResultsRun?.PropertyChanged -= OnLoadedResultsRunPropertyChanged;

            SetField(ref _loadedResultsRun, value);

            _loadedResultsRun?.PropertyChanged += OnLoadedResultsRunPropertyChanged;

            // Notify all dependent properties immediately
            OnPropertyChanged(nameof(CurrentResultsRun));
            OnPropertyChanged(nameof(MetricStats));
        }
    }
    private TempEvalRunViewModel? _loadedResultsRun;

    /// <summary>
    /// Gets the current results view model - either from an active run or loaded results.
    /// </summary>
    public IEvalRunViewModel? CurrentResultsRun => ActiveRun ?? LoadedResultsRun;

    private void OnLoadedResultsRunPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Results" or "EarlyCompletionsLimit")
        {
            // Notify on UI thread for Avalonia binding
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(MetricStats));
                // Notify that CurrentResultsRun's collection properties have changed
                OnPropertyChanged(nameof(CurrentResultsRun));
            });
        }
    }

    private void OnActiveRunPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Results")
        {
            // Notify on UI thread for Avalonia binding
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(MetricStats));
                // Notify that CurrentResultsRun's collection properties have changed
                // This is needed for Run Dashboard EarlyCompletions and Results Viewer
                OnPropertyChanged(nameof(CurrentResultsRun));
            });
        }
    }

    /// <summary>
    /// Metric statistics calculated from all items in the active run or loaded results.
    /// Groups metrics by stage and shows min, max, avg, count, and missing count.
    /// </summary>
    public List<StageStatsViewModel> MetricStats =>
        ActiveRun?.Results != null
            ? MetricStatsCalculator.Calculate(ActiveRun.Results)
            : LoadedResultsRun?.Results != null
                ? MetricStatsCalculator.Calculate(LoadedResultsRun.Results)
                : [];

    /// <summary>
    /// Whether navigation to Eval Gen view is allowed.
    /// Blocked when any run is active (regular eval run or eval gen run).
    /// </summary>
    public bool CanNavigateToEvalGen => !IsAnyRunActive;

    /// <summary>
    /// Whether any run is currently active (eval run or eval gen run).
    /// </summary>
    private bool IsAnyRunActive => ActiveRun?.IsRunning == true || EvalGenViewModel?.IsRunning == true;

    // ─── Navigation Commands ──────────────────────────────────────────────────

    public ICommand NavigateToWizardCommand { get; }
    public ICommand NavigateToDashboardCommand { get; }
    public ICommand NavigateToResultsCommand { get; }
    public ICommand NavigateToSettingsCommand { get; }
    public ICommand NavigateToEvalGenCommand { get; }
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
        IToastService toastService,
        EvalGenViewModel evalGenViewModel)
    {
        _configService = configService;
        _runnerService = runnerService;
        _scriptExporter = scriptExporter;
        _logger = logger;
        _filePicker = filePicker;
        _toastService = toastService;
        WizardState = wizardState;
        SettingsViewModel = new SettingsViewModel();
        EvalGenViewModel = evalGenViewModel;

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

        // Subscribe to eval gen run state changes to update navigation availability
        EvalGenViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EvalGenViewModel.IsRunning) or nameof(EvalGenViewModel.IsCompleted))
            {
                OnPropertyChanged(nameof(CanNavigateToEvalGen));
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
        NavigateToEvalGenCommand = new RelayCommand(() => CurrentView = AppView.EvalGen, () => CanNavigateToEvalGen);
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
        // Determine dialog identifier based on field key
        string? dialogIdentifier = args.FieldKey switch
        {
            // llama-server executable paths
            "server.executablePath" or "judge.executablePath" => "llama-server",
            
            // Model file paths
            "server.modelFile" or "judge.modelFile" => "model-file",
            
            // Output/checkpoint paths and split directories data source
            "run.outputDirectoryPath" or "run.checkpointDatabasePath" or
            "dataSource.promptDirectory" or "dataSource.expectedDirectory" => "output",
            
            // Single file data source
            "dataSource.filePath" => "data-file",
            
            // Settings save/load (detected by filter containing "Settings")
            _ when args.Filter?.Contains("Settings", StringComparison.OrdinalIgnoreCase) == true => "settings",
            
            // Default: no identifier
            _ => null
        };

        string? path = args.IsFolder
            ? await _filePicker.ShowOpenFolderDialogAsync("Select Folder", args.InitialPath, dialogIdentifier)
            : await _filePicker.ShowOpenFileDialogAsync("Select File", args.Filter ?? "All Files|*.*", args.InitialPath, dialogIdentifier);
        if (path != null)
        {
            // Find the field and update its value
            var field = SettingsViewModel.SettingsFields.FirstOrDefault(f => f.Key == args.FieldKey);
            field?.Value = path;
        }
    }

    private void UpdateWizardStateFromSettingsField(SettingsFieldViewModel field)
    {
        if (WizardState is not WizardViewModel state) return;

        // Use the reflection-based helper to apply the field value
        SettingsFieldMapping.ApplyFieldToWizard(field, state);
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
    }

    public void RemoveSettingsLayer(SettingsLayerViewModel layer)
    {
        _ = SettingsLayers.Remove(layer);
        IsModified = true;
    }

    /// <summary>Resolves the current settings stack + wizard state into a <see cref="ResolvedConfig"/>.</summary>
    public Result<ResolvedConfig> ResolveCurrentConfig(bool withWizard = true)
    {
        var partials = SettingsLayers
            .Where(static l => l.IsEnabled)
            .OrderBy(static l => l.LayerIndex)
            .Select(static l => l.Config)
            .ToList();

        // Add Settings view field values as session overrides (higher priority than loaded files)
        partials.Add(SettingsViewModel.BuildPartialConfigFromFields());

        // Add wizard state as highest priority
        if (withWizard) partials.Add(WizardState.BuildPartialConfig());
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

        if (string.IsNullOrWhiteSpace(resolveResult.Value.Run.RunName))
        {
            resolveResult = resolveResult.Value with { Run = resolveResult.Value.Run with { RunName = $"run_{DateTime.Now:yyyyMMdd_HHmmss}" } };
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
            "YAML Files|*.yml;*.yaml|JSON Files|*.json|All Files|*.*",
            dialogIdentifier: "settings");

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
            "YAML Files|*.yml;*.yaml|All Files|*.*",
            dialogIdentifier: "settings");

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
        var fields = SettingsViewModel.SettingsFields;

        // Helper to get materialized field value - empty strings are treated as null
        string? F(string key) => SettingsFieldMapping.GetFieldValue(fields, key, f => f.MaterializedValue);

        // Use reflection-based helper to build llama server settings
        var llamaServerSettings = SettingsFieldMapping.BuildLlamaServerSettings(fields, "llama", f => f.MaterializedValue);
        var judgeServerSettings = SettingsFieldMapping.BuildLlamaServerSettings(fields, "judge", f => f.MaterializedValue);

        // Build Judge config - always include, even if not enabled
        var judge = new PartialJudgeConfig
        {
            Enable = SettingsFieldMapping.ParseBool(F("judge.enable")),
            ServerConfig = new PartialServerConfig
            {
                Manage = SettingsFieldMapping.ParseBool(F("judge.manage")),
                ExecutablePath = F("judge.executablePath"),
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
            ScoreMinValue = double.TryParse(F("judge.scoreMin"), out var sm) ? sm : 0,
            ScoreMaxValue = double.TryParse(F("judge.scoreMax"), out var sx) ? sx : 10,
        };

        // Build DataSource config
        var dataSource = new PartialDataSourceConfig
        {
            Kind = SettingsFieldMapping.ParseDataSourceKind(F("dataSource.kind")),
            FilePath = F("dataSource.filePath"),
            PromptDirectory = F("dataSource.promptDirectory"),
            ExpectedDirectory = F("dataSource.expectedDirectory"),
        };

        return new PartialConfig
        {
            Server = new PartialServerConfig
            {
                Manage = SettingsFieldMapping.ParseBool(F("server.manage")),
                ExecutablePath = F("server.executablePath"),
                ApiKey = F("server.apiKey"),
                BaseUrl = F("server.baseUrl"),
            },
            LlamaSettings = llamaServerSettings,
            Judge = judge,
            Run = new PartialRunMeta
            {
                RunName = F("run.name"),
                OutputDirectoryPath = F("run.outputDirectoryPath"),
                ExportShellTarget = SettingsFieldMapping.ParseShellTarget(F("run.exportShellTarget")),
                ContinueOnEvalFailure = SettingsFieldMapping.ParseBool(F("run.continueOnEvalFailure")),
                MaxConcurrentEvals = int.TryParse(F("run.maxConcurrentEvals"), out var mc) ? mc : null,
            },
            Output = new OutputConfig
            {
                WritePerEvalJson = SettingsFieldMapping.ParseBool(F("output.writePerEvalJson")) ?? false,
                WriteSummaryJson = SettingsFieldMapping.ParseBool(F("output.writeSummaryJson")) ?? true,
                WriteSummaryCsv = SettingsFieldMapping.ParseBool(F("output.writeSummaryCsv")) ?? false,
                WriteResultsParquet = SettingsFieldMapping.ParseBool(F("output.writeParquet")) ?? false,
                IncludeRawLlmResponse = SettingsFieldMapping.ParseBool(F("output.includeRawResponse")) ?? true,
            },
            DataSource = dataSource,
            //TODO: PipelineOptions is a dictionary. See SettingsViewModel.
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
        var fields = SettingsViewModel.SettingsFields;

        // Helper to get field value - empty strings are treated as null
        string? F(string key) => SettingsFieldMapping.GetFieldValue(fields, key, f => f.Value);

        // Use reflection-based helper to build llama server settings
        var llamaServerSettings = SettingsFieldMapping.BuildLlamaServerSettings(fields, "llama", f => f.Value);
        var judgeServerSettings = SettingsFieldMapping.BuildLlamaServerSettings(fields, "judge", f => f.Value);

        // Build Judge config - always include, even if not enabled
        var judge = new PartialJudgeConfig
        {
            Enable = SettingsFieldMapping.ParseBool(F("judge.enable")),
            ServerConfig = new PartialServerConfig
            {
                Manage = SettingsFieldMapping.ParseBool(F("judge.manage")),
                ExecutablePath = F("judge.executablePath"),
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
            ScoreMinValue = double.TryParse(F("judge.scoreMin"), out var sm) ? sm : 0,
            ScoreMaxValue = double.TryParse(F("judge.scoreMax"), out var sx) ? sx : 10,
        };

        // Build DataSource config
        var dataSource = new PartialDataSourceConfig
        {
            Kind = SettingsFieldMapping.ParseDataSourceKind(F("dataSource.kind")),
            FilePath = F("dataSource.filePath"),
            PromptDirectory = F("dataSource.promptDirectory"),
            ExpectedDirectory = F("dataSource.expectedDirectory"),
        };

        return new PartialConfig
        {
            Server = new PartialServerConfig
            {
                Manage = SettingsFieldMapping.ParseBool(F("server.manage")),
                ExecutablePath = F("server.executablePath"),
                ApiKey = F("server.apiKey"),
                BaseUrl = F("server.baseUrl"),
            },
            LlamaSettings = llamaServerSettings,
            Judge = judge,
            Run = new PartialRunMeta
            {
                RunName = F("run.name"),
                OutputDirectoryPath = F("run.outputDirectoryPath"),
                ExportShellTarget = SettingsFieldMapping.ParseShellTarget(F("run.exportShellTarget")),
                ContinueOnEvalFailure = SettingsFieldMapping.ParseBool(F("run.continueOnEvalFailure")),
                MaxConcurrentEvals = int.TryParse(F("run.maxConcurrentEvals"), out var mc) ? mc : null,
            },
            Output = new OutputConfig
            {
                WritePerEvalJson = SettingsFieldMapping.ParseBool(F("output.writePerEvalJson")) ?? false,
                WriteSummaryJson = SettingsFieldMapping.ParseBool(F("output.writeSummaryJson")) ?? true,
                WriteSummaryCsv = SettingsFieldMapping.ParseBool(F("output.writeSummaryCsv")) ?? false,
                WriteResultsParquet = SettingsFieldMapping.ParseBool(F("output.writeParquet")) ?? false,
                IncludeRawLlmResponse = SettingsFieldMapping.ParseBool(F("output.includeRawResponse")) ?? true,
            },
            DataSource = dataSource,
            //TODO: PipelineOptions is a dictionary. See SettingsViewModel.
        };
    }

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
            "Checkpoint Database|*.db|JSON Files|*.json|All Files|*.*");

        if (string.IsNullOrEmpty(path)) return;

        try
        {
            if (path.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                // Load from checkpoint database
                await LoadResultsFromCheckpointDbAsync(path);
            }
            else
            {
                // Load from JSON file (existing logic - TODO)
                _toastService.Show("JSON file loading not yet implemented. Please select a checkpoint database (.db file).", 5000);
                _logger.LogInformation("JSON file loading not implemented: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load results: {ex.Message}", 8000);
            _logger.LogError(ex, "Failed to load results file: {Path}", path);
        }
    }

    private async Task LoadResultsFromCheckpointDbAsync(string dbPath)
    {
        if (!File.Exists(dbPath))
        {
            _toastService.ShowError("Database file not found", 5000);
            return;
        }

        // Create a temporary collector to load results from the database
        var collector = new PersistentResultCollector(dbPath);

        try
        {
            _logger.LogInformation("Loading eval results");

            // Load ALL results merged from all phases (primary, judge, etc.)
            // This ensures we get complete data including metrics and stage outputs from all phases
            var results = await collector.GetAllResultsMergedAsync(default);
            _logger.LogInformation("Loaded {Count} merged results from checkpoint: {Path}", results.Count, dbPath);

            if (results.Count == 0)
            {
                _toastService.Show("No results found in checkpoint database", 5000);
                return;
            }

            // Create a temporary view model to display results
            var tempRunVm = new TempEvalRunViewModel(results);
            _logger.LogInformation("Created TempEvalRunViewModel with {Count} results", tempRunVm.Results.Count);

            // Store in a property for the Results view to access
            // This will trigger property change notifications for CurrentResultsRun and MetricStats
            LoadedResultsRun = tempRunVm;

            // Switch to Results view
            CurrentView = AppView.Results;

            _toastService.ShowSuccess($"Loaded {results.Count} results from checkpoint database", 5000);
        }
        catch (Exception ex)
        {
            _toastService.ShowError($"Failed to load results: {ex.Message}", 8000);
            _logger.LogError(ex, "Failed to load results from checkpoint: {Path}", dbPath);
        }
        finally
        {
            await collector.DisposeAsync();
        }
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
                r.Succeeded,
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

        // After loading default settings, sync wizard defaults to ensure the wizard
        // shows the correct values from the settings file at startup
        var config = ResolveCurrentConfig();
        if (config.IsSuccess)
        {
            WizardState.SyncDefaultsFromSettings(config.Value, SettingsViewModel);
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

/// <summary>
/// Temporary view model for displaying loaded results from a checkpoint database or JSON file.
/// Implements minimal IEvalRunViewModel interface for results display.
/// </summary>
public sealed class TempEvalRunViewModel : IEvalRunViewModel, INotifyPropertyChanged
{
    private int _earlyCompletionsLimit = 10;

    public TempEvalRunViewModel(IReadOnlyList<EvalResult> results)
    {
        Results = new ObservableCollection<EvalResultViewModel>(results.Select(r => new EvalResultViewModel(r)));
        EarlyCompletions = [];
        TotalCount = results.Count;
        CompletedCount = results.Count;
        StatusLine = "Loaded from checkpoint";
        Config = new ResolvedConfig();

        // Initialize EarlyCompletions with first N items
        UpdateEarlyCompletions();

        // Make LoadMoreEarlyCompletionsCommand actually work
        LoadMoreEarlyCompletionsCommand = new RelayCommand(LoadMoreEarlyCompletions, () => HasMoreEarlyCompletions);

        // Notify that Results collection is ready (for UI binding)
        OnPropertyChanged(nameof(Results));
        OnPropertyChanged(nameof(EarlyCompletions));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(HadFailures));
    }

    public ResolvedConfig Config { get; }
    public ObservableCollection<EvalResultViewModel> Results { get; }
    public ObservableCollection<EvalResultViewModel> EarlyCompletions { get; }
    public int TotalCount { get; }
    public int CompletedCount { get; }
    public double ProgressPercent => 100;
    public string StatusLine { get; }
    public bool IsRunning => false;
    public bool IsPaused => false;
    public bool HadFailures => Results.Any(r => !r.Succeeded);
    public double? EstimatedRemainingSeconds => null;
    public double AverageTokensPerSecond => 0;

    public int EarlyCompletionsLimit
    {
        get => _earlyCompletionsLimit;
        set
        {
            if (_earlyCompletionsLimit != value)
            {
                _earlyCompletionsLimit = value;
                OnPropertyChanged(nameof(EarlyCompletionsLimit));
                UpdateEarlyCompletions();
                OnPropertyChanged(nameof(HasMoreEarlyCompletions));
                ((RelayCommand)LoadMoreEarlyCompletionsCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasMoreEarlyCompletions => Results.Count > EarlyCompletionsLimit;
    public RunSummary? Summary => null;

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

    /// <summary>
    /// Updates the EarlyCompletions collection to contain the first N results.
    /// </summary>
    private void UpdateEarlyCompletions()
    {
        var newEarlyCompletions = Results.Take(EarlyCompletionsLimit).ToList();

        for (int i = EarlyCompletions.Count - 1; i >= 0; i--)
        {
            if (i >= newEarlyCompletions.Count || EarlyCompletions[i] != newEarlyCompletions[i])
            {
                EarlyCompletions.RemoveAt(i);
            }
        }

        for (int i = EarlyCompletions.Count; i < newEarlyCompletions.Count; i++)
        {
            EarlyCompletions.Add(newEarlyCompletions[i]);
        }
    }

    public ICommand PauseCommand { get; } = new RelayCommand(() => { }, () => false);
    public ICommand CancelCommand { get; } = new RelayCommand(() => { }, () => false);
    public ICommand LoadMoreEarlyCompletionsCommand { get; }

    private void LoadMoreEarlyCompletions()
    {
        EarlyCompletionsLimit += 10;
    }

    public void TogglePause() { }
    public void Cancel() { }
    public Task StartAsync(CancellationToken externalCt = default) => Task.CompletedTask;
    public void Dispose() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
