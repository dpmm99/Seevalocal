namespace Seevalocal.Core.Models;

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

public enum ShellTarget { Bash, PowerShell }

public enum GpuKind { Cuda, Vulkan, Metal, CpuOnly }

// ---------------------------------------------------------------------------
// Shared sub-records (also used by Server – stubs here, real types in .Server)
// ---------------------------------------------------------------------------

/// <summary>
/// All llama-server tuning knobs. All fields are nullable — null means omit
/// from CLI args and let llama-server use its own default.
/// Network/API settings (Host, Port, ApiKey, ExtraArgs) are in ServerConfig.
/// </summary>
public record LlamaServerSettings
{
    // Context / batching
    [LlamaSetting(LlamaSettingType.Int, "contextWindowTokens", "Context Window", "-c", "Context window size in tokens")]
    public int? ContextWindowTokens { get; init; }
    [LlamaSetting(LlamaSettingType.Int, "batchSizeTokens", "Batch Size", "-b", "Batch size in tokens")]
    public int? BatchSizeTokens { get; init; }
    [LlamaSetting(LlamaSettingType.Int, "ubatchSizeTokens", "Micro-Batch Size", "-ub", "Micro-batch size in tokens")]
    public int? UbatchSizeTokens { get; init; }
    [LlamaSetting(LlamaSettingType.Int, "parallelSlotCount", "Parallel Slots", "-np", "Concurrent request slots")]
    public int? ParallelSlotCount { get; init; }
    [LlamaSetting(LlamaSettingType.BoolLong, "enableContinuousBatching", "Enable Continuous Batching", "--cont-batching", "--no-cont-batching", "Enable continuous batching")]
    public bool? EnableContinuousBatching { get; init; }
    [LlamaSetting(LlamaSettingType.BoolLong, "enableCachePrompt", "Enable Cache Prompt", "--cache-prompt", "--no-cache-prompt", "Cache prompt processing")]
    public bool? EnableCachePrompt { get; init; }
    [LlamaSetting(LlamaSettingType.BoolLong, "enableContextShift", "Enable Context Shift", "--context-shift", "--no-context-shift", "Enable context shifting")]
    public bool? EnableContextShift { get; init; }

    // GPU
    [LlamaSetting(LlamaSettingType.Int, "gpuLayerCount", "GPU Layers", "-ngl", "Number of layers to offload to GPU")]
    public int? GpuLayerCount { get; init; }
    [LlamaSetting(LlamaSettingType.String, "splitMode", "Split Mode", "--split-mode", "GPU split mode: none, layer, row")]
    public string? SplitMode { get; init; }
    [LlamaSetting(LlamaSettingType.String, "kvCacheTypeK", "KV Cache Type K", "-ctk", "KV cache type for K (f16, q8_0, etc.)")]
    public string? KvCacheTypeK { get; init; }
    [LlamaSetting(LlamaSettingType.String, "kvCacheTypeV", "KV Cache Type V", "-ctv", "KV cache type for V (f16, q8_0, etc.)")]
    public string? KvCacheTypeV { get; init; }
    [LlamaSetting(LlamaSettingType.BoolLong, "enableKvOffload", "Enable KV Offload", "--kv-offload", "--no-kv-offload", "Offload KV cache to GPU")]
    public bool? EnableKvOffload { get; init; }
    [LlamaSetting(LlamaSettingType.Bool, "enableFlashAttention", "Flash Attention", "-fa", "Enable flash attention")]
    public bool? EnableFlashAttention { get; init; }

    // Sampling
    [LlamaSetting(LlamaSettingType.Double, "samplingTemperature", "Temperature", "--temp", "Sampling temperature")]
    public double? SamplingTemperature { get; init; }
    [LlamaSetting(LlamaSettingType.Double, "topP", "Top P", "--top-p", "Top-p (nucleus) sampling")]
    public double? TopP { get; init; }
    [LlamaSetting(LlamaSettingType.Int, "topK", "Top K", "--top-k", "Top-k sampling")]
    public int? TopK { get; init; }
    [LlamaSetting(LlamaSettingType.Double, "minP", "Min P", "--min-p", "Min-p sampling")]
    public double? MinP { get; init; }
    [LlamaSetting(LlamaSettingType.Double, "repeatPenalty", "Repeat Penalty", "--repeat-penalty", "Penalty for repeated tokens")]
    public double? RepeatPenalty { get; init; }
    [LlamaSetting(LlamaSettingType.Int, "repeatLastNTokens", "Repeat Last N", "--repeat-last-n", "Number of tokens to consider for repeat penalty")]
    public int? RepeatLastNTokens { get; init; }
    [LlamaSetting(LlamaSettingType.Double, "presencePenalty", "Presence Penalty", "--presence-penalty", "Presence penalty for token generation")]
    public double? PresencePenalty { get; init; }
    [LlamaSetting(LlamaSettingType.Double, "frequencyPenalty", "Frequency Penalty", "--frequency-penalty", "Frequency penalty for token generation")]
    public double? FrequencyPenalty { get; init; }
    [LlamaSetting(LlamaSettingType.Int, "seed", "Seed", "--seed", "Random seed (-1 for random)")]
    public int? Seed { get; init; }

    // Threading
    [LlamaSetting(LlamaSettingType.Int, "threadCount", "Threads", "-t", "CPU threads for inference")]
    public int? ThreadCount { get; init; }

    // Model behaviour
    [LlamaSetting(LlamaSettingType.String, "chatTemplate", "Chat Template", "--chat-template", "Chat template name")]
    public string? ChatTemplate { get; init; }
    [LlamaSetting(LlamaSettingType.BoolLong, "enableJinja", "Enable Jinja", "--jinja", "--no-jinja", "Enable Jinja template processing")]
    public bool? EnableJinja { get; init; }
    [LlamaSetting(LlamaSettingType.String, "reasoningFormat", "Reasoning Format", "--reasoning-format", "Reasoning format (e.g., chain-of-thought)")]
    public string? ReasoningFormat { get; init; }
    [LlamaSetting(LlamaSettingType.String, "modelAlias", "Model Alias", "--model-alias", "Model alias for identification")]
    public string? ModelAlias { get; init; }
    [LlamaSetting(LlamaSettingType.Int, "reasoningBudget", "Reasoning Budget", "--reasoning-budget", "Reasoning budget in tokens")]
    public int? ReasoningBudget { get; init; }
    [LlamaSetting(LlamaSettingType.String, "reasoningBudgetMessage", "Reasoning Budget Message", "--reasoning-budget-message", "Message to control reasoning behavior")]
    public string? ReasoningBudgetMessage { get; init; }

    // Logging
    [LlamaSetting(LlamaSettingType.Int, "logVerbosity", "Log Verbosity", "-lv", "Log verbosity level (0-3)")]
    public int? LogVerbosity { get; init; }

    // Memory
    [LlamaSetting(LlamaSettingType.BoolLong, "enableMlock", "Enable Mlock", "--mlock", "--no-mlock", "Lock model in memory")]
    public bool? EnableMlock { get; init; }
    [LlamaSetting(LlamaSettingType.BoolLong, "enableMmap", "Enable Mmap", "--mmap", "--no-mmap", "Memory-map model file")]
    public bool? EnableMmap { get; init; }

    // Timeouts
    [LlamaSetting(LlamaSettingType.Double, "serverTimeoutSeconds", "Server Timeout", "--timeout", "Server timeout in seconds")]
    public double? ServerTimeoutSeconds { get; init; }

    // Pass-through (advanced) - no CLI flag, just passes through
    [LlamaSetting(LlamaSettingType.String, "extraArgs", "Extra Args", "Space-separated extra llama-server arguments")]
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];
}

// ---------------------------------------------------------------------------
// DataSourceConfig
// ---------------------------------------------------------------------------

public enum DataSourceKind
{
    SplitDirectories,
    SingleFile,
    JsonFile,
    JsonlFile,
    YamlFile,
    CsvFile,
    ParquetFile,
    InlineList,
}

public record FieldMapping
{
    public string? IdField { get; init; }
    public string? UserPromptField { get; init; }
    public string? ExpectedOutputField { get; init; }
    public string? SystemPromptField { get; init; }
    public string? SourceLanguageField { get; init; }
    public string? TargetLanguageField { get; init; }
}

public record DataSourceConfig
{
    [MergeDefault(DataSourceKind.SplitDirectories)]
    public DataSourceKind Kind { get; init; } = DataSourceKind.SplitDirectories;

    /// <summary>Path to a single file (JSON, YAML, CSV, Parquet, or raw text).</summary>
    public string? FilePath { get; init; }

    /// <summary>Path to prompt directory (for directory-based data sources).</summary>
    public string? PromptDirectory { get; init; }

    /// <summary>Path to expected output directory (optional, for directory-based data sources).</summary>
    public string? ExpectedDirectory { get; init; }

    /// <summary>Optional default system prompt applied to all items from this source.</summary>
    public string? DefaultSystemPrompt { get; init; }

    /// <summary>Path to a file whose contents become the default system prompt.</summary>
    public string? DefaultSystemPromptFilePath { get; init; }

    /// <summary>Column/field mapping for structured sources.</summary>
    public FieldMapping FieldMapping { get; init; } = new();

    /// <summary>Glob pattern filter for directory sources. Default: *</summary>
    public string FilePattern { get; init; } = "*";

    /// <summary>File extension filter for directory sources. Default: null (all extensions).</summary>
    public string? FileExtensionFilter { get; init; }
}

// ---------------------------------------------------------------------------
// PipelineConfig / OutputConfig
// ---------------------------------------------------------------------------

public record OutputConfig
{
    public bool WritePerEvalJson { get; init; } = true;
    public bool WriteSummaryJson { get; init; } = true;
    public bool WriteSummaryCsv { get; init; } = true;
    public bool WriteResultsParquet { get; init; } = false;
    public bool IncludeRawLlmResponse { get; init; } = true;
    public bool IncludeAllStageOutputs { get; init; } = false;

    /// <summary>Output directory path. Alias for compatibility.</summary>
    public string? OutputDir { get; init; }

    /// <summary>Shell target for export scripts. Alias for compatibility.</summary>
    public ShellTarget? ShellTarget { get; init; }
}

// ---------------------------------------------------------------------------
// RunMeta
// ---------------------------------------------------------------------------

public record RunMeta
{
    public string Id { get; init; } = "";
    public string PipelineName { get; init; } = "CasualQA";
    public string? RunName { get; init; }
    public string? OutputDirectoryPath { get; init; }
    public ShellTarget? ExportShellTarget { get; init; }
    public bool? ContinueOnEvalFailure { get; init; }
    public bool ContinueFromCheckpoint { get; init; }
    public string? CheckpointDatabasePath { get; init; }

    /// <summary>null = use total_slots from server /props response.</summary>
    public int? MaxConcurrentEvals { get; init; }
}

// ---------------------------------------------------------------------------
// JudgeConfig
// ---------------------------------------------------------------------------

/// <summary>
/// Configuration for LLM-as-judge scoring.
/// Supports both managed (local llama-server) and external judge endpoints.
/// </summary>
public record JudgeConfig
{
    /// <summary>
    /// Whether to enable LLM-as-judge scoring.
    /// When false, judge scoring is completely disabled.
    /// When true, uses either managed or external judge endpoint.
    /// </summary>
    [MergeDefault(false)]
    public bool Enable { get; init; }

    /// <summary>
    /// Server configuration for the judge (host, port, model, etc.).
    /// Used when Manage = true to start a local llama-server.
    /// </summary>
    public ServerConfig ServerConfig { get; init; } = new();

    /// <summary>LlamaServerSettings for the judge endpoint (if managed).</summary>
    public LlamaServerSettings? ServerSettings { get; init; }

    /// <summary>
    /// Interpolated string-style template for the judge prompt.
    /// Available variables: {prompt}, {expectedOutput}, {actualOutput}, {metadata.*}
    /// Can be a template name ("standard", "pass-fail", "structured-json") or full template content.
    /// </summary>
    [MergeDefault("standard")]
    public string JudgePromptTemplate { get; init; } = "standard";

    /// <summary>How to parse the judge's response.</summary>
    [MergeDefault(JudgeResponseFormat.StructuredJson)]
    public JudgeResponseFormat ResponseFormat { get; init; } = JudgeResponseFormat.StructuredJson;

    /// <summary>
    /// For NumericScore/StructuredJson format: the score range expected from the judge.
    /// The raw score is used directly for the judgeScore metric (e.g., 7/10 displays as 7.0).
    /// </summary>
    [MergeDefault(0.0)]
    public double ScoreMinValue { get; init; } = 0.0;
    [MergeDefault(10.0)]
    public double ScoreMaxValue { get; init; } = 10.0;

    /// <summary>System prompt for the judge LLM.</summary>
    public string? JudgeSystemPrompt { get; init; }

    /// <summary>Max tokens the judge is allowed to generate.</summary>
    public int? JudgeMaxTokenCount { get; init; }
}

public enum ModelSourceKind { LocalFile, HuggingFace }

public record ModelSource
{
    public ModelSourceKind Kind { get; init; }
    public string? FilePath { get; init; }
    public string? HfRepo { get; init; }
    public string? HfQuant { get; init; }
    public string? HfToken { get; init; }
}

public record ServerConfig
{
    public bool? Manage { get; init; }
    public string? ExecutablePath { get; init; }
    public ModelSource? Model { get; init; }
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
}

public record ResolvedConfig
{
    public RunMeta Run { get; init; } = new();
    public ServerConfig Server { get; init; } = new();
    public LlamaServerSettings LlamaServer { get; init; } = new();
    public JudgeConfig? Judge { get; init; }
    public DataSourceConfig DataSource { get; init; } = new();
    public Dictionary<string, object?> PipelineOptions { get; init; } = [];
}

/// <summary>
/// Resembles ResolvedConfig, but everything is nullable. For layering settings on top of each other (settings files, settings view, wizard).
/// </summary>
public record PartialRunMeta
{
    public string? PipelineName { get; init; }
    public string? RunName { get; init; }
    public string? OutputDirectoryPath { get; init; }
    public ShellTarget? ExportShellTarget { get; init; }
    public bool? ContinueOnEvalFailure { get; init; }
    public bool? ContinueFromCheckpoint { get; init; }
    public string? CheckpointDatabasePath { get; init; }
    public int? MaxConcurrentEvals { get; init; }
}

public record PartialLlamaServerSettings
{
    public int? ContextWindowTokens { get; init; }
    public int? ParallelSlotCount { get; init; }
    public int? BatchSizeTokens { get; init; }
    public int? UbatchSizeTokens { get; init; }
    public bool? EnableContinuousBatching { get; init; }
    public bool? EnableCachePrompt { get; init; }
    public bool? EnableContextShift { get; init; }
    public int? GpuLayerCount { get; init; }
    public string? SplitMode { get; init; }
    public string? KvCacheTypeK { get; init; }
    public string? KvCacheTypeV { get; init; }
    public bool? EnableKvOffload { get; init; }
    public bool? EnableFlashAttention { get; init; }
    public double? SamplingTemperature { get; init; }
    public double? TopP { get; init; }
    public int? TopK { get; init; }
    public double? MinP { get; init; }
    public double? RepeatPenalty { get; init; }
    public int? RepeatLastNTokens { get; init; }
    public double? PresencePenalty { get; init; }
    public double? FrequencyPenalty { get; init; }
    public int? Seed { get; init; }
    public int? ThreadCount { get; init; }
    public string? ChatTemplate { get; init; }
    public bool? EnableJinja { get; init; }
    public string? ReasoningFormat { get; init; }
    public string? ModelAlias { get; init; }
    public int? ReasoningBudget { get; init; }
    public string? ReasoningBudgetMessage { get; init; }
    public int? LogVerbosity { get; init; }
    public bool? EnableMlock { get; init; }
    public bool? EnableMmap { get; init; }
    public double? ServerTimeoutSeconds { get; init; }
    public List<string>? ExtraArgs { get; init; }
}

public record PartialServerConfig
{
    public bool? Manage { get; init; }
    //TODO: add managed server retry count: max number of times to restart llama-server if it crashes. The count should reset if a request succeeds.
    public string? ExecutablePath { get; init; }
    public ModelSource? Model { get; init; }
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
}

public record PartialConfig
{
    public PartialRunMeta? Run { get; init; }
    public PartialServerConfig? Server { get; init; }
    public PartialLlamaServerSettings? LlamaSettings { get; init; }
    public PartialJudgeConfig? Judge { get; init; }
    public OutputConfig? Output { get; init; } //TODO: no partial OutputConfig exists, and ResolvedConfig doesn't have Output, and so none of those values are hooked up to the settings anymore
    public PartialDataSourceConfig? DataSource { get; init; }
    public Dictionary<string, object?>? PipelineOptions { get; init; }
}

/// <summary>
/// Partial data source configuration — mirrors DataSourceConfig but all fields nullable.
/// </summary>
public record PartialDataSourceConfig
{
    public DataSourceKind? Kind { get; init; }
    public string? FilePath { get; init; }
    public string? PromptDirectory { get; init; }
    public string? ExpectedDirectory { get; init; }
    public string? DefaultSystemPrompt { get; init; }
    public string? DefaultSystemPromptFilePath { get; init; }
    public FieldMapping? FieldMapping { get; init; }
    //TODO: Missing FilePattern and FileExtensionFilter; I probably don't need both, but at least the FilePattern one may be beneficial for SplitDirectories mode.
}

// ---------------------------------------------------------------------------
// PartialJudgeConfig — mirrors JudgeConfig but all fields nullable
// ---------------------------------------------------------------------------

public record PartialJudgeConfig
{
    public bool? Enable { get; init; }
    public PartialServerConfig? ServerConfig { get; init; }
    public PartialLlamaServerSettings? ServerSettings { get; init; }
    public string? JudgePromptTemplate { get; init; }
    public JudgeResponseFormat? ResponseFormat { get; init; }
    public double? ScoreMinValue { get; init; }
    public double? ScoreMaxValue { get; init; }
    public string? JudgeSystemPrompt { get; init; }
    public int? JudgeMaxTokenCount { get; init; }
}

// ---------------------------------------------------------------------------
// ValidationError
// ---------------------------------------------------------------------------

public record ValidationError(string Field, string MessageText);

// ---------------------------------------------------------------------------
// RunConfig - Alias for RunMeta (for test compatibility)
// ---------------------------------------------------------------------------

public record RunConfig : RunMeta
{
    // Inherits all properties from RunMeta
    // This is a type alias for test compatibility
}