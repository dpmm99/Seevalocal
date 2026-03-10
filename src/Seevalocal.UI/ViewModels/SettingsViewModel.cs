using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Seevalocal.Core.Models;
using Seevalocal.UI.Commands;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Seevalocal.UI.ViewModels;

/// <summary>
/// Represents a single settings field in the SettingsView.
/// </summary>
public sealed class SettingsFieldViewModel : INotifyPropertyChanged
{
    private string _searchText = "";
    private bool _isVisible = true;
    private string? _materializedValue;
    private string? _origin;
    private string? _value;

    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }
    public string? Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value))
            {
                // Notify that materialized value and origin have changed so the indicators update
                OnPropertyChanged(nameof(MaterializedValue));
                OnPropertyChanged(nameof(Origin));
                OnPropertyChanged(nameof(IsLlamaCppDefault));
            }
        }
    }
    public string? Watermark { get; init; }
    public bool IsOptional { get; init; }
    public string Section { get; init; } = "";

    /// <summary>
    /// Callback to get the materialized value from config layers (excluding this field's own Value).
    /// Set by SettingsViewModel during initialization.
    /// </summary>
    public Func<string?, (string? Value, string? Origin)>? GetMaterializedValueFromLayers { get; set; }

    /// <summary>
    /// The final materialized value after merging all configuration layers.
    /// The field's own Value takes highest priority (unless empty or "Unspecified").
    /// </summary>
    public string? MaterializedValue
    {
        get
        {
            // Field's own Value has highest priority (if not empty or "Unspecified")
            if (!string.IsNullOrEmpty(_value) && _value != "Unspecified")
                return _value;

            // Otherwise, use value from config layers
            return GetMaterializedValueFromLayers?.Invoke(_value).Value ?? _materializedValue;
        }
        set => SetField(ref _materializedValue, value);
    }

    /// <summary>
    /// The origin of the materialized value (e.g., file path, "CLI", "Default", "UI").
    /// </summary>
    public string? Origin
    {
        get
        {
            // Field's own Value has highest priority (if not empty or "Unspecified")
            if (!string.IsNullOrEmpty(_value) && _value != "Unspecified")
                return "UI";

            // Otherwise, use origin from config layers
            return GetMaterializedValueFromLayers?.Invoke(_value).Origin ?? _origin;
        }
        set => SetField(ref _origin, value);
    }

    /// <summary>
    /// True if the materialized value is null, meaning llama-server will use its own default.
    /// </summary>
    public bool IsLlamaCppDefault
    {
        get
        {
            // If there's a value from UI or any config layer, it's not a llama.cpp default
            var hasValue = (!string.IsNullOrEmpty(_value) && _value != "Unspecified") ||
                           (GetMaterializedValueFromLayers?.Invoke(_value).Value != null) ||
                           (_materializedValue != null);
            return !hasValue && IsLlamaServerSetting(Key);
        }
    }

    private static bool IsLlamaServerSetting(string key) => key.StartsWith("llama.", StringComparison.Ordinal);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                UpdateVisibility();
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    /// <summary>
    /// True if this field is a boolean field (should use ComboBox with Unspecified/true/false).
    /// </summary>
    public bool IsBooleanField => Key.Contains("enable", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("manage", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("mlock", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("mmap", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("jinja", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("kvOffload", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("flashAttention", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("continuousBatching", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("cachePrompt", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("contextShift", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("continueOnEvalFailure", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("writePerEval", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("writeSummary", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("writeParquet", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("writeResults", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("includeRaw", StringComparison.OrdinalIgnoreCase) ||
                                  Key.Contains("includeAll", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if this field is an enum field (should use ComboBox with enum values).
    /// </summary>
    public bool IsEnumField => Key.EndsWith("template", StringComparison.OrdinalIgnoreCase) ||
                               Key.EndsWith("Template", StringComparison.OrdinalIgnoreCase) ||
                               Key.Contains("shellTarget", StringComparison.OrdinalIgnoreCase) ||
                               Key.Contains("ShellTarget", StringComparison.OrdinalIgnoreCase) ||
                               Key.Contains("splitMode", StringComparison.OrdinalIgnoreCase) ||
                               Key.Contains("SplitMode", StringComparison.OrdinalIgnoreCase) ||
                               Key.Contains("reasoningFormat", StringComparison.OrdinalIgnoreCase) ||
                               Key.Contains("ReasoningFormat", StringComparison.OrdinalIgnoreCase) ||
                               Key.Contains("logVerbosity", StringComparison.OrdinalIgnoreCase) ||
                               Key.Contains("LogVerbosity", StringComparison.OrdinalIgnoreCase) ||
                               Key.Contains("dataSource.kind", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if this field is a file path (should show Browse button).
    /// </summary>
    public bool IsFilePathField => Key.Contains("executablePath", StringComparison.OrdinalIgnoreCase) ||
                                   Key.Contains("ExecutablePath", StringComparison.OrdinalIgnoreCase) ||
                                   Key.Contains("modelFile", StringComparison.OrdinalIgnoreCase) ||
                                   Key.Contains("ModelFile", StringComparison.OrdinalIgnoreCase) ||
                                   Key.Contains("filePath", StringComparison.OrdinalIgnoreCase) ||
                                   Key.Contains("FilePath", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if this field is a folder path (should show Browse button).
    /// </summary>
    public bool IsFolderPathField => Key.Contains("outputDirectoryPath", StringComparison.OrdinalIgnoreCase) ||
                                     Key.Contains("OutputDirectoryPath", StringComparison.OrdinalIgnoreCase) ||
                                     Key.Contains("promptDirectory", StringComparison.OrdinalIgnoreCase) ||
                                     Key.Contains("PromptDirectory", StringComparison.OrdinalIgnoreCase) ||
                                     Key.Contains("expectedDirectory", StringComparison.OrdinalIgnoreCase) ||
                                     Key.Contains("ExpectedDirectory", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if this field is the data source kind enum field.
    /// </summary>
    public bool IsDataSourceKindField => Key == "dataSource.kind";

    /// <summary>
    /// Gets the enum options for enum fields.
    /// </summary>
    public string[]? EnumOptions => Key switch
    {
        "judge.template" or "judgeTemplate" => GetJudgeTemplateOptions(),
        "run.exportShellTarget" or "ShellTarget" => ["Unspecified", "bash", "powershell"],
        "llama.splitMode" or "SplitMode" => ["Unspecified", "none", "layer", "row"],
        "llama.reasoningFormat" or "ReasoningFormat" => ["Unspecified", "deepseek", "r1", "none"],
        "llama.logVerbosity" or "LogVerbosity" => ["Unspecified", "0", "1", "2", "3"],
        "dataSource.kind" => ["Unspecified", "SingleFile", "JsonlFile", "SplitDirectories", "Directory"],
        _ => null
    };

    /// <summary>
    /// Gets the available judge template names using reflection from DefaultTemplates class.
    /// </summary>
    private static string[] GetJudgeTemplateOptions()
    {
        // Get all public static string constants from DefaultTemplates
        var templateType = typeof(Core.DefaultTemplates);
        var constants = templateType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .Order()
            .ToArray();

        // Convert PascalCase names to kebab-case for UI
        var options = new List<string> { "Unspecified" };
        options.AddRange(constants.Select(name =>
        {
            // Convert PascalCase to kebab-case (e.g., "PassFail" -> "pass-fail")
            var kebab = System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
            return kebab;
        }));
        return options.ToArray();
    }

    /// <summary>
    /// True if this field is a regular text field (not boolean, not enum, not file path).
    /// </summary>
    public bool IsTextField => !IsBooleanField && !IsEnumField;

    /// <summary>
    /// True if this field is the judge template enum field.
    /// </summary>
    public bool IsJudgeTemplateField => Key == "judge.template";

    /// <summary>
    /// True if this field is the shell target enum field.
    /// </summary>
    public bool IsShellTargetField => Key == "run.exportShellTarget";

    /// <summary>
    /// True if this field is the split mode enum field.
    /// </summary>
    public bool IsSplitModeField => Key == "llama.splitMode";

    /// <summary>
    /// True if this field is the reasoning format enum field.
    /// </summary>
    public bool IsReasoningFormatField => Key == "llama.reasoningFormat";

    /// <summary>
    /// True if this field is the log verbosity enum field.
    /// </summary>
    public bool IsLogVerbosityField => Key == "llama.logVerbosity";

    private void UpdateVisibility()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            IsVisible = true;
        }
        else
        {
            // Only match on DisplayName, Key, and Section - not Description
            IsVisible = DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                       Key.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                       (Section?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private bool SetField<T>(ref T fieldVar, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(fieldVar, v)) return false;
        fieldVar = v; OnPropertyChanged(n); return true;
    }
}

/// <summary>
/// Represents a group of settings fields for a section.
/// </summary>
public sealed class SettingsSectionGroup : INotifyPropertyChanged
{
    private readonly ObservableCollection<SettingsFieldViewModel> _fields;

    public SettingsSectionGroup(string sectionName, IEnumerable<SettingsFieldViewModel> fields)
    {
        SectionName = sectionName;
        _fields = new ObservableCollection<SettingsFieldViewModel>(fields);

        // Subscribe to property changes in child fields; only notify when visibility changes
        foreach (var field in _fields)
        {
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsFieldViewModel.IsVisible))
                {
                    OnPropertyChanged(nameof(Fields));
                    OnPropertyChanged(nameof(VisibleFieldCount));
                }
                else if (e.PropertyName == nameof(SettingsFieldViewModel.MaterializedValue))
                {
                    //TODO: update the materialization info for this single field
                }
            };
        }
    }

    public string SectionName { get; }

    public IEnumerable<SettingsFieldViewModel> Fields => _fields.Where(f => f.IsVisible);

    public int VisibleFieldCount => _fields.Count(f => f.IsVisible);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// View model for the Settings view with search functionality.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private string _searchText = "";
    private readonly List<(string Origin, PartialConfig Config)> _configLayers = [];
    private readonly List<SettingsSectionGroup> _cachedSectionGroups = [];
    private bool _sectionGroupsDirty = true;

    public ObservableCollection<SettingsFieldViewModel> SettingsFields { get; } = [];

    /// <summary>
    /// Settings fields grouped by section for display.
    /// Cached to avoid recreating objects on every property change.
    /// </summary>
    public IEnumerable<SettingsSectionGroup> SettingsFieldsBySection
    {
        get
        {
            if (_sectionGroupsDirty)
            {
                RebuildSectionGroups();
            }
            return _cachedSectionGroups;
        }
    }

    private void RebuildSectionGroups()
    {
        _cachedSectionGroups.Clear();
        _cachedSectionGroups.AddRange(
            SettingsFields
                .GroupBy(f => f.Section)
                .Select(g => new SettingsSectionGroup(g.Key, g))
                .Where(g => g.Fields.Any())
        );
        _sectionGroupsDirty = false;
        OnPropertyChanged(nameof(SettingsFieldsBySection));
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                foreach (var f in SettingsFields)
                {
                    f.SearchText = value;
                }
                // Mark section groups as dirty so they get rebuilt on next access
                _sectionGroupsDirty = true;
            }
        }
    }

    public SettingsViewModel()
    {
        InitializeSettingsFields();
        BrowseFileCommand = new RelayCommand<string>(BrowseFile);
        BrowseFolderCommand = new RelayCommand<string>(BrowseFolder);
        CopyTextCommand = new RelayCommand<string>(CopyText);
        RecalculateMaterializedValues();
    }

    /// <summary>
    /// Event args for browse requests.
    /// </summary>
    public class BrowseEventArgs : EventArgs
    {
        public string FieldKey { get; init; } = "";
        public bool IsFolder { get; init; }
        public string? InitialPath { get; init; }
        public string? Filter { get; init; }
    }

    private void BrowseFile(string? fieldKey)
    {
        if (fieldKey != null)
        {
            // Determine appropriate filter based on field key
            var filter = GetFilterForField(fieldKey);
            BrowseRequested?.Invoke(this, new BrowseEventArgs
            {
                FieldKey = fieldKey,
                IsFolder = false,
                Filter = filter
            });
        }
    }

    private static string GetFilterForField(string fieldKey)
    {
        return fieldKey.ToLowerInvariant() switch
        {
            var k when k.Contains("executablepath") => "Executable Files|llama-server;llama-server.exe|All Files|*.*",
            var k when k.Contains("modelfile") => "Model Files|*.gguf|All Files|*.*",
            var k when k.Contains("filepath") && k.Contains("data") => "Data Files|*.json;*.yaml;*.yml;*.csv;*.parquet;*.jsonl|All Files|*.*",
            var k when k.Contains("database") || k.Contains(".db") => "SQLite Database|*.db|All Files|*.*",
            _ => "All Files|*.*"
        };
    }

    private void BrowseFolder(string? fieldKey)
    {
        if (fieldKey != null)
            BrowseRequested?.Invoke(this, new BrowseEventArgs
            {
                FieldKey = fieldKey,
                IsFolder = true
            });
    }

    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            TopLevel.GetTopLevel(Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null)?
                .Clipboard?.SetTextAsync(text);
        }
        catch
        {
            // Silently fail - clipboard access can fail in some contexts
        }
    }

    /// <summary>
    /// Adds a configuration layer and recalculates materialized values.
    /// </summary>
    public void AddConfigLayer(string origin, PartialConfig config)
    {
        _configLayers.Add((origin, config));
        RecalculateMaterializedValues();
    }

    /// <summary>
    /// Builds a PartialConfig from the current field values in this SettingsViewModel.
    /// Used to include Settings view edits in config resolution.
    /// Note: Empty strings are treated as null throughout settings. If you need
    /// to allow an explicit empty string for any setting, add a "use empty string"
    /// checkbox at that time.
    /// </summary>
    public PartialConfig BuildPartialConfigFromFields()
    {
        // Helper to get field value - use MaterializedValue which includes values from loaded files
        // Empty strings are treated as null
        string? F(string key)
        {
            var val = SettingsFields.FirstOrDefault(f => f.Key == key)?.MaterializedValue;
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
            Judge = new PartialJudgeConfig
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
            },
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
            DataSource = new PartialDataSourceConfig
            {
                Kind = F("dataSource.kind") is var kind && !string.IsNullOrEmpty(kind) && kind != "Unspecified"
                    ? ParseDataSourceKind(kind) : null,
                FilePath = F("dataSource.filePath"),
                PromptDirectoryPath = F("dataSource.promptDirectory"),
                ExpectedOutputDirectoryPath = F("dataSource.expectedDirectory"),
            },
            EvalSets = [],
        };

        static ShellTarget? ParseShellTarget(string? value) => value?.ToLowerInvariant() switch
        {
            "bash" => ShellTarget.Bash,
            "powershell" => ShellTarget.PowerShell,
            _ => null
        };

        static DataSourceKind? ParseDataSourceKind(string? value) => value?.ToLowerInvariant() switch
        {
            "singlefile" => DataSourceKind.SingleFile,
            "jsonlfile" => DataSourceKind.JsonlFile,
            "splitdirectories" or "directorypair" => DataSourceKind.SplitDirectories,
            "directory" => DataSourceKind.Directory,
            _ => null
        };
    }

    /// <summary>
    /// Clears all configuration layers and resets materialized values.
    /// </summary>
    public void ClearConfigLayers()
    {
        _configLayers.Clear();
        RecalculateMaterializedValues();
    }

    /// <summary>
    /// Command to browse for a file.
    /// </summary>
    public ICommand BrowseFileCommand { get; }

    /// <summary>
    /// Command to browse for a folder.
    /// </summary>
    public ICommand BrowseFolderCommand { get; }

    /// <summary>
    /// Command to copy text to clipboard.
    /// </summary>
    public ICommand CopyTextCommand { get; }

    /// <summary>
    /// Event raised when a file or folder is browsed.
    /// </summary>
    public event EventHandler<BrowseEventArgs>? BrowseRequested;

    /// <summary>
    /// Raises the BrowseRequested event.
    /// </summary>
    public void OnBrowseRequested(BrowseEventArgs args)
    {
        BrowseRequested?.Invoke(this, args);
    }

    /// <summary>
    /// Recalculates materialized values and origins for all settings fields.
    /// </summary>
    private void RecalculateMaterializedValues()
    {
        foreach (var field in SettingsFields)
        {
            // Set up callback for this field to get materialized value from layers
            field.GetMaterializedValueFromLayers = _ => FindMaterializedValue(field.Key);

            // Update the stored materialized value and origin (these are used as fallbacks)
            var (value, origin) = FindMaterializedValue(field.Key);
            field.MaterializedValue = value;
            field.Origin = origin;
            // IsLlamaCppDefault is now computed, no need to set it
        }

        // Mark section groups as dirty so they get rebuilt on next access
        _sectionGroupsDirty = true;
    }

    /// <summary>
    /// Finds the materialized value and origin for a given settings key.
    /// </summary>
    private (string? Value, string? Origin) FindMaterializedValue(string key)
    {
        // Walk from highest priority (last) to lowest (first)
        for (int i = _configLayers.Count - 1; i >= 0; i--)
        {
            var (origin, config) = _configLayers[i];
            var value = GetValueFromConfig(config, key);
            if (value is not null)
            {
                return (value, origin);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Gets a value from a PartialConfig based on the key path.
    /// </summary>
    private static string? GetValueFromConfig(PartialConfig config, string key)
    {
        var parts = key.Split('.');
        return parts[0] switch
        {
            "server" => GetServerValue(config.Server, parts[1]),
            "llama" => GetLlamaValue(config.LlamaServer, parts[1]),
            "judge" => GetJudgeValue(config.Judge, parts[1]),
            "output" => GetOutputValue(config.Output, parts[1]),
            "run" => GetRunValue(config.Run, parts[1]),
            "dataSource" => GetDataSourceValue(config.DataSource, parts[1]),
            _ => null
        };
    }

    private static string? GetDataSourceValue(PartialDataSourceConfig? ds, string field) => field switch
    {
        "kind" => ds?.Kind?.ToString(),
        "filePath" => ds?.FilePath,
        "promptDirectory" => ds?.PromptDirectoryPath,
        "expectedDirectory" => ds?.ExpectedOutputDirectoryPath,
        _ => null
    };

    private static string? GetServerValue(PartialServerConfig? server, string field) => field switch
    {
        "manage" => server?.Manage?.ToString().ToLowerInvariant(),
        "executablePath" => server?.ExecutablePath,
        "host" => server?.Host,
        "port" => server?.Port?.ToString(),
        "apiKey" => server?.ApiKey,
        "extraArgs" => server?.ExtraArgs is { Count: > 0 } args ? string.Join(" ", args) : null,
        "baseUrl" => server?.BaseUrl,
        _ => null
    };

    private static string? GetLlamaValue(PartialLlamaServerSettings? llama, string field) => field switch
    {
        "contextWindowTokens" => llama?.ContextWindowTokens?.ToString(),
        "batchSizeTokens" => llama?.BatchSizeTokens?.ToString(),
        "ubatchSizeTokens" => llama?.UbatchSizeTokens?.ToString(),
        "parallelSlotCount" => llama?.ParallelSlotCount?.ToString(),
        "enableContinuousBatching" => llama?.EnableContinuousBatching?.ToString().ToLowerInvariant(),
        "enableCachePrompt" => llama?.EnableCachePrompt?.ToString().ToLowerInvariant(),
        "enableContextShift" => llama?.EnableContextShift?.ToString().ToLowerInvariant(),
        "gpuLayerCount" => llama?.GpuLayerCount?.ToString(),
        "splitMode" => llama?.SplitMode,
        "kvCacheTypeK" => llama?.KvCacheTypeK,
        "kvCacheTypeV" => llama?.KvCacheTypeV,
        "enableKvOffload" => llama?.EnableKvOffload?.ToString().ToLowerInvariant(),
        "enableFlashAttention" => llama?.EnableFlashAttention?.ToString().ToLowerInvariant(),
        "samplingTemperature" => llama?.SamplingTemperature?.ToString(),
        "topP" => llama?.TopP?.ToString(),
        "topK" => llama?.TopK?.ToString(),
        "minP" => llama?.MinP?.ToString(),
        "repeatPenalty" => llama?.RepeatPenalty?.ToString(),
        "repeatLastNTokens" => llama?.RepeatLastNTokens?.ToString(),
        "presencePenalty" => llama?.PresencePenalty?.ToString(),
        "frequencyPenalty" => llama?.FrequencyPenalty?.ToString(),
        "seed" => llama?.Seed?.ToString(),
        "threadCount" => llama?.ThreadCount?.ToString(),
        "httpThreadCount" => llama?.HttpThreadCount?.ToString(),
        "chatTemplate" => llama?.ChatTemplate,
        "enableJinja" => llama?.EnableJinja?.ToString().ToLowerInvariant(),
        "reasoningFormat" => llama?.ReasoningFormat,
        "modelAlias" => llama?.ModelAlias,
        "logVerbosity" => llama?.LogVerbosity?.ToString(),
        "enableMlock" => llama?.EnableMlock?.ToString().ToLowerInvariant(),
        "enableMmap" => llama?.EnableMmap?.ToString().ToLowerInvariant(),
        "serverTimeoutSeconds" => llama?.ServerTimeoutSeconds?.ToString(),
        _ => null
    };

    private static string? GetJudgeValue(PartialJudgeConfig? judge, string field) => field switch
    {
        "enable" => judge?.Enable?.ToString().ToLowerInvariant(),
        "manage" => judge?.ServerConfig?.Manage?.ToString().ToLowerInvariant(),
        "host" => judge?.ServerConfig?.Host,
        "port" => judge?.ServerConfig?.Port?.ToString(),
        "baseUrl" => judge?.BaseUrl,
        "modelFile" => judge?.ServerConfig?.Model?.FilePath,
        "hfRepo" => judge?.ServerConfig?.Model?.HfRepo,
        "apiKey" => judge?.ServerConfig?.ApiKey,
        "template" => judge?.JudgePromptTemplate,
        "scoreMin" => judge?.ScoreMinValue?.ToString(),
        "scoreMax" => judge?.ScoreMaxValue?.ToString(),
        "executablePath" => judge?.ServerConfig?.ExecutablePath,
        _ => null
    };

    private static string? GetOutputValue(OutputConfig? output, string field) => output is null ? null : field switch
    {
        "writePerEvalJson" => output.WritePerEvalJson ? "true" : "false",
        "writeSummaryJson" => output.WriteSummaryJson ? "true" : "false",
        "writeSummaryCsv" => output.WriteSummaryCsv ? "true" : "false",
        "writeParquet" => output.WriteResultsParquet ? "true" : "false",
        "includeRawResponse" => output.IncludeRawLlmResponse ? "true" : "false",
        _ => null
    };

    private static string? GetRunValue(PartialRunMeta? run, string field) => field switch
    {
        "name" => run?.RunName,
        "outputDirectoryPath" => run?.OutputDirectoryPath,
        "exportShellTarget" => run?.ExportShellTarget?.ToString().ToLowerInvariant(),
        "continueOnEvalFailure" => run?.ContinueOnEvalFailure?.ToString().ToLowerInvariant(),
        "maxConcurrentEvals" => run?.MaxConcurrentEvals?.ToString(),
        _ => null
    };

    private void InitializeSettingsFields()
    {
        // Server Configuration
        AddField("server.manage", "Manage Server", "Server Configuration", "", "Whether to manage llama-server locally", true);
        AddField("server.executablePath", "Server Executable Path", "Server Configuration", "", "Path to llama-server executable (optional)", true);
        AddField("server.host", "Host", "Server Configuration", "", "Server host address", true);
        AddField("server.port", "Port", "Server Configuration", "", "Server port number", true);
        AddField("server.apiKey", "API Key", "Server Configuration", "", "Optional API key for authentication", true);
        AddField("server.extraArgs", "Extra Args", "Server Configuration", "", "Space-separated extra arguments", true);
        AddField("server.baseUrl", "Base URL", "Server Configuration", "", "Base URL for external server", true);

        // Llama Server Settings - Context/Batching (all default to null/empty for llama.cpp defaults)
        AddField("llama.contextWindowTokens", "Context Window", "Llama Server Settings", "", "Context window size in tokens", true);
        AddField("llama.batchSizeTokens", "Batch Size", "Llama Server Settings", "", "Batch size in tokens", true);
        AddField("llama.ubatchSizeTokens", "Micro-Batch Size", "Llama Server Settings", "", "Micro-batch size in tokens", true);
        AddField("llama.parallelSlotCount", "Parallel Slots", "Llama Server Settings", "", "Concurrent request slots (from /props)", true);
        AddField("llama.enableContinuousBatching", "Enable Continuous Batching", "Llama Server Settings", "", "Enable continuous batching", true);
        AddField("llama.enableCachePrompt", "Cache Prompt", "Llama Server Settings", "", "Cache prompt processing", true);
        AddField("llama.enableContextShift", "Enable Context Shift", "Llama Server Settings", "", "Enable context shifting", true);

        // Llama Server Settings - GPU
        AddField("llama.gpuLayerCount", "GPU Layers", "Llama Server Settings", "", "Number of layers to offload to GPU", true);
        AddField("llama.splitMode", "Split Mode", "Llama Server Settings", "", "GPU split mode: none, layer, row", true);
        AddField("llama.kvCacheTypeK", "KV Cache Type K", "Llama Server Settings", "", "KV cache type for K (f16, q8_0, etc.)", true);
        AddField("llama.kvCacheTypeV", "KV Cache Type V", "Llama Server Settings", "", "KV cache type for V (f16, q8_0, etc.)", true);
        AddField("llama.enableKvOffload", "Enable KV Offload", "Llama Server Settings", "", "Offload KV cache to GPU", true);
        AddField("llama.enableFlashAttention", "Enable Flash Attention", "Llama Server Settings", "", "Enable flash attention", true);

        // Llama Server Settings - Sampling (all default to null for llama.cpp defaults)
        AddField("llama.samplingTemperature", "Temperature", "Llama Server Settings", "", "Sampling temperature", true);
        AddField("llama.topP", "Top P", "Llama Server Settings", "", "Top-p (nucleus) sampling", true);
        AddField("llama.topK", "Top K", "Llama Server Settings", "", "Top-k sampling", true);
        AddField("llama.minP", "Min P", "Llama Server Settings", "", "Min-p sampling", true);
        AddField("llama.repeatPenalty", "Repeat Penalty", "Llama Server Settings", "", "Penalty for repeated tokens", true);
        AddField("llama.repeatLastNTokens", "Repeat Last N", "Llama Server Settings", "", "Number of tokens to consider for repeat penalty", true);
        AddField("llama.presencePenalty", "Presence Penalty", "Llama Server Settings", "", "Presence penalty for token generation", true);
        AddField("llama.frequencyPenalty", "Frequency Penalty", "Llama Server Settings", "", "Frequency penalty for token generation", true);
        AddField("llama.seed", "Seed", "Llama Server Settings", "", "Random seed (-1 for random)", true);

        // Llama Server Settings - Threading
        AddField("llama.threadCount", "Threads", "Llama Server Settings", "", "CPU threads for inference", true);
        AddField("llama.httpThreadCount", "HTTP Threads", "Llama Server Settings", "", "HTTP server threads", true);

        // Llama Server Settings - Model Behavior
        AddField("llama.chatTemplate", "Chat Template", "Llama Server Settings", "", "Chat template name", true);
        AddField("llama.enableJinja", "Enable Jinja", "Llama Server Settings", "", "Enable Jinja template processing", true);
        AddField("llama.reasoningFormat", "Reasoning Format", "Llama Server Settings", "", "Reasoning format (e.g., chain-of-thought)", true);
        AddField("llama.modelAlias", "Model Alias", "Llama Server Settings", "", "Model alias for identification", true);

        // Llama Server Settings - Logging
        AddField("llama.logVerbosity", "Log Verbosity", "Llama Server Settings", "", "Log verbosity level (0-3)", true);

        // Llama Server Settings - Memory
        AddField("llama.enableMlock", "Enable Mlock", "Llama Server Settings", "", "Lock model in memory", true);
        AddField("llama.enableMmap", "Enable Mmap", "Llama Server Settings", "", "Memory-map model file", true);

        // Llama Server Settings - Timeouts
        AddField("llama.serverTimeoutSeconds", "Server Timeout", "Llama Server Settings", "", "Server timeout in seconds", true);

        // Output Settings (all default to null/unspecified)
        AddField("output.writePerEvalJson", "Write per-eval JSON", "Output Settings", "", "Write individual JSON for each eval", true);
        AddField("output.writeSummaryJson", "Write summary JSON", "Output Settings", "", "Write summary JSON file", true);
        AddField("output.writeSummaryCsv", "Write summary CSV", "Output Settings", "", "Write summary CSV file", true);
        AddField("output.writeParquet", "Write Parquet", "Output Settings", "", "Write Parquet output file", true);
        AddField("output.includeRawResponse", "Include raw LLM responses", "Output Settings", "", "Include raw LLM responses in output", true);

        // Judge Settings - Basic
        AddField("judge.enable", "Enable LLM-as-Judge", "Judge Settings", "", "Whether to enable LLM-as-judge scoring", true);
        AddField("judge.manage", "Manage Judge Server", "Judge Settings", "", "Whether to manage judge llama-server locally", true);
        AddField("judge.executablePath", "Judge Executable Path", "Judge Settings", "", "Path to judge llama-server executable (optional)", true);
        AddField("judge.host", "Judge Host", "Judge Settings", "127.0.0.1", "Judge server host address", true);
        AddField("judge.port", "Judge Port", "Judge Settings", "8081", "Judge server port number", true);
        AddField("judge.baseUrl", "Judge Server URL", "Judge Settings", "", "Judge LLM server URL (for external server)", true);
        AddField("judge.modelFile", "Judge Model File", "Judge Settings", "", "Judge model file path", true);
        AddField("judge.hfRepo", "Judge HuggingFace Repo", "Judge Settings", "", "Judge HuggingFace repo", true);
        AddField("judge.apiKey", "Judge API Key", "Judge Settings", "", "Judge API key", true);
        AddField("judge.template", "Judge Template", "Judge Settings", "standard", "Judge prompt template");
        AddField("judge.scoreMin", "Min Score", "Judge Settings", "0", "Minimum score value");
        AddField("judge.scoreMax", "Judge Score Max", "Judge Settings", "10", "Maximum score value");

        // Judge Settings - Llama Server (same options as main server)
        AddField("judge.contextWindowTokens", "Judge Context Window", "Judge Settings", "", "Context window size in tokens", true);
        AddField("judge.batchSizeTokens", "Judge Batch Size", "Judge Settings", "", "Batch size in tokens", true);
        AddField("judge.ubatchSizeTokens", "Judge Micro-Batch Size", "Judge Settings", "", "Micro-batch size in tokens", true);
        AddField("judge.parallelSlotCount", "Judge Parallel Slots", "Judge Settings", "", "Concurrent request slots", true);
        AddField("judge.enableContinuousBatching", "Judge Enable Continuous Batching", "Judge Settings", "", "Enable continuous batching", true);
        AddField("judge.enableCachePrompt", "Judge Cache Prompt", "Judge Settings", "", "Cache prompt processing", true);
        AddField("judge.enableContextShift", "Judge Enable Context Shift", "Judge Settings", "", "Enable context shifting", true);
        AddField("judge.gpuLayerCount", "Judge GPU Layers", "Judge Settings", "", "Number of layers to offload to GPU", true);
        AddField("judge.splitMode", "Judge Split Mode", "Judge Settings", "", "GPU split mode: none, layer, row", true);
        AddField("judge.kvCacheTypeK", "Judge KV Cache Type K", "Judge Settings", "", "KV cache type for K (f16, q8_0, etc.)", true);
        AddField("judge.kvCacheTypeV", "Judge KV Cache Type V", "Judge Settings", "", "KV cache type for V (f16, q8_0, etc.)", true);
        AddField("judge.enableKvOffload", "Judge Enable KV Offload", "Judge Settings", "", "Offload KV cache to GPU", true);
        AddField("judge.enableFlashAttention", "Judge Enable Flash Attention", "Judge Settings", "", "Enable flash attention", true);
        AddField("judge.samplingTemperature", "Judge Temperature", "Judge Settings", "", "Sampling temperature", true);
        AddField("judge.topP", "Judge Top P", "Judge Settings", "", "Top-p (nucleus) sampling", true);
        AddField("judge.topK", "Judge Top K", "Judge Settings", "", "Top-k sampling", true);
        AddField("judge.minP", "Judge Min P", "Judge Settings", "", "Min-p sampling", true);
        AddField("judge.repeatPenalty", "Judge Repeat Penalty", "Judge Settings", "", "Penalty for repeated tokens", true);
        AddField("judge.repeatLastNTokens", "Judge Repeat Last N", "Judge Settings", "", "Number of tokens to consider for repeat penalty", true);
        AddField("judge.presencePenalty", "Judge Presence Penalty", "Judge Settings", "", "Presence penalty for token generation", true);
        AddField("judge.frequencyPenalty", "Judge Frequency Penalty", "Judge Settings", "", "Frequency penalty for token generation", true);
        AddField("judge.seed", "Judge Seed", "Judge Settings", "", "Random seed (-1 for random)", true);
        AddField("judge.threadCount", "Judge Threads", "Judge Settings", "", "CPU threads for inference", true);
        AddField("judge.httpThreadCount", "Judge HTTP Threads", "Judge Settings", "", "HTTP server threads", true);
        AddField("judge.chatTemplate", "Judge Chat Template", "Judge Settings", "", "Chat template name", true);
        AddField("judge.enableJinja", "Judge Enable Jinja", "Judge Settings", "", "Enable Jinja template processing", true);
        AddField("judge.reasoningFormat", "Judge Reasoning Format", "Judge Settings", "", "Reasoning format (e.g., chain-of-thought)", true);
        AddField("judge.modelAlias", "Judge Model Alias", "Judge Settings", "", "Model alias for identification", true);
        AddField("judge.logVerbosity", "Judge Log Verbosity", "Judge Settings", "", "Log verbosity level (0-3)", true);
        AddField("judge.enableMlock", "Judge Enable Mlock", "Judge Settings", "", "Lock model in memory", true);
        AddField("judge.enableMmap", "Judge Enable Mmap", "Judge Settings", "", "Memory-map model file", true);
        AddField("judge.serverTimeoutSeconds", "Judge Server Timeout", "Judge Settings", "", "Server timeout in seconds", true);

        // Run Meta Settings
        AddField("run.name", "Run Name", "Run Meta", "", "Human-readable name for this run", true);
        AddField("run.outputDirectoryPath", "Output Directory", "Run Meta", "./results", "Results output directory");
        AddField("run.exportShellTarget", "Shell Dialect", "Run Meta", "bash", "Shell dialect for export: bash, powershell");
        AddField("run.continueOnEvalFailure", "Continue on Failure", "Run Meta", "true", "Continue running on eval failure");
        AddField("run.maxConcurrentEvals", "Max Concurrent Evals", "Run Meta", "", "Maximum concurrent evaluations", true);

        // Data Source Settings
        AddField("dataSource.kind", "Data Source Mode", "Data Source", "Unspecified", "Data source mode: SingleFile or SplitDirectories");
        AddField("dataSource.filePath", "Data File Path", "Data Source", "", "Path to single data file (JSON, YAML, CSV, etc.)", true);
        AddField("dataSource.promptDirectory", "Prompt Directory", "Data Source", "", "Path to prompt files directory", true);
        AddField("dataSource.expectedDirectory", "Expected Output Directory", "Data Source", "", "Path to expected output files directory", true);
    }

    private void AddField(string key, string displayName, string section, string defaultValue, string? description = null, bool isOptional = false)
    {
        SettingsFields.Add(new SettingsFieldViewModel
        {
            Key = key,
            DisplayName = displayName,
            Section = section,
            Value = defaultValue,
            Description = description,
            IsOptional = isOptional,
            SearchText = _searchText
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private bool SetField<T>(ref T fieldVar, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(fieldVar, v)) return false;
        fieldVar = v; OnPropertyChanged(n); return true;
    }
}
