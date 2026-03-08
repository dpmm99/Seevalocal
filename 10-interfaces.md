# Seevalocal — Part 9: Cross-Component Interfaces (Authoritative)

> **Read `00-conventions.md` before this file.**  
> This file is the **single source of truth** for all interfaces and shared records that cross project boundaries.  
> Every agent implementing any part of this system must consult this file for types they consume from other parts.  
> Do not redefine these types locally. Reference the namespace below directly.

---

## Namespace Map

| Namespace | Project | Contents |
|---|---|---|
| `Seevalocal.Core.Abstractions` | `Seevalocal.Core` | `IEvalStage`, `EvalStageContext`, `StageResult`, `EvalResult`, `EvalProgress`, `IResultCollector` |
| `Seevalocal.Core.Models` | `Seevalocal.Core` | `EvalItem`, `MetricValue`, `MetricScalar`, `MetricType` |
| `Seevalocal.Config.Models` | `Seevalocal.Config` | `ResolvedConfig`, `PartialConfig`, `LlamaServerSettings`, `EvalSetConfig`, `DataSourceConfig`, `FieldMapping`, `PipelineConfig`, `OutputConfig`, `RunMeta`, `JudgeConfig`, `ShellTarget`, `ValidationError` |
| `Seevalocal.Server.Models` | `Seevalocal.Server` | `ServerConfig`, `ModelSource`, `ModelSourceKind`, `ServerInfo`, `ServerProps`, `ChatCompletionRequest`, `ChatCompletionResponse`, `ChatMessage`, `ChatUsage`, `ChatTimings`, `GpuKind` |
| `Seevalocal.Server.Client` | `Seevalocal.Server` | `LlamaServerClient`, `LlamaServerManager` |
| `Seevalocal.DataSources` | `Seevalocal.DataSources` | `IDataSource`, `DataSourceFactory`, `DataSourceKind` |
| `Seevalocal.Metrics.Models` | `Seevalocal.Metrics` | `RunSummary`, `MetricSummary`, `MetricAggregator` |
| `Seevalocal.Metrics.Writers` | `Seevalocal.Metrics` | `IResultWriter`, `CompositeResultWriter` |
| `Seevalocal.Judge` | `Seevalocal.Judge` | `JudgeStage`, `JudgeResponseFormat`, `DefaultTemplates`, `ParsedJudgeResponse` |
| `Seevalocal.Pipelines` | `Seevalocal.Pipelines` | `IBuiltinPipelineFactory`, `EvalPipeline`, `PipelineOrchestrator` |

---

## 1. Core Abstractions

### 1.1 IEvalStage

```csharp
namespace Seevalocal.Core.Abstractions;

/// <summary>
/// A single step in an evaluation pipeline. Implementations must be thread-safe.
/// A single instance is shared across all concurrently running eval items.
/// </summary>
public interface IEvalStage
{
    /// <summary>
    /// Unique name within a pipeline. PascalCase. Convention: ends with "Stage".
    /// Used as the prefix for this stage's outputs in StageOutputs.
    /// </summary>
    string StageName { get; }

    /// <summary>
    /// Execute this stage for the given item.
    /// MUST NOT throw for expected failures. Return a failed StageResult instead.
    /// MAY throw only for programming errors (ArgumentNullException, etc.).
    /// </summary>
    Task<StageResult> ExecuteAsync(EvalStageContext context);
}
```

### 1.2 EvalStageContext

```csharp
namespace Seevalocal.Core.Abstractions;

public record EvalStageContext
{
    /// <summary>The eval item being processed in this invocation.</summary>
    public required EvalItem Item { get; init; }

    /// <summary>
    /// Outputs from all stages that ran before this one in the pipeline.
    /// Key format: "{StageName}.{outputKey}", e.g., "PromptStage.response".
    /// </summary>
    public IReadOnlyDictionary<string, object?> StageOutputs { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>The fully resolved run configuration.</summary>
    public required ResolvedConfig Config { get; init; }

    /// <summary>HTTP client for the primary LLM endpoint. Never null.</summary>
    public required LlamaServerClient PrimaryClient { get; init; }

    /// <summary>HTTP client for the judge LLM endpoint. Null if no judge is configured.</summary>
    public LlamaServerClient? JudgeClient { get; init; }

    /// <summary>Cancellation token. All async calls must respect this.</summary>
    public CancellationToken CancellationToken { get; init; }
}
```

### 1.3 StageResult

```csharp
namespace Seevalocal.Core.Abstractions;

public record StageResult
{
    /// <summary>
    /// Named outputs produced by this stage.
    /// Keys must follow the format "{StageName}.{outputKey}".
    /// Values can be any serializable type; string and numeric types are preferred.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Outputs { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>Typed metrics emitted by this stage. Names must include unit suffix.</summary>
    public IReadOnlyList<MetricValue> Metrics { get; init; } = [];

    /// <summary>
    /// True if the stage completed successfully.
    /// A false value stops subsequent stages for this item (unless pipeline is configured otherwise).
    /// </summary>
    public bool Succeeded { get; init; } = true;

    /// <summary>Human-readable failure reason. Null on success.</summary>
    public string? FailureReason { get; init; }

    public static StageResult Success(
        IReadOnlyDictionary<string, object?> outputs,
        IReadOnlyList<MetricValue> metrics) =>
        new() { Outputs = outputs, Metrics = metrics, Succeeded = true };

    public static StageResult Failure(string reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
```

### 1.4 EvalResult

```csharp
namespace Seevalocal.Core.Abstractions;

public record EvalResult
{
    public required string EvalItemId { get; init; }
    public required string EvalSetId { get; init; }
    public bool Succeeded { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>All metrics emitted by all stages, in emission order.</summary>
    public IReadOnlyList<MetricValue> Metrics { get; init; } = [];

    /// <summary>
    /// All stage outputs collected across the pipeline.
    /// Keys: "{StageName}.{outputKey}".
    /// </summary>
    public IReadOnlyDictionary<string, object?> AllStageOutputs { get; init; }
        = new Dictionary<string, object?>();

    public DateTimeOffset StartedAt { get; init; }
    public double DurationSeconds { get; init; }

    /// <summary>
    /// The raw text response from the primary LLM.
    /// Set by PromptStage from "PromptStage.response" output.
    /// Null if PromptStage did not run or failed.
    /// </summary>
    public string? RawLlmResponse { get; init; }
}
```

### 1.5 EvalProgress

```csharp
namespace Seevalocal.Core.Abstractions;

public record EvalProgress
{
    public required string EvalItemId { get; init; }
    public bool Succeeded { get; init; }
    public int CompletedCount { get; init; }
    public int TotalCount { get; init; }           // -1 if unknown
    public double ElapsedSeconds { get; init; }
    public double? EstimatedRemainingSeconds { get; init; }
    public double? AverageCompletionTokensPerSecond { get; init; }
}
```

### 1.6 IResultCollector

```csharp
namespace Seevalocal.Core.Abstractions;

public interface IResultCollector
{
    /// <summary>Called for each completed EvalResult (may be called concurrently).</summary>
    Task CollectAsync(EvalResult result, CancellationToken ct);

    /// <summary>Called once after all items are processed. Flush any pending state.</summary>
    Task FinalizeAsync(CancellationToken ct);

    /// <summary>Returns all collected results. Safe to call during or after the run.</summary>
    IReadOnlyList<EvalResult> GetResults();
}
```

---

## 2. Core Models

### 2.1 EvalItem

```csharp
namespace Seevalocal.Core.Models;

public record EvalItem
{
    /// <summary>
    /// Stable identifier, unique within a dataset.
    /// Auto-generated as "{sourceName}-{index:D6}" if not provided by the source.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Optional system prompt. Overrides dataset-level default.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The user-turn content sent to the model.</summary>
    public required string UserPrompt { get; init; }

    /// <summary>Reference output for scoring. Null if no expected output is available.</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// Arbitrary string key-value metadata.
    /// Examples: { "category": "greetings", "difficulty": "easy", "sourceLang": "en" }
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Optional path to an associated file artifact.
    /// E.g., the .cs file to complete, the source document for translation.
    /// </summary>
    public string? ArtifactFilePath { get; init; }
}
```

### 2.2 MetricValue

```csharp
namespace Seevalocal.Core.Models;

/// <summary>
/// A typed, named measurement emitted by a pipeline stage.
/// RULE: Name MUST end with a unit suffix (Seconds, Count, Bytes, Ratio, Percent, etc.).
/// See 00-conventions.md §2.1 for the full list.
/// </summary>
public record MetricValue
{
    public required string Name { get; init; }
    public required MetricScalar Value { get; init; }
    /// <summary>The stage that emitted this metric. Used for disambiguation in reports.</summary>
    public string? SourceStage { get; init; }
}

[JsonConverter(typeof(MetricScalarJsonConverter))]
public abstract record MetricScalar
{
    public sealed record IntMetric(int Value) : MetricScalar;
    public sealed record DoubleMetric(double Value) : MetricScalar;
    public sealed record BoolMetric(bool Value) : MetricScalar;
    public sealed record StringMetric(string Value) : MetricScalar;

    /// <summary>Convenience factory methods.</summary>
    public static MetricScalar From(int v) => new IntMetric(v);
    public static MetricScalar From(double v) => new DoubleMetric(v);
    public static MetricScalar From(bool v) => new BoolMetric(v);
    public static MetricScalar From(string v) => new StringMetric(v);
}

public enum MetricType { Int, Double, Bool, String }
```

---

## 3. Config Models

### 3.1 ResolvedConfig (summary — full detail in 03-config.md)

```csharp
namespace Seevalocal.Config.Models;

/// <summary>
/// Fully resolved configuration for a run. All fields are non-null
/// (nullable fields within nested records represent "use server default").
/// Produced by ConfigurationMerger. Treated as immutable after creation.
/// </summary>
public record ResolvedConfig
{
    public required RunMeta Run { get; init; }
    public required ServerConfig Server { get; init; }
    public required LlamaServerSettings LlamaServer { get; init; }
    public required IReadOnlyList<EvalSetConfig> EvalSets { get; init; }
    public JudgeConfig? Judge { get; init; }
}
```

### 3.2 Key Nested Config Types (abbreviated — see 03-config.md for full definition)

```csharp
public record RunMeta
{
    public string RunName { get; init; } = "";
    public string OutputDirectoryPath { get; init; } = "./results";
    public ShellTarget ExportShellTarget { get; init; } = ShellTarget.Bash;
    public bool ContinueOnEvalFailure { get; init; } = true;
    public int? MaxConcurrentEvals { get; init; }
}

public record PipelineConfig
{
    // Pipeline-specific; passed as-is to IBuiltinPipelineFactory.Create().
    // See each pipeline's documentation for supported keys.
}

public record OutputConfig
{
    public bool WritePerEvalJson { get; init; } = true;
    public bool WriteSummaryJson { get; init; } = true;
    public bool WriteSummaryCsv { get; init; } = true;
    public bool WriteResultsParquet { get; init; } = false;
    public bool IncludeRawLlmResponse { get; init; } = true;
    public bool IncludeAllStageOutputs { get; init; } = false;
}

public record ValidationError(string Field, string MessageText);
public enum ShellTarget { Bash, PowerShell }
```

---

## 4. Server Models and Client

### 4.1 ServerConfig (see 02-server.md §3.3 for full definition)

```csharp
namespace Seevalocal.Server.Models;

public record ServerConfig
{
    public bool Manage { get; init; }
    public string? ExecutablePath { get; init; }
    public ModelSource? Model { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 8080;
    public string? ApiKey { get; init; }
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];
    public string? BaseUrl { get; init; }  // for Manage=false
}

public record ModelSource
{
    public ModelSourceKind Kind { get; init; }
    public string? FilePath { get; init; }
    public string? HfRepo { get; init; }
    public string? HfQuant { get; init; }
    public string? HfToken { get; init; }
}

public enum ModelSourceKind { LocalFile, HuggingFace }
public enum GpuKind { Cuda, Vulkan, Metal, CpuOnly }
```

### 4.2 ServerInfo

```csharp
namespace Seevalocal.Server.Models;

/// <summary>
/// Runtime information about a running llama-server instance.
/// Produced by LlamaServerManager.StartAsync().
/// </summary>
public record ServerInfo
{
    /// <summary>Base URL, e.g., "http://127.0.0.1:8080".</summary>
    public required string BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    /// <summary>From GET /props. Used to size the concurrency semaphore.</summary>
    public int TotalSlots { get; init; }
    public string ModelAlias { get; init; } = "";
}
```

### 4.3 LlamaServerClient (interface surface)

```csharp
namespace Seevalocal.Server.Client;

/// <summary>
/// Pre-configured HTTP client for a llama-server instance.
/// Constructed from a ServerInfo. Thread-safe; share one instance per endpoint.
/// </summary>
public sealed class LlamaServerClient
{
    public LlamaServerClient(ServerInfo serverInfo, HttpClient httpClient, ILogger<LlamaServerClient> logger);

    public Task<Result<ChatCompletionResponse>> ChatCompletionAsync(
        ChatCompletionRequest request, CancellationToken ct);

    public Task<Result<ServerProps>> GetPropsAsync(CancellationToken ct);
    public Task<Result<HealthStatus>> GetHealthAsync(CancellationToken ct);
    public Task<Result<TokenizeResponse>> TokenizeAsync(string content, bool addSpecial, CancellationToken ct);
}
```

---

## 5. Data Source Interface

### 5.1 IDataSource

```csharp
namespace Seevalocal.DataSources;

public interface IDataSource
{
    string Name { get; }
    IAsyncEnumerable<EvalItem> GetItemsAsync(CancellationToken ct);
    Task<int?> GetCountAsync(CancellationToken ct);
}
```

---

## 6. Metrics Interfaces

### 6.1 IResultWriter

```csharp
namespace Seevalocal.Metrics.Writers;

public interface IResultWriter
{
    /// <summary>Write one result as it arrives. May be called concurrently.</summary>
    Task WriteResultAsync(EvalResult result, CancellationToken ct);

    /// <summary>Write the run summary. Called once after all results are collected.</summary>
    Task WriteSummaryAsync(RunSummary summary, CancellationToken ct);

    /// <summary>Flush buffers and close files.</summary>
    Task FinalizeAsync(CancellationToken ct);
}
```

---

## 7. Pipeline Interface

### 7.1 IBuiltinPipelineFactory

```csharp
namespace Seevalocal.Pipelines;

public interface IBuiltinPipelineFactory
{
    string PipelineName { get; }
    string Description { get; }
    EvalPipeline Create(EvalSetConfig evalSetConfig, ResolvedConfig resolvedConfig);
    IReadOnlyList<ValidationError> Validate(EvalSetConfig evalSetConfig);
    DataSourceConfig DefaultDataSourceConfig { get; }
}
```

### 7.2 EvalPipeline

```csharp
namespace Seevalocal.Pipelines;

public sealed class EvalPipeline
{
    public string PipelineName { get; init; } = "";
    public IReadOnlyList<IEvalStage> Stages { get; init; } = [];

    /// <summary>
    /// Run all stages for a single item. Stages execute sequentially.
    /// If a stage fails and continueOnStageFailure is false, remaining stages are skipped.
    /// Returns a complete EvalResult regardless of success/failure.
    /// Never throws.
    /// </summary>
    public Task<EvalResult> RunItemAsync(
        EvalStageContext context,
        bool continueOnStageFailure,
        string evalSetId);
}
```

---

## 8. Integration Wiring Diagram

The following shows how all components are connected at runtime. This is the responsibility of the **entry point** (CLI or UI):

```
Entry Point (Cli or Ui)
  │
  ├─ [1] Load PartialConfig(s) via ConfigurationMerger
  │       → ResolvedConfig
  │
  ├─ [2] Validate via ConfigValidator
  │       → abort on errors
  │
  ├─ [3] Start server(s) via LlamaServerManager
  │       → ServerInfo (primary), ServerInfo? (judge)
  │
  ├─ [4] Create LlamaServerClient(s) from ServerInfo(s)
  │
  ├─ [5] For each EvalSetConfig:
  │   ├─ Create IDataSource via DataSourceFactory
  │   ├─ Look up IBuiltinPipelineFactory by PipelineName
  │   ├─ Create EvalPipeline via factory.Create()
  │   ├─ Create IResultWriter(s) per OutputConfig
  │   ├─ Wrap in CompositeResultWriter
  │   ├─ Create InMemoryResultCollector + WritingResultCollector
  │   └─ Create PipelineOrchestrator
  │
  ├─ [6] Determine concurrency:
  │       maxConcurrent = ResolvedConfig.Run.MaxConcurrentEvals
  │                       ?? ServerInfo.TotalSlots
  │
  ├─ [7] Run all orchestrators concurrently
  │       (one per EvalSetConfig, each internally limited by maxConcurrent)
  │
  ├─ [8] After all complete:
  │   ├─ MetricAggregator.Aggregate() → RunSummary
  │   └─ IResultWriter.WriteSummaryAsync() + FinalizeAsync()
  │
  └─ [9] ShellScriptExporter.Export() → write to output dir
```

---

## 9. Error Code Conventions

All public methods that can fail return `Result<T>` (FluentResults). Error messages follow this format:

```
[ComponentName] {short description}: {details}
```

Examples:
```
[LlamaServerManager] Health check timed out after 60.0 seconds: process may have crashed
[DataSourceFactory] Could not read file: /path/to/data.csv — access denied
[JudgeResponseParser] Could not extract numeric score from judge response: "The answer is great!"
[ConfigValidator] Field 'evalSets[0].id' is not unique: 'translation-eval' appears 2 times
```

This format makes log searching reliable and error attribution clear.
