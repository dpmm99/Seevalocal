using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
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

    // ─── Results View Commands ────────────────────────────────────────────────

    public ICommand LoadResultsFileCommand { get; }
    public ICommand ExportResultsJsonCommand { get; }
    public ICommand ExportResultsShellScriptCommand { get; }
    public ICommand OpenResultsFolderCommand { get; }

    // ─── Constructor ─────────────────────────────────────────────────────────

    public MainWindowViewModel(
        IConfigurationService configService,
        IRunnerService runnerService,
        IShellScriptExporter scriptExporter,
        ILogger<MainWindowViewModel> logger,
        IFilePickerService filePicker,
        IWizardViewModel wizardState)
    {
        _configService = configService;
        _runnerService = runnerService;
        _scriptExporter = scriptExporter;
        _logger = logger;
        _filePicker = filePicker;
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

        NavigateToWizardCommand = new RelayCommand(() => CurrentView = AppView.Wizard);
        NavigateToDashboardCommand = new RelayCommand(() => CurrentView = AppView.RunDashboard);
        NavigateToResultsCommand = new RelayCommand(() => CurrentView = AppView.Results);
        NavigateToSettingsCommand = new RelayCommand(() => CurrentView = AppView.Settings);
        AddSettingsFileCommand = new RelayCommand(async () => await AddSettingsFileAsync());

        // Settings view commands
        ResetSettingsToDefaultsCommand = new RelayCommand(ResetSettingsToDefaults);
        LoadSettingsFileCommand = new RelayCommand(async () => await AddSettingsFileAsync());
        SaveSettingsFileCommand = new RelayCommand(async () => await SaveSettingsFileAsync());

        // Results view commands
        LoadResultsFileCommand = new RelayCommand(async () => await LoadResultsFileAsync());
        ExportResultsJsonCommand = new RelayCommand(async () => await ExportResultsToJsonAsync());
        ExportResultsShellScriptCommand = new RelayCommand(async () => await ExportResultsToShellScriptAsync());
        OpenResultsFolderCommand = new RelayCommand(OpenResultsFolder);

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
        _logger.LogInformation("Loaded settings file {Path}", filePath);
    }

    /// <summary>Recalculates the SettingsViewModel based on current settings layers.</summary>
    private void RecalculateSettingsViewModel()
    {
        SettingsViewModel.ClearConfigLayers();

        // Add each enabled layer in order
        foreach (var layer in SettingsLayers.OrderBy(l => l.LayerIndex))
        {
            if (layer.IsEnabled)
            {
                SettingsViewModel.AddConfigLayer(layer.DisplayName, layer.Config);
            }
        }

        // Add wizard state as the highest priority layer
        SettingsViewModel.AddConfigLayer("UI (Wizard)", WizardState.BuildPartialConfig());
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
                // Also update the wizard state
                UpdateWizardStateFromSettingsField(field);
            }
        }
    }

    private void UpdateWizardStateFromSettingsField(SettingsFieldViewModel field)
    {
        // Update the corresponding property in WizardState based on the field key
        switch (field.Key)
        {
            case "judge.modelFile":
                typeof(IWizardViewModel).GetProperty("JudgeLocalModelPath")?.SetValue(WizardState, field.Value);
                break;
            case "run.outputDirectoryPath":
                typeof(IWizardViewModel).GetProperty("OutputDirectoryPath")?.SetValue(WizardState, field.Value);
                break;
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

        partials.Add(WizardState.BuildPartialConfig());
        return _configService.Resolve(partials);
    }

    public async Task SaveCurrentConfigAsync(string path)
    {
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
            _logger.LogError("Cannot start run: config resolution failed");
            return;
        }

        var config = resolveResult.Value;
        var errors = _configService.Validate(config);
        if (errors.Count > 0)
        {
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
            _logger.LogError(ex, "Error during run");
        }
    }

    // ─── Script export ────────────────────────────────────────────────────────

    public string ExportScript(ShellTarget target)
    {
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

        var resolveResult = ResolveCurrentConfig();
        if (resolveResult.IsFailed)
        {
            _logger.LogError("Failed to resolve settings: {Error}", resolveResult.Errors[0].Message);
            return;
        }

        await File.WriteAllTextAsync(path, SerializeToYaml(WizardState.BuildPartialConfig()));
        IsModified = false;
        UpdateTitle();
        _logger.LogInformation("Settings saved to {Path}", path);
    }

    // ─── Results View Methods ─────────────────────────────────────────────────

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
