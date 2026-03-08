# Seevalocal — System Overview & Architecture

> **Read `00-conventions.md` before this file.**

---

## 1. Purpose

Seevalocal is a cross-platform tool for running structured, repeatable evaluations of LLMs served by `llama-server` (llama.cpp). It handles the full lifecycle:

1. Optionally download and launch `llama-server` (including detecting CUDA vs. Vulkan vs. CPU)
2. Load a dataset from one of several formats
3. Submit prompts concurrently, up to the server's slot count
4. Collect structured, typed, multi-metric results per eval item
5. Optionally score results with a second LLM (LLM-as-judge) or with external processes
6. Write results to disk in JSON, CSV, and/or Parquet
7. Generate a shell script that exactly reproduces the run

It exposes all functionality through both a **CLI** (Spectre.Console.Cli) and an **Avalonia UI**, sharing all logic beneath the entry-point layer.

---

## 2. Generalized Problem Space

The following generalizations apply throughout:

| User's specific idea | Generalized concept |
|---|---|
| Prompts in one folder, answers in another | Any source pairing: two root directories, one file, one DB query, etc. |
| LLM-as-judge | *Scoring pipeline stage* backed by any HTTP-compatible LLM endpoint |
| Compile + unit tests | *External-process stage* producing typed metric fields |
| Prompts and answers in same file | *Unified dataset* with configurable field mapping |
| System prompt file | *Template injection* into any prompt field |
| Multiple quantities per eval | *Typed metric schema*: each eval type declares its output fields with name, type, and unit |
| Shell script export | *Reproducible run descriptor* serialized to the target shell dialect |
| Settings files | *Layered configuration* with explicit merge semantics |
| Multiple LLMs | *Named endpoint registry* — each eval set can target a different endpoint |

---

## 3. High-Level Component Map

```
┌─────────────────────────────────────────────────────────────┐
│                        Entry Points                         │
│  Seevalocal.Cli (Spectre)       Seevalocal.Ui (Avalonia)     │
└───────────────────────┬─────────────────────────────────────┘
                        │ calls
┌───────────────────────▼─────────────────────────────────────┐
│                    Seevalocal.Config                         │
│  Settings models · file loading · layer merging             │
└───────────────────────┬─────────────────────────────────────┘
                        │ produces ResolvedConfig
          ┌─────────────▼──────────────┐
          │    Seevalocal.Core          │
          │  Pipeline orchestrator     │
          │  IEvalPipeline, IEvalStage │
          │  ConcurrencyCoordinator    │
          └──┬──────┬──────┬──────┬───┘
             │      │      │      │
    ┌────────▼─┐ ┌──▼──┐ ┌▼────┐ ┌▼──────────┐
    │ .Server  │ │.Data│ │.Met-│ │  .Judge   │
    │ Lifecycle│ │Src  │ │rics │  │ LLM-as-j │
    │ HTTP API │ │Load │ │Coll-│ │ Scoring   │
    └──────────┘ └─────┘ └─────┘ └───────────┘
          │ all of the above used by
    ┌─────▼──────────────────────────────────┐
    │         Seevalocal.Pipelines            │
    │  Built-in: Translation, CSharpCoding, │
    │  CasualQA, Custom                      │
    └────────────────────────────────────────┘
```

---

## 4. Independent Implementation Parts

The following parts can each be implemented independently. Each has its own design file.

| File | Part | Summary |
|------|------|---------|
| `02-server.md` | Server Lifecycle & HTTP Client | Detecting GPU, downloading llama.cpp, launching llama-server, HTTP wrapper for all endpoints |
| `03-config.md` | Configuration System | Settings models, file formats, CLI binding, layer merging, shell script export |
| `04-datasources.md` | Data Source Abstraction | Loading eval items from files/folders/JSON/YAML/CSV/Parquet, template injection, field mapping |
| `05-core-pipeline.md` | Pipeline Core | Orchestrator, concurrency control, stage interfaces, result collection |
| `06-metrics.md` | Metrics & Output | Typed metric schema, aggregation, CSV/Parquet/JSON writers, per-eval and summary outputs |
| `07-judge.md` | LLM-as-Judge | Scoring stage, judge endpoint config, prompt templates, parsing judge responses |
| `08-pipelines.md` | Built-in Pipelines | Translation, C# Coding, Casual QA — each self-contained with auto-setup |
| `09-ui-cli.md` | UI & CLI | Spectre CLI commands, Avalonia screens, guided setup wizard, shell script export |
| `10-interfaces.md` | Cross-Component Interfaces | Authoritative interface definitions and shared record types |

---

## 5. Data Flow (Single Eval Item)

```
DataSource
  └─ EvalItem { Id, SystemPrompt?, UserPrompt, ExpectedOutput?, Metadata }
        │
        ▼
PipelineStage: PromptStage
  └─ sends to llama-server /v1/chat/completions
  └─ produces: LlmResponse { Content, PromptTokenCount, CompletionTokenCount, DurationSeconds }
        │
        ▼
PipelineStage: ScoringStage(s) [0 or more, run sequentially per item]
  ├─ ExternalProcessStage → runs compiler/tests, parses stdout/stderr
  ├─ JudgeStage           → sends to judge LLM, parses structured score
  └─ ExactMatchStage      → string comparison with normalization options
        │
        ▼
MetricCollector
  └─ EvalResult { EvalItemId, Metrics: Dictionary<string, MetricValue>, RawResponse, ... }
        │
        ▼
ResultWriter → results/run-<ts>/eval-<id>.json + summary
```

---

## 6. Concurrency Model

At pipeline start:

1. `GET /props` is called on the target endpoint.
2. `total_slots` is extracted.
3. A `SemaphoreSlim(total_slots)` gates concurrent eval item dispatch.
4. Items are dispatched via `System.Threading.Channels.Channel<EvalItem>`.
5. A configurable number of consumer tasks drain the channel, each acquiring the semaphore before calling the server.

If multiple endpoints are configured (e.g., judge + target), each has its own semaphore sized from its own `total_slots`.

---

## 7. Settings Priority (Summary)

```
Explicit flag / programmatic set
  ↓ falls back to
Settings file N (last --settings)
  ↓ falls back to
Settings file N-1
  ...
  ↓ falls back to
Settings file 1 (first --settings)
  ↓ falls back to
(omit field — llama-server uses its own default)
```

---

## 8. Extensibility Contract

Users who want to add a custom pipeline stage:

1. Implement `IEvalStage` (defined in `10-interfaces.md`).
2. Register it in the DI container.
3. Reference it by name in a settings file or wire it in `Program.cs`.

No scripting language, no node editor — just C# code. The design ensures the pipeline is a plain `IReadOnlyList<IEvalStage>` that is straightforward to modify.

---

## 9. Deliverable Summary per Part

Each design file (`02` through `09`) must produce, at minimum:

- One or more C# projects with `*.csproj`
- All types and interfaces mentioned (even if bodies are stubbed)
- Complete unit tests for all non-trivial logic
- Any embedded resources (Jinja templates, default configs)

`10-interfaces.md` is *design only* — the interfaces defined there are *implemented* in the project specified in each interface's documentation.
