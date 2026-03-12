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
/// </summary>
public record LlamaServerSettings
{
    // Network
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? ApiKey { get; init; }

    // Context / batching
    public int? ContextWindowTokens { get; init; }
    public int? BatchSizeTokens { get; init; }
    public int? UbatchSizeTokens { get; init; }
    public int? ParallelSlotCount { get; init; }
    public bool? EnableContinuousBatching { get; init; }
    public bool? EnableCachePrompt { get; init; }
    public bool? EnableContextShift { get; init; }

    // GPU
    public int? GpuLayerCount { get; init; }
    public string? SplitMode { get; init; }
    public string? KvCacheTypeK { get; init; }
    public string? KvCacheTypeV { get; init; }
    public bool? EnableKvOffload { get; init; }
    public bool? EnableFlashAttention { get; init; }

    // Sampling
    public double? SamplingTemperature { get; init; }
    public double? TopP { get; init; }
    public int? TopK { get; init; }
    public double? MinP { get; init; }
    public double? RepeatPenalty { get; init; }
    public int? RepeatLastNTokens { get; init; }
    public double? PresencePenalty { get; init; }
    public double? FrequencyPenalty { get; init; }
    public int? Seed { get; init; }

    // Threading
    public int? ThreadCount { get; init; }
    public int? HttpThreadCount { get; init; }

    // Model behaviour
    public string? ChatTemplate { get; init; }
    public bool? EnableJinja { get; init; }
    public string? ReasoningFormat { get; init; }
    public string? ModelAlias { get; init; }

    // Logging
    public int? LogVerbosity { get; init; }

    // Memory
    public bool? EnableMlock { get; init; }
    public bool? EnableMmap { get; init; }

    // Timeouts
    public double? ServerTimeoutSeconds { get; init; }

    // Pass-through (advanced)
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];
}

// ---------------------------------------------------------------------------
// DataSourceConfig
// ---------------------------------------------------------------------------

public enum DataSourceKind
{
    Directory,
    SplitDirectories,
    SingleFile,
    JsonFile,
    JsonlFile,
    YamlFile,
    CsvFile,
    ParquetFile,
    InlineList,
    File,        // Alias for SingleFile
    DirectoryPair,  // Alias for SplitDirectories
}

public record FieldMapping
{
    public string? IdField { get; init; }
    public string? UserPromptField { get; init; }
    public string? ExpectedOutputField { get; init; }
    public string? SystemPromptField { get; init; }
}

public record DataSourceConfig
{
    public DataSourceKind Kind { get; init; } = DataSourceKind.Directory;

    /// <summary>Used when Kind = Directory. Path to prompt files.</summary>
    public string? PromptDirectoryPath { get; init; }

    /// <summary>Used when Kind = Directory. Path to expected-output files (optional).</summary>
    public string? ExpectedOutputDirectoryPath { get; init; }

    /// <summary>Path to a single file (JSON, YAML, CSV, Parquet, or raw text).</summary>
    public string? FilePath { get; init; }

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

    /// <summary>Alias for PromptDirectoryPath (for CLI compatibility).</summary>
    public string? PromptDirectory
    {
        get => PromptDirectoryPath;
        init => PromptDirectoryPath = value;
    }

    /// <summary>Alias for ExpectedOutputDirectoryPath (for CLI compatibility).</summary>
    public string? ExpectedDirectory
    {
        get => ExpectedOutputDirectoryPath;
        init => ExpectedOutputDirectoryPath = value;
    }
}

// ---------------------------------------------------------------------------
// PipelineConfig / OutputConfig
// ---------------------------------------------------------------------------

public record PipelineConfig
{
    // Pipeline-specific options are passed via EvalSetConfig.PipelineOptions.
    // This record is intentionally open for future base options.
}

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
// EvalSetConfig
// ---------------------------------------------------------------------------

public record EvalSetConfig
{
    /// <summary>
    /// Unique identifier for this eval set.
    /// When continuing from checkpoint, this MUST match the original run's EvalSetId.
    /// Default: new GUID (for new runs).
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = "";
    public string PipelineName { get; init; } = "";
    public DataSourceConfig DataSource { get; init; } = new();
    public PipelineConfig Pipeline { get; init; } = new();
    public OutputConfig Output { get; init; } = new();

    /// <summary>
    /// Pipeline-specific options forwarded to IBuiltinPipelineFactory.Create().
    /// Keys/values are pipeline-defined.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? PipelineOptions { get; init; }

    /// <summary>Optional endpoint name override. Null = use primary server.</summary>
    public string? EndpointName { get; init; }

    /// <summary>Optional judge configuration for this eval set.</summary>
    public JudgeConfig? Judge { get; init; }
}

// ---------------------------------------------------------------------------
// RunMeta
// ---------------------------------------------------------------------------

public record RunMeta
{
    public string Id { get; init; } = "";
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
    public bool Enable { get; init; }

    /// <summary>
    /// Whether to manage a local llama-server instance for the judge.
    /// When true, a second llama-server process is started with the configured settings.
    /// When false, connects to an existing judge endpoint via BaseUrl.
    /// Only used when Enable = true.
    /// </summary>
    public bool Manage { get; init; }

    /// <summary>
    /// Server configuration for the judge (host, port, model, etc.).
    /// Used when Manage = true to start a local llama-server.
    /// </summary>
    public ServerConfig ServerConfig { get; init; } = new();

    /// <summary>LlamaServerSettings for the judge endpoint (if managed).</summary>
    public LlamaServerSettings? ServerSettings { get; init; }

    /// <summary>
    /// Jinja2-style template for the judge prompt.
    /// Available variables: {prompt}, {expectedOutput}, {actualOutput}, {metadata.*}
    /// Can be a template name ("standard", "pass-fail", "structured-json") or full template content.
    /// </summary>
    public string JudgePromptTemplate { get; init; } = "standard";

    /// <summary>How to parse the judge's response.</summary>
    public JudgeResponseFormat ResponseFormat { get; init; } = JudgeResponseFormat.StructuredJson;

    /// <summary>
    /// For NumericScore/StructuredJson format: the score range expected from the judge.
    /// The raw score is used directly for the judgeScore metric (e.g., 7/10 displays as 7.0).
    /// </summary>
    public double ScoreMinValue { get; init; } = 0.0;
    public double ScoreMaxValue { get; init; } = 10.0;

    /// <summary>System prompt for the judge LLM.</summary>
    public string? JudgeSystemPrompt { get; init; }

    /// <summary>Max tokens the judge is allowed to generate.</summary>
    public int JudgeMaxTokenCount { get; init; } = 512;

    /// <summary>Temperature for judge responses. Lower = more deterministic scoring.</summary>
    public double JudgeSamplingTemperature { get; init; } = 0.0;

    /// <summary>Base URL for the judge endpoint. Used when Manage = false.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Sampling temperature for judge. Alias for JudgeSamplingTemperature.</summary>
    public double? SamplingTemperature { get; init; }

    /// <summary>Alias for BaseUrl (for CLI compatibility).</summary>
    public string? JudgeUrl
    {
        get => BaseUrl;
        init => BaseUrl = value;
    }

    /// <summary>Alias for ScoreMinValue (for CLI compatibility).</summary>
    public double? ScoreMin
    {
        get => ScoreMinValue;
        init { if (value.HasValue) ScoreMinValue = value.Value; }
    }

    /// <summary>Alias for ScoreMaxValue (for CLI compatibility).</summary>
    public double? ScoreMax
    {
        get => ScoreMaxValue;
        init { if (value.HasValue) ScoreMaxValue = value.Value; }
    }
}

// ---------------------------------------------------------------------------
// Server stub — full type lives in Seevalocal.Server
// Reproduced here minimally so Config has no project reference to Server.
// The real ServerConfig is in Seevalocal.Server.Models; this mirrors it.
// ---------------------------------------------------------------------------

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
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? ApiKey { get; init; }
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];
    public string? BaseUrl { get; init; }
}

// ---------------------------------------------------------------------------
// ResolvedConfig (fully-populated, immutable)
// ---------------------------------------------------------------------------

public record ResolvedConfig
{
    public RunMeta Run { get; init; } = new();
    public ServerConfig Server { get; init; } = new();
    public LlamaServerSettings LlamaServer { get; init; } = new();
    public IReadOnlyList<EvalSetConfig> EvalSets { get; init; } = [];
    public JudgeConfig? Judge { get; init; }
    public DataSourceConfig DataSource { get; init; } = new();
}

// ---------------------------------------------------------------------------
// PartialConfig — structurally mirrors ResolvedConfig but every leaf nullable
// ---------------------------------------------------------------------------

public record PartialRunMeta
{
    public string? RunName { get; init; }
    public string? OutputDirectoryPath { get; init; }
    public ShellTarget? ExportShellTarget { get; init; }
    public bool? ContinueOnEvalFailure { get; init; }
    public bool? ContinueFromCheckpoint { get; init; }
    public string? CheckpointDatabasePath { get; init; }
    public int? MaxConcurrentEvals { get; init; }
    public double? TimeoutSeconds { get; init; }
    public int? RetryCount { get; init; }
}

public record PartialLlamaServerSettings
{
    public int? ContextWindowTokens { get; init; }
    public int? BatchSizeTokens { get; init; }
    public int? UbatchSizeTokens { get; init; }
    public int? ParallelSlotCount { get; init; }
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
    public int? HttpThreadCount { get; init; }
    public string? ChatTemplate { get; init; }
    public bool? EnableJinja { get; init; }
    public string? ReasoningFormat { get; init; }
    public string? ModelAlias { get; init; }
    public int? LogVerbosity { get; init; }
    public bool? EnableMlock { get; init; }
    public bool? EnableMmap { get; init; }
    public double? ServerTimeoutSeconds { get; init; }
    public List<string>? ExtraArgs { get; init; }
}

public record PartialServerConfig
{
    public bool? Manage { get; init; }
    public string? ExecutablePath { get; init; }
    public ModelSource? Model { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? ApiKey { get; init; }
    public List<string>? ExtraArgs { get; init; }
    public string? BaseUrl { get; init; }
}

public record PartialConfig
{
    public PartialRunMeta? Run { get; init; }
    public PartialServerConfig? Server { get; init; }
    public PartialLlamaServerSettings? LlamaServer { get; init; }
    public List<EvalSetConfig>? EvalSets { get; init; }
    public PartialJudgeConfig? Judge { get; init; }
    public OutputConfig? Output { get; init; }
    public PartialDataSourceConfig? DataSource { get; init; }

    /// <summary>Alias for LlamaServer (for CLI compatibility).</summary>
    public PartialLlamaServerSettings? LlamaSettings
    {
        get => LlamaServer;
        init => LlamaServer = value;
    }
}

/// <summary>
/// Partial data source configuration — mirrors DataSourceConfig but all fields nullable.
/// </summary>
public record PartialDataSourceConfig
{
    public DataSourceKind? Kind { get; init; }
    public string? FilePath { get; init; }
    public string? PromptDirectoryPath { get; init; }
    public string? ExpectedOutputDirectoryPath { get; init; }

    /// <summary>Alias for PromptDirectoryPath (for CLI compatibility).</summary>
    public string? PromptDirectory
    {
        get => PromptDirectoryPath;
        init => PromptDirectoryPath = value;
    }

    /// <summary>Alias for ExpectedOutputDirectoryPath (for CLI compatibility).</summary>
    public string? ExpectedDirectory
    {
        get => ExpectedOutputDirectoryPath;
        init => ExpectedOutputDirectoryPath = value;
    }
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
    public double? JudgeSamplingTemperature { get; init; }
    public string? BaseUrl { get; init; }
    public double? SamplingTemperature { get; init; }

    /// <summary>Alias for BaseUrl (for CLI compatibility).</summary>
    public string? JudgeUrl
    {
        get => BaseUrl;
        init => BaseUrl = value;
    }

    /// <summary>Alias for ScoreMinValue (for CLI compatibility).</summary>
    public double? ScoreMin
    {
        get => ScoreMinValue;
        init { if (value.HasValue) ScoreMinValue = value.Value; }
    }

    /// <summary>Alias for ScoreMaxValue (for CLI compatibility).</summary>
    public double? ScoreMax
    {
        get => ScoreMaxValue;
        init { if (value.HasValue) ScoreMaxValue = value.Value; }
    }
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
