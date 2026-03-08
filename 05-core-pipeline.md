# Seevalocal — Part 4: Pipeline Core & Orchestration

> **Read `00-conventions.md` before this file.**  
> Interfaces referenced here are defined in `10-interfaces.md`.  
> This part is implemented in project `Seevalocal.Core`.

---

## 1. Responsibilities

- Define the `IEvalStage` interface and the stage execution contract.
- Orchestrate the sequence of stages for each `EvalItem`.
- Manage concurrency using `total_slots` from the server.
- Collect `EvalResult` from each item and route it to the metrics/output system.
- Handle cancellation, per-item errors, and retry logic.
- Publish progress events for UI consumption.

---

## 2. Core Types

### 2.1 EvalStageContext

Passed to every stage; carries all information the stage needs.

```csharp
public record EvalStageContext
{
    /// <summary>The item being evaluated.</summary>
    public EvalItem Item { get; init; } = new();

    /// <summary>Accumulated outputs from all previous stages in this item's pipeline.</summary>
    public IReadOnlyDictionary<string, object?> StageOutputs { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>The resolved run configuration.</summary>
    public ResolvedConfig Config { get; init; } = new();

    /// <summary>
    /// HTTP client pre-configured for the primary LLM endpoint.
    /// Stages should use this rather than creating their own clients.
    /// </summary>
    public LlamaServerClient PrimaryClient { get; init; } = null!;

    /// <summary>
    /// HTTP client pre-configured for the judge endpoint, if configured.
    /// Null if no judge is configured.
    /// </summary>
    public LlamaServerClient? JudgeClient { get; init; }

    /// <summary>Token to observe for cancellation.</summary>
    public CancellationToken CancellationToken { get; init; }
}
```

### 2.2 StageResult

Each stage returns a `StageResult` describing what it produced.

```csharp
public record StageResult
{
    /// <summary>
    /// Named outputs this stage produced. These become available in StageOutputs
    /// for subsequent stages. Keys must be unique within the pipeline.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Outputs { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>Typed metrics emitted by this stage.</summary>
    public IReadOnlyList<MetricValue> Metrics { get; init; } = [];

    /// <summary>Whether this stage succeeded. Failure stops subsequent stages for this item.</summary>
    public bool Succeeded { get; init; } = true;

    /// <summary>Human-readable failure reason, if any.</summary>
    public string? FailureReason { get; init; }
}
```

### 2.3 EvalResult

The combined output for a single `EvalItem` after all stages complete.

```csharp
public record EvalResult
{
    public string EvalItemId { get; init; } = "";
    public string EvalSetId { get; init; } = "";
    public bool Succeeded { get; init; }
    public string? FailureReason { get; init; }
    public IReadOnlyList<MetricValue> Metrics { get; init; } = [];
    public IReadOnlyDictionary<string, object?> AllStageOutputs { get; init; }
        = new Dictionary<string, object?>();
    public DateTimeOffset StartedAt { get; init; }
    public double DurationSeconds { get; init; }
    public string? RawLlmResponse { get; init; }  // captured by PromptStage
}
```

---

## 3. IEvalStage Interface

```csharp
/// <summary>
/// A single step in an evaluation pipeline.
/// Stages are executed sequentially per EvalItem.
/// Stages must be thread-safe: multiple items run concurrently in different threads,
/// each with its own EvalStageContext. A single stage instance is shared across all items.
/// </summary>
public interface IEvalStage
{
    /// <summary>
    /// Unique name within a pipeline. Used to key outputs in StageOutputs.
    /// Convention: PascalCase, e.g., "PromptStage", "CompileStage".
    /// </summary>
    string StageName { get; }

    /// <summary>
    /// Execute this stage for the given item context.
    /// Must not throw for expected failures — return a failed StageResult instead.
    /// </summary>
    Task<StageResult> ExecuteAsync(EvalStageContext context);
}
```

---

## 4. Built-in Stages

### 4.1 PromptStage

Sends the item's `SystemPrompt` + `UserPrompt` to the primary LLM endpoint.

```csharp
public sealed class PromptStage : IEvalStage
{
    public string StageName => "PromptStage";

    // Options (set per pipeline instance)
    public int? MaxTokens { get; init; }
    public IReadOnlyList<string> StopSequences { get; init; } = [];
    public bool UseAnthropicApi { get; init; } = false;
}
```

**Outputs:**
- `"PromptStage.response"` → `string` (model's text response)
- `"PromptStage.rawResponse"` → `ChatCompletionResponse` (full deserialized object)

**Metrics emitted:**
- `"promptTokenCount"` (int)
- `"completionTokenCount"` (int)
- `"totalTokenCount"` (int)
- `"llmLatencySeconds"` (double)
- `"promptTokensPerSecond"` (double)
- `"completionTokensPerSecond"` (double)

### 4.2 ExternalProcessStage

Runs an external program, captures stdout/stderr, and extracts metrics.

```csharp
public sealed class ExternalProcessStage : IEvalStage
{
    public string StageName { get; init; } = "ExternalProcessStage";

    public string ExecutablePath { get; init; } = "";
    public string Arguments { get; init; } = "";    // supports {stageOutput.X} placeholders
    public string? WorkingDirectoryPath { get; init; }
    public double TimeoutSeconds { get; init; } = 30.0;

    // Patterns to extract named numeric metrics from stdout
    public IReadOnlyList<MetricExtractorConfig> MetricExtractors { get; init; } = [];

    // Whether non-zero exit code = failure
    public bool FailOnNonZeroExit { get; init; } = true;
}

public record MetricExtractorConfig
{
    public string MetricName { get; init; } = "";   // must include unit suffix
    public string RegexPattern { get; init; } = ""; // named group "value"
    public MetricType Type { get; init; } = MetricType.Double;
}
```

**Outputs:**
- `"{StageName}.stdout"` → `string`
- `"{StageName}.stderr"` → `string`
- `"{StageName}.exitCode"` → `int`
- Each extracted metric also as a stage output

**Metrics emitted:**
- `"processExitCode"` (int)
- `"processDurationSeconds"` (double)
- Plus all `MetricExtractorConfig`-defined metrics

### 4.3 ExactMatchStage

Compares the LLM response to `ExpectedOutput`.

```csharp
public sealed class ExactMatchStage : IEvalStage
{
    public string StageName => "ExactMatchStage";

    public bool CaseSensitive { get; init; } = false;
    public bool TrimWhitespace { get; init; } = true;
    public string InputStageOutputKey { get; init; } = "PromptStage.response";
}
```

**Metrics emitted:**
- `"exactMatch"` (bool → stored as int 0/1)

### 4.4 FileWriterStage

Writes a stage output value to a file (used by coding pipelines to save generated code).

```csharp
public sealed class FileWriterStage : IEvalStage
{
    public string StageName { get; init; } = "FileWriterStage";
    public string InputStageOutputKey { get; init; } = "PromptStage.response";
    public string OutputFilePathTemplate { get; init; } = "./generated/{id}.cs"; // {id} = EvalItem.Id
    public bool StripMarkdownCodeFences { get; init; } = true;
}
```

**Outputs:**
- `"{StageName}.writtenFilePath"` → `string`

---

## 5. EvalPipeline

An `EvalPipeline` is a named, ordered list of stages.

```csharp
public sealed class EvalPipeline
{
    public string PipelineName { get; init; } = "";
    public IReadOnlyList<IEvalStage> Stages { get; init; } = [];

    /// <summary>
    /// Execute all stages sequentially for a single item.
    /// If any stage fails and the pipeline is not configured to continue on failure,
    /// subsequent stages are skipped.
    /// </summary>
    public Task<EvalResult> RunItemAsync(EvalStageContext context, bool continueOnStageFailure);
}
```

---

## 6. PipelineOrchestrator

The orchestrator drives the entire evaluation run.

```csharp
public sealed class PipelineOrchestrator
{
    public PipelineOrchestrator(
        IDataSource dataSource,
        EvalPipeline pipeline,
        EvalSetConfig evalSetConfig,
        ResolvedConfig resolvedConfig,
        LlamaServerClient primaryClient,
        LlamaServerClient? judgeClient,
        IResultCollector resultCollector,
        IProgress<EvalProgress> progress,
        ILogger<PipelineOrchestrator> logger);

    /// <summary>
    /// Run all items concurrently, up to maxConcurrentCount.
    /// Returns when all items are complete or cancellation is requested.
    /// </summary>
    public Task RunAsync(int maxConcurrentCount, CancellationToken ct);
}
```

### 6.1 Concurrency Implementation

```csharp
public async Task RunAsync(int maxConcurrentCount, CancellationToken ct)
{
    var semaphore = new SemaphoreSlim(maxConcurrentCount);
    var channel = Channel.CreateUnbounded<EvalItem>();

    // Producer: write all items to channel
    _ = Task.Run(async () =>
    {
        await foreach (var item in _dataSource.GetItemsAsync(ct))
            await channel.Writer.WriteAsync(item, ct);
        channel.Writer.Complete();
    }, ct);

    // Consumers: process items from channel
    var tasks = Enumerable.Range(0, maxConcurrentCount)
        .Select(_ => ConsumeAsync(channel.Reader, semaphore, ct));

    await Task.WhenAll(tasks);
}

private async Task ConsumeAsync(
    ChannelReader<EvalItem> reader,
    SemaphoreSlim semaphore,
    CancellationToken ct)
{
    await foreach (var item in reader.ReadAllAsync(ct))
    {
        await semaphore.WaitAsync(ct);
        try
        {
            var context = BuildContext(item, ct);
            var result = await _pipeline.RunItemAsync(context, _config.Run.ContinueOnEvalFailure);
            await _resultCollector.CollectAsync(result, ct);
            _progress.Report(new EvalProgress(item.Id, result.Succeeded));
        }
        finally
        {
            semaphore.Release();
        }
    }
}
```

---

## 7. Progress Reporting

```csharp
public record EvalProgress
{
    public string EvalItemId { get; init; } = "";
    public bool Succeeded { get; init; }
    public int CompletedCount { get; init; }
    public int TotalCount { get; init; }
    public double ElapsedSeconds { get; init; }
}
```

The `IProgress<EvalProgress>` implementation differs between CLI (Spectre progress bar) and UI (Avalonia binding).

---

## 8. IResultCollector Interface

```csharp
public interface IResultCollector
{
    Task CollectAsync(EvalResult result, CancellationToken ct);
    Task FinalizeAsync(CancellationToken ct);
    IReadOnlyList<EvalResult> GetResults();
}
```

A default `InMemoryResultCollector` buffers results. The metrics/output layer (Part 5) wraps this with writers.

---

## 9. Retry Policy per Item

Items that fail due to transient errors (HTTP 503, timeout) are retried up to a configurable `MaxRetryCount` (default: 2) with exponential back-off. Items that fail due to logic errors (bad response format, compilation failure) are not retried.

```csharp
public record RetryConfig
{
    public int MaxRetryCount { get; init; } = 2;
    public double InitialDelaySeconds { get; init; } = 1.0;
    public double BackoffMultiplier { get; init; } = 2.0;
}
```

---

## 10. Unit Tests (Seevalocal.Core.Tests)

| Test class | Coverage |
|---|---|
| `EvalPipelineTests` | Sequential stage execution; stage failure stops pipeline; context propagation |
| `PipelineOrchestratorTests` | Concurrency limit respected; all items processed; cancellation mid-run |
| `PromptStageTests` | Mock client; correct request formed; metrics extracted |
| `ExternalProcessStageTests` | Stdout/stderr captured; exit code metric; regex extractor |
| `ExactMatchStageTests` | Case-insensitive match; trim whitespace |
| `FileWriterStageTests` | Markdown fence stripping; path template substitution |
