# Seevalocal — Cross-Cutting Conventions

> **Every agent implementing any part of this system must read this file first.**
> These conventions apply uniformly to all components. Nothing in another design file overrides them unless that file explicitly says it does and explains why.

---

## 1. Project Identity

| Item | Value |
|------|-------|
| Solution name | `Seevalocal` |
| Root namespace | `Seevalocal` |
| Target framework | `net10.0` |
| Language | C# 14 |
| Nullable reference types | **enabled** everywhere |
| Implicit usings | enabled |

---

## 2. Naming Rules

### 2.1 Units in Scalar Names

Any variable, property, field, parameter, or record member that holds a numeric measurement **must** include the unit as a suffix in its name. There are no exceptions.

```
// WRONG
double timeout;
int tokens;
long fileSize;

// CORRECT
double timeoutSeconds;
int tokenCount;           // "count" is acceptable for pure integers
long fileSizeBytes;
double promptTokensPerSecond;
int contextWindowTokens;
```

This applies to JSON/YAML/TOML keys in configuration files, column names in result CSV/Parquet output, and metric names in any report.

### 2.2 Standard Suffixes

| Concept | Suffix |
|---------|--------|
| Duration | `...Seconds`, `...Milliseconds`, `...Minutes` |
| Memory | `...Bytes`, `...Mib`, `...Gib` |
| Count of discrete items | `...Count` |
| Ratio / fraction (0–1) | `...Ratio` |
| Percentage (0–100) | `...Percent` |
| Temperature (LLM sampling) | `...Temperature` (dimensionless, but the word is the unit) |
| Tokens (LLM) | `...Tokens` or `...TokenCount` |

### 2.3 General C# Conventions

- **PascalCase**: types, methods, properties, constants, enum members
- **camelCase**: local variables, parameters, private fields
- **`_camelCase`**: private instance fields (underscore prefix)
- Interfaces prefixed with `I`: `IEvalPipeline`
- No Hungarian notation (except the unit suffix rule above)
- Async methods suffixed `Async`: `RunEvalAsync`

---

## 3. Error Handling

- Use `Result<T, TError>` (from `FluentResults` or a lightweight bespoke type — see §8) rather than exceptions for expected failures (network errors, missing files, bad config).
- Reserve exceptions for **programming errors** (null dereferences, argument misuse).
- Every public async method that can fail must return `Task<Result<T>>` or `Task<Result>`.
- Log at `Error` level before returning a failure result. Never swallow.

---

## 4. Cancellation

Every long-running operation must accept a `CancellationToken` as its final parameter. If the caller has no token, pass `CancellationToken.None`. Never hardcode timeouts inside a method — accept them as `double timeoutSeconds` parameters from config.

---

## 5. Logging

- Use `Microsoft.Extensions.Logging.ILogger<T>` everywhere.
- Log levels: `Trace` (per-token detail), `Debug` (pipeline step entry/exit), `Information` (eval start/finish), `Warning` (retries, missing optional config), `Error` (recoverable failure), `Critical` (unrecoverable, process exits).
- Structured logging with named holes: `_logger.LogInformation("Eval {EvalId} completed in {DurationSeconds:F2}s", id, elapsed)`.

---

## 6. Configuration Layering

Settings are resolved in this priority order (highest first):

1. Explicit CLI flags / programmatic override
2. Each settings file passed via `--settings <path>` (later files override earlier ones)
3. LlamaServer defaults (never reproduced in code — simply omit the field if not set)

A setting that is "not set" is represented as `null` for nullable value types (`int?`, `double?`, `bool?`) or as `Option<T>` if using a discriminated union library. **Never** use sentinel values like `-1` for "unset"; use `null`.

---

## 7. Tri-State Booleans

For any llama-server boolean option, the C# model uses `bool?`:

```csharp
bool? enableFlashAttention;   // null = use llama-server default, true/false = explicit
```

When serializing to a shell script or HTTP request body, `null` fields are simply omitted.

---

## 8. Shared NuGet Packages

All projects in the solution share these packages (defined in `Directory.Packages.props`):

| Package | Purpose |
|---------|---------|
| `Spectre.Console` | CLI rendering & progress |
| `Spectre.Console.Cli` | CLI command/option parsing |
| `Avalonia` + `Avalonia.Desktop` | Cross-platform UI |
| `Microsoft.Extensions.Logging` | Logging abstraction |
| `Serilog` + sinks | Logging implementation |
| `System.Text.Json` | JSON serialization |
| `YamlDotNet` | YAML configuration |
| `CsvHelper` | CSV reading/writing |
| `Parquet.Net` | Parquet reading/writing |
| `Polly` | Retry policies |
| `FluentResults` | `Result<T>` type |
| `Nito.AsyncEx` | `AsyncLock`, `AsyncManualResetEvent` |
| `MessagePipe` | In-process event bus |

---

## 9. Project Structure

```
Seevalocal.sln
├── src/
│   ├── Seevalocal.Core/            # Domain models, interfaces, pipeline abstractions
│   ├── Seevalocal.Server/          # llama-server lifecycle + HTTP client
│   ├── Seevalocal.Config/          # Settings models, file I/O, layering logic
│   ├── Seevalocal.DataSources/     # Dataset loading (files, folders, JSON, YAML, CSV, Parquet)
│   ├── Seevalocal.Metrics/         # Metric collection, aggregation, output
│   ├── Seevalocal.Pipelines/       # Built-in pipeline implementations
│   ├── Seevalocal.Judge/           # LLM-as-judge logic
│   ├── Seevalocal.Cli/             # Spectre.Console.Cli entry point
│   └── Seevalocal.Ui/              # Avalonia entry point
└── tests/
    ├── Seevalocal.Core.Tests/
    ├── Seevalocal.Server.Tests/
    └── ...
```

Each `src/` project references only the projects *below* it in the list above (Core is the base; Cli and Ui are the leaves). The dependency arrow is always downward.

---

## 10. Async Concurrency Model

- The degree of concurrency (how many evals run simultaneously) equals `total_slots` from `GET /props`, fetched at pipeline start.
- Use `System.Threading.Channels` + `Parallel.ForEachAsync` (or a `SemaphoreSlim`) to limit concurrency.
- All shared mutable state must be protected. Prefer immutable records and message-passing over locks.

---

## 11. Platform Portability

- File paths: always use `Path.Combine` and `Path.GetFullPath`; never hardcode `/` or `\`.
- Shell script export: detect target shell (bash vs. PowerShell) from a UI dropdown or `--shell` flag.
- Process launching: use `System.Diagnostics.Process` with `UseShellExecute = false`.
- No P/Invoke that is platform-specific unless wrapped in a platform guard.

---

## 12. Interfaces That Cross Component Boundaries

See **`10-interfaces.md`** for the full, authoritative interface definitions. Any interface mentioned by name in another design file is defined there. Do not redefine it locally.

---

## 13. Test Requirements

- Every public method in `Seevalocal.Core` must have at least one unit test.
- Integration tests that call a real llama-server must be in an `[Integration]` xUnit trait category and skipped by default in CI.
- Use `Testcontainers` for Docker-based integration tests where feasible.

---

## 14. Output File Formats

Results are always written to a **results directory** specified by `--output-dir`. Within it:

```
results/
├── run-<timestamp>/
│   ├── summary.json          # aggregated metrics for the whole run
│   ├── summary.csv           # same, flattened for spreadsheet use
│   ├── eval-<id>.json        # per-eval detailed output
│   └── export-script.sh/.ps1 # the shell script that reproduces this run
```

All JSON files use camelCase keys. All CSV/Parquet column names follow the unit-suffix convention (§2.1).
