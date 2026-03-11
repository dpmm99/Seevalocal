# Seevalocal

**LLM Evaluation Tool**

Seevalocal is a cross-platform (.NET 10) tool for running structured, repeatable evaluations of LLMs served by `llama-server` (llama.cpp) or other OpenAI Completions API compatible endpoints. It provides both a CLI and an Avalonia UI for configuring and running evaluations, with built-in support for LLM-as-judge scoring, multiple data source formats, and result exports complete with metrics.

This is still a work-in-progress; see the Todo.md file.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Project Structure](#project-structure)
3. [Core Concepts](#core-concepts)
4. [Configuration System](#configuration-system)
5. [Data Flow](#data-flow)
6. [Pipeline Architecture](#pipeline-architecture)
7. [Creating Custom Pipelines](#creating-custom-pipelines)
8. [Creating Custom Stages](#creating-custom-stages)
9. [UI Architecture](#ui-architecture)

---

## Quick Start

### Running an Evaluation

1. **Via UI**: Launch `Seevalocal.UI`, use the Setup Wizard to configure your model(s), data source, and any other options you want, then click "Start Run"
2. **Via CLI**: 
   ```bash
   dotnet run --project Seevalocal.Cli --run --settings my-config.yml
   ```

### Example Settings File

```yaml
run:
  outputDirectoryPath: ./results
  exportShellTarget: Bash
  continueOnEvalFailure: true
server:
  manage: true
  executablePath: C:\AI\vulkan\llama-server.exe
llamaServer: &o0
  samplingTemperature: 0
evalSets: []
judge:
  enable: true
  serverConfig:
    manage: true
    executablePath: C:\AI\vulkan\llama-server.exe
    model:
      filePath: C:\AI\LiquidAI_LFM2-24B-A2B-Q4_K_M.gguf
  serverSettings:
    contextWindowTokens: 8192
    parallelSlotCount: 1
    samplingTemperature: 0
  judgePromptTemplate: standard
  scoreMinValue: 0
  scoreMaxValue: 100
  scoreMin: 0
  scoreMax: 100
output:
  writePerEvalJson: true
  writeSummaryJson: true
  writeSummaryCsv: true
  includeRawLlmResponse: true
dataSource:
  kind: SplitDirectories
  promptDirectoryPath: C:\AI\Eval
  expectedOutputDirectoryPath: C:\AI\ExpectedOutput
  promptDirectory: C:\AI\Eval
  expectedDirectory: C:\AI\ExpectedOutput
llamaSettings: *o0
```

---

## Project Structure

| Project | Responsibility |
|---------|---------------|
| **`Seevalocal.Core.Abstractions`** | Core interfaces and data models (`EvalResult`, `EvalItem`, `MetricValue`, `IEvalStage`, `IEvalPipeline`) |
| **`Seevalocal.Core.Pipeline`** | Pipeline orchestrator, stage execution, concurrency control, checkpoint/resume |
| **`Seevalocal.Server`** | llama-server lifecycle (download, launch, GPU detection), HTTP client wrapper |
| **`Seevalocal.DataSources`** | Data source abstraction and implementations (JSON, YAML, CSV, Parquet, directories) |
| **`Seevalocal.Judge`** | LLM-as-judge scoring stage with prompt templates and response parsing |
| **`Seevalocal.Metrics`** | Metric collection, aggregation, and output writers (JSON, CSV, Parquet) |
| **`Seevalocal.Pipelines`** | Built-in pipeline factories (CasualQA, Translation, CSharpCoding) |
| **`Seevalocal.Config`** | Configuration loading, merging, validation, and shell script export |
| **`Seevalocal.UI`** | Avalonia UI application with wizard, run dashboard, results viewer, and settings editor |

---

## Core Concepts

### EvalItem
A single evaluation input:
```csharp
public record EvalItem
{
    public string Id { get; init; }           // Unique identifier
    public string? SystemPrompt { get; init; }
    public string UserPrompt { get; init; }   // The prompt to send
    public string? ExpectedOutput { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
}
```

### EvalResult
A single evaluation output:
```csharp
public record EvalResult
{
    public string EvalItemId { get; init; }
    public string EvalSetId { get; init; }
    public bool Succeeded { get; init; }
    public string? FailureReason { get; init; }
    public IReadOnlyList<MetricValue> Metrics { get; init; }
    public IReadOnlyDictionary<string, object?> AllStageOutputs { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public double DurationSeconds { get; init; }
    public string? RawLlmResponse { get; init; }
}
```

### MetricValue
A typed measurement:
```csharp
public record MetricValue
{
    public string Name { get; init; }      // Must end with unit suffix (Count, Seconds, etc.)
    public MetricScalar Value { get; init; }
    public string? SourceStage { get; init; }
}

// MetricScalar is a discriminated union:
// - IntMetric(int)
// - DoubleMetric(double)
// - BoolMetric(bool)
// - StringMetric(string)
```

---

## Configuration System

### Configuration Layers (Priority: High → Low)

1. **Wizard state** — Values explicitly set by the user in the UI wizard
2. **Settings view fields** — Values edited in the Settings screen
3. **Loaded settings files** — YAML/JSON/TOML files loaded via "Load Settings" (last loaded = highest priority)
4. **Default values** — Hardcoded defaults in the model classes

**Key principle**: Null means "not set, so fall back to lower priority layer". Empty strings are treated as null.

### ResolvedConfig Structure

```
ResolvedConfig
├── Run (RunMeta)              — Run name, output directory, shell target
├── Server (ServerConfig)      — Primary llama-server connection/management
├── LlamaServer (LlamaServerSettings) — All llama-server tuning parameters
├── EvalSets (EvalSetConfig[]) — One or more evaluation sets to run
│   ├── DataSource             — Data source configuration
│   ├── Pipeline               — Pipeline-specific options
│   └── Output                 — Output format options
├── Judge (JudgeConfig?)       — Optional LLM-as-judge configuration
└── DataSource (DataSourceConfig) — Global data source defaults
```

### Configuration Flow

```
User Action (UI/CLI)
       │
       ▼
┌───────────────────┐
│ PartialConfig     │  ← All fields nullable
│ (from wizard,     │
│  settings, file)  │
└────────┬──────────┘
         │
         ▼
┌───────────────────┐
│ConfigurationMerger│
│ (merges layers)   │
└────────┬──────────┘
         │
         ▼
┌───────────────────┐
│ ResolvedConfig    │  ← Final config for run
│ (all fields set)  │
└────────┬──────────┘
         │
         ▼
┌───────────────────┐
│ ConfigValidator   │
│ (validates)       │
└───────────────────┘
```

### Judge Template Names

Judge templates are specified by **name** (e.g., `"standard"`, `"pass-fail"`, `"structured-json"`), not by full template content. The `JudgeStage.ResolveTemplate()` method uses reflection to look up the actual template content from `DefaultTemplates` at runtime.

Available template names (kebab-case):
- `standard` — 0-100 scoring with rationale first in the output, using a rubric
- `pass-fail` — Binary pass/fail verdict
- `structured-json` — Full structured JSON response
- `translation` — Translation quality scoring
- `casualqa` — Casual Q&A evaluation
- `codequality` — Code quality evaluation

---

## Data Flow

### Single Eval Item Flow

```
DataSource.GetItemsAsync()
    │
    ▼
EvalItem { Id, UserPrompt, ExpectedOutput?, Metadata }
    │
    ▼
PipelineOrchestrator.RunItemAsync()
    │
    ├─► PromptStage
    │     └─► llama-server /v1/chat/completions
    │     └─► Output: "PromptStage.response", metrics (token counts, latency)
    │
    ├─► [Optional] ExactMatchStage
    │     └─► String comparison with ExpectedOutput
    │     └─► Output: "ExactMatchStage.passed" (bool)
    │
    ├─► [Optional] JudgeStage
    │     └─► Judge LLM with template: {prompt}, {expectedOutput}, {actualOutput}
    │     └─► Output: "JudgeStage.rationale", "JudgeStage.score" (double)
    │
    ├─► [Optional] ExternalProcessStage
    │     └─► Run external process (compiler, tests, etc.)
    │     └─► Output: metrics from stdout parsing
    │
    ▼
EvalResult { EvalItemId, Metrics[], AllStageOutputs, DurationSeconds }
    │
    ▼
ResultCollector.CollectAsync()
    │
    ├─► In-memory cache (for UI display)
    ├─► SQLite database (for checkpoint/resume)
    └─► Result writers (JSON, CSV, Parquet)
```

### Two-Phase Execution (When Using Two Locally Managed Llama-Server Instances)

When both the primary model server AND judge server are locally managed:

**Phase 1 (Primary)**:
- Start primary llama-server
- Run PromptStage (+ any non-judge stages) for ALL items
- Stop primary llama-server
- Save checkpoint to database

**Phase 2 (Judge)**:
- Start judge llama-server
- Load completed items from checkpoint
- Run JudgeStage for all completed items
- Stop judge llama-server
- Write final results

This allows running on machines with limited VRAM by not keeping both models loaded simultaneously.

---

## Pipeline Architecture

### IEvalPipeline

```csharp
public interface IEvalPipeline
{
    string PipelineName { get; }
    IReadOnlyList<IEvalStage> Stages { get; }
    Task<EvalResult> RunItemAsync(EvalStageContext context, bool continueOnStageFailure);
}
```

### IEvalStage

```csharp
public interface IEvalStage
{
    string StageName { get; }
    Task<StageResult> ExecuteAsync(EvalStageContext context);
}

public record StageResult
{
    public bool Succeeded { get; init; }
    public string? FailureReason { get; init; }
    public Dictionary<string, object?> Outputs { get; init; }
    public List<MetricValue> Metrics { get; init; }
}
```

### Built-in Stages

| Stage | Purpose | Outputs | Metrics |
|-------|---------|---------|---------|
| `PromptStage` | Send prompt to primary LLM | `response`, `rawResponse` | `promptTokenCount`, `completionTokenCount`, `llmLatencySeconds`, `promptTokensPerSecond`, `completionTokensPerSecond` |
| `ExactMatchStage` | String comparison with expected output | `passed` (bool) | `exactMatchScore` (0 or 1) |
| `JudgeStage` | LLM-as-judge scoring | `rationale`, `score` | `judgeScore` (e.g., 0-10), `judgePassedBool` (0 or 1) |
| `FileWriterStage` | Write item output to file | `filePath` | `fileSizeBytes`, `writeDurationSeconds` |
| `ExternalProcessStage` | Run external process (compiler, tests) | `exitCode`, `stdout`, `stderr` | Custom metrics from output parsing |

---

## Creating Custom Pipelines

### Step 1: Create a Pipeline Factory

Implement `IBuiltinPipelineFactory`:

```csharp
public sealed class MyPipelineFactory(ILoggerFactory loggerFactory) : IBuiltinPipelineFactory
{
    public string PipelineName => "MyPipeline";
    public string Description => "Description of my custom pipeline";

    public DataSourceConfig DefaultDataSourceConfig => new()
    {
        Kind = DataSourceKind.JsonFile,
        FilePath = "./data/my-data.json",
        FieldMapping = new FieldMapping
        {
            UserPromptField = "prompt",
            ExpectedOutputField = "expected",
        },
    };

    public IReadOnlyList<ValidationError> Validate(EvalSetConfig evalSetConfig)
    {
        // Return validation errors if any
        return [];
    }

    public EvalPipeline Create(EvalSetConfig evalSetConfig, ResolvedConfig resolvedConfig)
    {
        var stages = new List<IEvalStage>
        {
            new PromptStage(loggerFactory.CreateLogger<PromptStage>()),
            // Add your custom stages here
            new MyCustomStage(loggerFactory.CreateLogger<MyCustomStage>()),
        };

        // Use resolvedConfig.Judge to access judge configuration
        if (resolvedConfig.Judge != null)
        {
            stages.Add(new JudgeStage(
                resolvedConfig.Judge,
                loggerFactory.CreateLogger<JudgeStage>(),
                loggerFactory.CreateLogger<JudgePromptRenderer>(),
                loggerFactory.CreateLogger<JudgeResponseParser>()));
        }

        return new EvalPipeline(loggerFactory.CreateLogger<EvalPipeline>())
        {
            PipelineName = PipelineName,
            Stages = stages,
        };
    }
}
```

### Step 2: Register the Factory

In `Program.cs` or your DI setup:

```csharp
services.AddSingleton<IBuiltinPipelineFactory, MyPipelineFactory>();
```

### Step 3: Use in Settings

```yaml
evalSets:
  - name: "my-eval"
    pipelineName: "MyPipeline"
    dataSource:
      kind: JsonFile
      filePath: ./data/my-data.json
```

---

## Creating Custom Stages

### Basic Stage Implementation

```csharp
public sealed class MyCustomStage(ILogger<MyCustomStage> logger) : IEvalStage
{
    public string StageName => "MyCustomStage";

    public async Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        try
        {
            // Access input from previous stages
            var promptResponse = context.StageOutputs
                .GetValueOrDefault("PromptStage.response") as string;

            // Access the eval item
            var userPrompt = context.Item.UserPrompt;
            var expectedOutput = context.Item.ExpectedOutput;

            // Do your custom processing
            var result = await ProcessAsync(promptResponse, context.CancellationToken);

            // Return outputs and metrics
            return StageResult.Success(new Dictionary<string, object?>
            {
                ["MyCustomStage.result"] = result,
            }, new List<MetricValue>
            {
                new MetricValue 
                { 
                    Name = "customMetricCount", 
                    Value = new MetricScalar.IntMetric(42) 
                },
            });
        }
        catch (Exception ex)
        {
            return StageResult.Failure($"Processing failed: {ex.Message}");
        }
    }

    private Task<string> ProcessAsync(string input, CancellationToken ct)
    {
        // Your implementation
        return Task.FromResult($"Processed: {input}");
    }
}
```

### StageResult Helper Methods

```csharp
// Success with outputs and metrics
StageResult.Success(
    outputs: new Dictionary<string, object?> { ["key"] = value },
    metrics: new List<MetricValue> { ... }
)

// Success with no outputs
StageResult.Success()

// Failure with reason
StageResult.Failure("Error message")
```

### Accessing Configuration in Stages

Stages receive an `EvalStageContext`:

```csharp
public record EvalStageContext
{
    public EvalItem Item { get; init; }
    public IReadOnlyDictionary<string, object?> StageOutputs { get; init; }
    public LlamaServerClient? PrimaryClient { get; init; }
    public LlamaServerClient? JudgeClient { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
```

For additional configuration, pass it via the stage constructor when creating the pipeline.

### Metric Naming Conventions

Metric names MUST end with a unit suffix:
- `Count` — Integer counts (e.g., `tokenCount`, `errorCount`)
- `Seconds` — Time durations (e.g., `latencySeconds`, `durationSeconds`)
- `Bytes` — Data sizes (e.g., `fileSizeBytes`, `responseBytes`)
- `Ratio` — Unitless ratios 0-1 (e.g., `accuracyRatio`)
- `Percent` — Percentages 0-100 (e.g., `successPercent`)

---

## UI Architecture

### Main Views

| View | Purpose |
|------|---------|
| `WizardView` | Guided setup for new runs (step-by-step configuration) |
| `RunDashboardView` | Live run monitoring with progress, metrics, and early completions |
| `ResultsView` | Full results browser with filtering and export |
| `SettingsView` | Full settings editor with search, materialized values, and file load/save |

### Key ViewModels

| ViewModel | Responsibility |
|-----------|---------------|
| `MainWindowViewModel` | Application state, navigation, config resolution |
| `WizardViewModel` | Wizard state, validation, `BuildPartialConfig()` |
| `EvalRunViewModel` | Single-phase run monitoring and control |
| `TwoPhaseEvalRunViewModel` | Two-phase (primary + judge) run monitoring |
| `SettingsViewModel` | Settings field management, materialized value computation |

### Data Source Configuration in UI

The wizard and settings views support these data source modes:

| Mode | Description | Fields |
|------|-------------|--------|
| `SingleFile` | Single JSON/JSONL/YAML/CSV/Parquet file | `FilePath` |
| `SplitDirectories` | Prompts in one folder, expected outputs in another | `PromptDirectoryPath`, `ExpectedOutputDirectoryPath` |
| `Directory` | Prompts only (no expected outputs) | `PromptDirectoryPath` |

File type detection is automatic based on extension when using `SingleFile` mode.

### JSONL Default Field Mapping

When using JSONL files, the default field mapping is:
- `question` → `UserPrompt`
- `answer` → `ExpectedOutput`
- `id` → `Id` (auto-generated if missing)

This can be overridden in the settings file's `fieldMapping` section.
