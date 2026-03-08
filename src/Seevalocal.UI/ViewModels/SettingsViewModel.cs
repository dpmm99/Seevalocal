using Seevalocal.Core.Models;
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
    private bool _isLlamaCppDefault;

    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? Description { get; init; }
    public string? Value { get; set; }
    public string? Watermark { get; init; }
    public bool IsOptional { get; init; }
    public string Section { get; init; } = "";

    /// <summary>
    /// The final materialized value after merging all configuration layers.
    /// </summary>
    public string? MaterializedValue
    {
        get => _materializedValue;
        set => SetField(ref _materializedValue, value);
    }

    /// <summary>
    /// The origin of the materialized value (e.g., file path, "CLI", "Default").
    /// </summary>
    public string? Origin
    {
        get => _origin;
        set => SetField(ref _origin, value);
    }

    /// <summary>
    /// True if the materialized value is null, meaning llama-server will use its own default.
    /// </summary>
    public bool IsLlamaCppDefault
    {
        get => _isLlamaCppDefault;
        set => SetField(ref _isLlamaCppDefault, value);
    }

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
                               Key.Contains("LogVerbosity", StringComparison.OrdinalIgnoreCase);

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
    /// Gets the enum options for enum fields.
    /// </summary>
    public string[]? EnumOptions => Key switch
    {
        "judge.template" or "judgeTemplate" => ["Unspecified", "standard", "pass-fail", "json"],
        "run.exportShellTarget" or "ShellTarget" => ["Unspecified", "bash", "powershell"],
        "llama.splitMode" or "SplitMode" => ["Unspecified", "none", "layer", "row"],
        "llama.reasoningFormat" or "ReasoningFormat" => ["Unspecified", "deepseek", "r1", "none"],
        "llama.logVerbosity" or "LogVerbosity" => ["Unspecified", "0", "1", "2", "3"],
        _ => null
    };

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

        // Subscribe to property changes in child fields to notify when anything changes
        foreach (var field in _fields)
        {
            field.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(Fields));
                OnPropertyChanged(nameof(VisibleFieldCount));
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

    public ObservableCollection<SettingsFieldViewModel> SettingsFields { get; } = [];

    /// <summary>
    /// Settings fields grouped by section for display.
    /// </summary>
    public IEnumerable<SettingsSectionGroup> SettingsFieldsBySection =>
        SettingsFields
            .GroupBy(f => f.Section)
            .Select(g => new SettingsSectionGroup(g.Key, g))
            .Where(g => g.Fields.Any());

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
                // Notify that the grouped fields have changed
                OnPropertyChanged(nameof(SettingsFieldsBySection));
            }
        }
    }

    public SettingsViewModel()
    {
        InitializeSettingsFields();
        BrowseFileCommand = new RelayCommand<string>(BrowseFile);
        BrowseFolderCommand = new RelayCommand<string>(BrowseFolder);
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
            BrowseRequested?.Invoke(this, new BrowseEventArgs
            {
                FieldKey = fieldKey,
                IsFolder = false,
                Filter = "All Files|*.*"
            });
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

    /// <summary>
    /// Adds a configuration layer and recalculates materialized values.
    /// </summary>
    public void AddConfigLayer(string origin, PartialConfig config)
    {
        _configLayers.Add((origin, config));
        RecalculateMaterializedValues();
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
            var (value, origin) = FindMaterializedValue(field.Key);
            field.MaterializedValue = value;
            field.Origin = origin;
            field.IsLlamaCppDefault = value is null && IsLlamaServerSetting(field.Key);
        }

        // Notify that the grouped fields have changed
        OnPropertyChanged(nameof(SettingsFieldsBySection));
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
            _ => null
        };
    }

    private static string? GetServerValue(PartialServerConfig? server, string field) => field switch
    {
        "host" => server?.Host,
        "port" => server?.Port?.ToString(),
        "apiKey" => server?.ApiKey,
        "extraArgs" => server?.ExtraArgs is { Count: > 0 } args ? string.Join(" ", args) : null,
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
        "manage" => judge?.Manage?.ToString().ToLowerInvariant(),
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

    /// <summary>
    /// Checks if a key belongs to llama server settings.
    /// </summary>
    private static bool IsLlamaServerSetting(string key) => key.StartsWith("llama.", StringComparison.Ordinal);

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

        // Judge Settings
        AddField("judge.manage", "Manage Judge Server", "Judge Settings", "", "Whether to manage judge llama-server locally", true);
        AddField("judge.executablePath", "Judge Executable Path", "Judge Settings", "", "Path to judge llama-server executable (optional)", true);
        AddField("judge.baseUrl", "Judge Server URL", "Judge Settings", "", "Judge LLM server URL", true);
        AddField("judge.modelFile", "Judge Model File", "Judge Settings", "", "Judge model file path", true);
        AddField("judge.hfRepo", "Judge HuggingFace Repo", "Judge Settings", "", "Judge HuggingFace repo", true);
        AddField("judge.apiKey", "Judge API Key", "Judge Settings", "", "Judge API key", true);
        AddField("judge.template", "Judge Template", "Judge Settings", "standard", "Judge prompt template");
        AddField("judge.scoreMin", "Min Score", "Judge Settings", "0", "Minimum score value");
        AddField("judge.scoreMax", "Judge Score Max", "Judge Settings", "10", "Maximum score value");

        // Run Meta Settings
        AddField("run.name", "Run Name", "Run Meta", "", "Human-readable name for this run", true);
        AddField("run.outputDirectoryPath", "Output Directory", "Run Meta", "./results", "Results output directory");
        AddField("run.exportShellTarget", "Shell Dialect", "Run Meta", "bash", "Shell dialect for export: bash, powershell");
        AddField("run.continueOnEvalFailure", "Continue on Failure", "Run Meta", "true", "Continue running on eval failure");
        AddField("run.maxConcurrentEvals", "Max Concurrent Evals", "Run Meta", "", "Maximum concurrent evaluations", true);
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
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private bool SetField<T>(ref T fieldVar, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(fieldVar, v)) return false;
        fieldVar = v; OnPropertyChanged(n); return true;
    }
}
