# Seevalocal — Part 2: Configuration System

> **Read `00-conventions.md` before this file.**
> Interfaces referenced here are defined in `10-interfaces.md`.
> This part is implemented in project `Seevalocal.Config`.

---

## 1. Responsibilities

- Define all settings as C# records with `null` representing "not set."
- Load settings from YAML, JSON, or TOML files.
- Merge multiple settings files according to the priority rules in `00-conventions.md §6`.
- Bind CLI flags into the same settings model.
- Expose a `ResolvedConfig` that is the single authoritative configuration for a run.
- Export the current configuration as a reproducible shell script (bash or PowerShell).
- Validate the resolved configuration and return human-readable error messages.

---

## 2. Settings Model Hierarchy

```
ResolvedConfig
├── RunMeta           (run name, output dir, shell target for export)
├── ServerConfig      (from Seevalocal.Server — see 02-server.md §3.3)
├── LlamaServerSettings  (all llama-server tuning knobs)
├── EvalSetConfig[]   (one or more evaluation sets to run)
│   ├── DataSourceConfig
│   ├── PipelineConfig
│   └── OutputConfig
└── JudgeConfig?      (optional second LLM endpoint for scoring)
```

### 2.1 LlamaServerSettings

All fields are nullable — null means "omit from CLI / use llama-server default."

```csharp
public record LlamaServerSettings
{
    // Context / batching
    public int? ContextWindowTokens { get; init; }
    public int? BatchSizeTokens { get; init; }
    public int? UbatchSizeTokens { get; init; }
    public int? ParallelSlotCount { get; init; }
    public bool? EnableContinuousBatching { get; init; }
    public bool? EnableCachePrompt { get; init; }
    public bool? EnableContextShift { get; init; }

    // GPU
    public int? GpuLayerCount { get; init; }       // null = auto
    public string? SplitMode { get; init; }         // "none" | "layer" | "row"
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

    // Model behavior
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
```

### 2.2 EvalSetConfig

```csharp
public record EvalSetConfig
{
    public string Id { get; init; } = "";          // unique within a run
    public string Name { get; init; } = "";
    public string PipelineName { get; init; } = ""; // registered pipeline type name
    public DataSourceConfig DataSource { get; init; } = new();
    public PipelineConfig Pipeline { get; init; } = new();
    public OutputConfig Output { get; init; } = new();
    public IReadOnlyDictionary<string, object?> PipelineOptions { get; init; }
        = new Dictionary<string, object?>();
}
```

### 2.3 RunMeta

```csharp
public record RunMeta
{
    public string RunName { get; init; } = "";
    public string OutputDirectoryPath { get; init; } = "./results";
    public ShellTarget ExportShellTarget { get; init; } = ShellTarget.Bash;
    public bool ContinueOnEvalFailure { get; init; } = true;
    public int? MaxConcurrentEvals { get; init; } // null = use total_slots from server
}

public enum ShellTarget { Bash, PowerShell }
```

---

## 3. Settings File Format

Settings files are YAML (preferred), JSON, or TOML. The file format is auto-detected by extension: `.yml`, `.yaml` → YAML; `.json` → JSON; `.toml` → TOML.

### 3.1 Example Settings File (YAML)

```yaml
# Seevalocal-settings.yml
run:
  name: "my-coding-eval"
  outputDirectoryPath: "./results"
  exportShellTarget: bash

server:
  manage: true
  model:
    source: localFile
    filePath: /models/phi-4-Q4_K_M.gguf
  host: 127.0.0.1
  port: 8080

llamaServer:
  contextWindowTokens: 8192
  parallelSlotCount: 4
  enableFlashAttention: true
  samplingTemperature: 0.2
  # All unset fields use llama-server defaults

evalSets:
  - id: csharp-coding
    name: "C# Coding Eval"
    pipelineName: CSharpCoding
    dataSource:
      kind: directory
      promptDirectoryPath: ./prompts
    pipeline:
      compileTimeoutSeconds: 30.0
      testTimeoutSeconds: 60.0
    output:
      includeRawResponse: true
```

---

## 4. Layered Merge Logic

```csharp
public sealed class ConfigurationMerger
{
    /// Merges settings files in order; later files override earlier ones.
    /// CLI overrides are applied last.
    public ResolvedConfig Merge(
        IReadOnlyList<PartialConfig> settingsFiles,
        PartialConfig cliOverrides);
}
```

### 4.1 Merge Rule

For each field in the settings hierarchy:
- If `cliOverrides` has a non-null value → use it.
- Else walk the `settingsFiles` list from last to first; use the first non-null value.
- If still null → field is unset (omit from server args / use defaults).

This is a **right-fold null-coalescing merge** over the list, followed by the CLI override.

### 4.2 PartialConfig

`PartialConfig` is structurally identical to `ResolvedConfig` but every leaf field is nullable. A YAML deserializer produces a `PartialConfig`; the merger produces a `ResolvedConfig`.

```csharp
// Auto-generated or hand-written parallel of ResolvedConfig with all fields nullable.
public record PartialConfig { ... }
```

A source generator or T4 template can derive `PartialConfig` from `ResolvedConfig` automatically.

---

## 5. Validation

```csharp
public sealed class ConfigValidator
{
    public IReadOnlyList<ValidationError> Validate(ResolvedConfig config);
}

public record ValidationError(string Field, string MessageText);
```

Validation rules:

- `Server.Manage = true` → `Model` must be set.
- `Server.Manage = false` → `BaseUrl` must be set and parseable as a URI.
- Each `EvalSetConfig.Id` must be unique within the run.
- `PipelineName` must match a registered pipeline.
- `OutputDirectoryPath` must be writable (or creatable).
- `ContextWindowTokens`, if set, must be > 0.
- `SamplingTemperature`, if set, must be in [0, 2].

---

## 6. Shell Script Export

```csharp
public sealed class ShellScriptExporter
{
    public string Export(ResolvedConfig config, ShellTarget target);
}
```

### 6.1 Bash Output Example

```bash
#!/usr/bin/env bash
# Generated by Seevalocal — run: my-coding-eval
# Timestamp: 2025-03-01T12:00:00Z

llama-server \
  -m /models/phi-4-Q4_K_M.gguf \
  --host 127.0.0.1 \
  --port 8080 \
  -c 8192 \
  -np 4 \
  -fa on \
  --temp 0.2 &

LLAMA_PID=$!

dotnet run --project Seevalocal.Cli -- \
  --settings ./Seevalocal-settings.yml \
  --server-url http://127.0.0.1:8080 \
  --output-dir ./results

kill $LLAMA_PID
```

### 6.2 PowerShell Output Example

```powershell
# Generated by Seevalocal — run: my-coding-eval

$llamaProcess = Start-Process llama-server.exe `
  -ArgumentList "-m /models/phi-4-Q4_K_M.gguf --host 127.0.0.1 --port 8080 -c 8192 -np 4 -fa on --temp 0.2" `
  -PassThru

dotnet run --project Seevalocal.Cli -- `
  --settings ./Seevalocal-settings.yml `
  --server-url http://127.0.0.1:8080 `
  --output-dir ./results

Stop-Process -Id $llamaProcess.Id
```

### 6.3 Escaping Rules

- String values containing spaces are quoted.
- Special characters in model paths are escaped per shell dialect.
- The exporter does **not** reproduce llama-server defaults — only explicitly set values appear.

---

## 7. Settings File Discovery

At startup, Seevalocal looks for a default settings file in this order:
1. `--settings` CLI flag(s)
2. `./Seevalocal.yml` in the current directory
3. `~/.Seevalocal/default.yml`

If none found, all settings default to null (llama-server defaults apply).

---

## 8. Unit Tests (Seevalocal.Config.Tests)

| Test class | Coverage |
|---|---|
| `ConfigurationMergerTests` | Two-file merge; CLI override wins; null passthrough |
| `ConfigValidatorTests` | Each validation rule; combined error collection |
| `ShellScriptExporterTests` | Bash output for known config; PowerShell output; null fields omitted |
| `SettingsFileLoaderTests` | YAML/JSON round-trip; unknown fields ignored; format auto-detect |
