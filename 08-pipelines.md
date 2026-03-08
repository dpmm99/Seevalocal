# Seevalocal — Part 7: Built-in Pipelines

> **Read `00-conventions.md` before this file.**  
> Interfaces referenced here are defined in `10-interfaces.md`.  
> This part is implemented in project `Seevalocal.Pipelines`.

---

## 1. Responsibilities

- Provide three self-contained, ready-to-use pipeline definitions.
- Each pipeline includes: default `DataSourceConfig`, default `PipelineConfig`, required stages, and a setup wizard (used by both CLI and UI) that resolves all prerequisites.
- Each pipeline is fully operable from a single command once the user provides a model path/HuggingFace spec.

---

## 2. Pipeline Registration

```csharp
public interface IBuiltinPipelineFactory
{
    string PipelineName { get; }
    string Description { get; }

    /// <summary>Builds an EvalPipeline from the merged config.</summary>
    EvalPipeline Create(EvalSetConfig evalSetConfig, ResolvedConfig resolvedConfig);

    /// <summary>
    /// Validates pipeline-specific options in EvalSetConfig.PipelineOptions.
    /// Returns empty list on success.
    /// </summary>
    IReadOnlyList<ValidationError> Validate(EvalSetConfig evalSetConfig);

    /// <summary>
    /// Returns the default DataSourceConfig for this pipeline type.
    /// Used when the user doesn't specify one.
    /// </summary>
    DataSourceConfig DefaultDataSourceConfig { get; }
}
```

Registered factories (keyed by `PipelineName`):
- `"Translation"` → `TranslationPipelineFactory`
- `"CSharpCoding"` → `CSharpCodingPipelineFactory`
- `"CasualQA"` → `CasualQAPipelineFactory`

---

## 3. Pipeline: Translation

### 3.1 Purpose

Evaluates language translation quality using LLM-as-judge for accuracy scoring.

### 3.2 Stages

1. `PromptStage` — sends translation request to primary LLM
2. `JudgeStage` — sends (source, reference translation, actual translation) to judge LLM

### 3.3 Default System Prompt

```
You are a professional translator. Translate the following text accurately and naturally. Output only the translation, with no explanation or preamble.
```

### 3.4 PipelineOptions (in settings file)

```yaml
pipelineOptions:
  sourceLanguage: English
  targetLanguage: French
  judgePromptTemplate: null   # null = use default judge template
```

### 3.5 Default Data Source

```
Kind: SplitDirectories
PromptDirectoryPath: ./data/source
ExpectedOutputDirectoryPath: ./data/reference
FileExtensionFilter: "*.txt"
```

### 3.6 Metrics Emitted

| Metric | Source |
|---|---|
| `promptTokenCount` | PromptStage |
| `completionTokenCount` | PromptStage |
| `llmLatencySeconds` | PromptStage |
| `judgeScoreRatio` | JudgeStage |
| `judgePassedBool` | JudgeStage |

### 3.7 Auto-Setup

The `TranslationPipelineFactory.EnsurePrerequisitesAsync` checks:
- Judge endpoint is configured and reachable.
- Source and reference directories exist (or can be created for sample data).

---

## 4. Pipeline: CSharpCoding

### 4.1 Purpose

Evaluates C# code generation quality by compiling the output and running unit tests.

### 4.2 Stages

1. `PromptStage` — sends coding task to primary LLM
2. `FileWriterStage` — saves generated code to a temp `.cs` file (strips markdown fences)
3. `ExternalProcessStage` ("CompileStage") — runs `dotnet build` on a preconfigured project skeleton
4. `ExternalProcessStage` ("TestStage") — runs `dotnet test`, capturing test results
5. `JudgeStage` *(optional)* — if configured, evaluates code quality/style

### 4.3 Project Skeleton

Each eval item gets a **fresh copy** of a template .NET project placed in a temp directory:

```
<tempDir>/eval-{id}/
├── EvalProject.csproj
├── Generated.cs         ← written by FileWriterStage
└── Tests/
    ├── Tests.csproj
    └── TestSuite.cs     ← pre-written unit tests from dataset
```

The `EvalSetConfig.PipelineOptions["testProjectTemplatePath"]` points to the template directory to copy. If absent, a minimal default template (compiles `Generated.cs` into a class library) is used.

### 4.4 PipelineOptions

```yaml
pipelineOptions:
  testProjectTemplatePath: ./templates/csharp-test-skeleton
  compileTimeoutSeconds: 30.0
  testTimeoutSeconds: 60.0
  cleanupTempFilesOnSuccess: true
  cleanupTempFilesOnFailure: false   # keep for debugging
  scoreStyleWithJudge: false
```

### 4.5 Metric Extractors (CompileStage)

From `dotnet build` output:

| Pattern | Metric |
|---|---|
| `Build succeeded` (exit code 0) | `compilationSucceededBool` |
| `Error\(s\)` regex `(\d+) Error` | `compilationErrorCount` |
| `Warning\(s\)` regex `(\d+) Warning` | `compilationWarningCount` |

From process: `compileDurationSeconds`

### 4.6 Metric Extractors (TestStage)

From `dotnet test --logger "console;verbosity=normal"`:

| Pattern | Metric |
|---|---|
| `Passed: (\d+)` | `testPassCount` |
| `Failed: (\d+)` | `testFailCount` |
| `Skipped: (\d+)` | `testSkipCount` |

Derived: `testTotalCount` = pass + fail + skip  
Derived: `testPassRatioPercent` = (pass / total) * 100

From process: `testDurationSeconds`

Additional metrics from stage outputs:
- `compilationSucceededBool` (bool)
- `codeLineCount` (int — count non-blank lines of `FileWriterStage.writtenFilePath`)

### 4.7 Auto-Setup

`CSharpCodingPipelineFactory.EnsurePrerequisitesAsync`:
1. Check `dotnet` is on PATH (`dotnet --version`).
2. If not, emit a clear error: "Install .NET SDK from https://dotnet.microsoft.com/download".
3. Verify the template project builds cleanly.
4. Create temp directory structure.

### 4.8 Default Data Source

```
Kind: Directory
PromptDirectoryPath: ./data/prompts      # one prompt per .txt file
```

Each prompt file may optionally be accompanied by a `<id>.tests.cs` file in the same directory to be used as the test suite for that item.

---

## 5. Pipeline: CasualQA

### 5.1 Purpose

Evaluates casual conversational Q&A — open-ended questions where there is a reference answer but semantic similarity matters more than exact match.

### 5.2 Stages

1. `PromptStage` — sends question to primary LLM
2. `ExactMatchStage` — optional quick filter (exact match score)
3. `JudgeStage` — semantic accuracy evaluation

### 5.3 Default System Prompt

```
You are a helpful, knowledgeable assistant. Answer the following question concisely and accurately.
```

### 5.4 PipelineOptions

```yaml
pipelineOptions:
  enableExactMatch: false
  judgeMaxScore: 10
  judgePassThresholdRatio: 0.6  # 6/10 = pass
```

### 5.5 Metrics Emitted

| Metric | Source |
|---|---|
| `promptTokenCount` | PromptStage |
| `completionTokenCount` | PromptStage |
| `llmLatencySeconds` | PromptStage |
| `exactMatch` | ExactMatchStage (if enabled) |
| `judgeScoreRatio` | JudgeStage |
| `judgePassedBool` | JudgeStage |

### 5.6 Default Data Source

```
Kind: JsonFile
DataFilePath: ./data/qa.json
FieldMapping:
  idField: id
  userPromptField: question
  expectedOutputField: answer
```

---

## 6. Common Auto-Setup Behavior (All Pipelines)

All three factories share a common auto-setup helper that:

1. Detects GPU (`GpuDetector.DetectAsync`).
2. Downloads the appropriate `llama-server` binary if `manage = true` and no `executablePath` is set.
3. If `hfRepo` is specified, does not download the model (llama-server does this itself via `-hf`).
4. Prints a summary of what will happen before starting.
5. Prompts for confirmation in interactive mode (skipped with `--yes` flag or in non-TTY mode).

---

## 7. Unit Tests (Seevalocal.Pipelines.Tests)

| Test class | Coverage |
|---|---|
| `TranslationPipelineTests` | Stage order correct; judge stage present; default data source config |
| `CSharpCodingPipelineTests` | File writer stage strips fences; compile/test stages have correct timeouts; metric extractor patterns |
| `CasualQAPipelineTests` | ExactMatch skipped when disabled; judge configured |
| `AutoSetupTests` | dotnet not on PATH → clear error message; binary already cached → no download |
| `PipelineRegistryTests` | All three names registered; unknown name → helpful error |
