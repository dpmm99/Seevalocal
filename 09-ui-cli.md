# Seevalocal — Part 8: UI & CLI

> **Read `00-conventions.md` before this file.**  
> Interfaces referenced here are defined in `10-interfaces.md`.  
> CLI project: `Seevalocal.Cli` | UI project: `Seevalocal.Ui`

---

## 1. Responsibilities

- Provide a Spectre.Console.Cli entry point with all settings expressible as flags.
- Provide an Avalonia desktop UI with a guided wizard, real-time progress, and results viewer.
- Share 100% of logic below the entry-point layer (no business logic in CLI or UI projects).
- Export the current configuration as a shell script from both interfaces.
- Settings files can be passed in; their contents populate both CLI defaults and UI fields.

---

## 2. CLI Design (Seevalocal.Cli)

### 2.1 Command Structure

```
Seevalocal
├── run                 # Run an eval (primary command)
├── validate            # Validate a settings file without running
├── export-script       # Export shell script from a settings file
├── server
│   ├── start           # Start llama-server only (for debugging)
│   └── check           # Check if a URL is a healthy llama-server
└── pipeline list       # List registered pipeline names and descriptions
```

### 2.2 `run` Command Options

All options are nullable where the underlying setting is nullable. CLI uses `--option value`; boolean options use `--enable-X` / `--disable-X` / `--X` / `--no-X` pairs.

```
Seevalocal run [OPTIONS] [--settings FILE]... 

Server options:
  --manage                      Start and manage llama-server
  --no-manage                   Connect to existing server
  --executable PATH             Path to llama-server binary (implies --manage)
  --model-file PATH             Local model file path (implies --manage)
  --hf-repo REPO[:QUANT]        HuggingFace repo (implies --manage)
  --hf-token TOKEN              HuggingFace token
  --server-url URL              Connect to existing server (implies --no-manage)
  --api-key KEY                 API key for server authentication
  --host HOST                   llama-server host (default: 127.0.0.1)
  --port PORT                   llama-server port (default: 8080)

llama-server tuning (all optional — unset = server default):
  --ctx TOKENS                  Context window size
  --batch TOKENS                Batch size
  --ubatch TOKENS               Micro-batch size
  --parallel N                  Parallel slots (concurrent requests)
  --ngl N                       GPU layers
  --flash-attn / --no-flash-attn
  --cache-prompt / --no-cache-prompt
  --context-shift / --no-context-shift
  --kv-type-k TYPE              KV cache type for K (f16, q8_0, etc.)
  --kv-type-v TYPE              KV cache type for V
  --threads N                   CPU threads
  --temp FLOAT                  Sampling temperature
  --top-p FLOAT                 Top-p
  --top-k INT                   Top-k
  --min-p FLOAT                 Min-p
  --seed INT                    Random seed
  --chat-template NAME          Chat template name
  --reasoning-format FORMAT     Reasoning format
  --log-verbosity N             Log verbosity level
  --extra-arg ARG               Pass-through arg to llama-server (repeatable)

Eval options:
  --pipeline NAME               Pipeline to run (Translation, CSharpCoding, CasualQA, or custom)
  --prompt-dir PATH             Directory of prompt files
  --expected-dir PATH           Directory of expected output files
  --data-file PATH              Unified data file (JSON/YAML/CSV/Parquet/JSONL)
  --system-prompt TEXT          System prompt (inline)
  --system-prompt-file PATH     System prompt file
  --max-items N                 Max eval items to process
  --shuffle-seed INT            Shuffle dataset with this seed

Judge options:
  --judge-url URL               Judge LLM endpoint URL
  --judge-model-file PATH       Judge model file (if managing judge)
  --judge-hf-repo REPO[:QUANT]  Judge HuggingFace repo
  --judge-api-key KEY
  --judge-template NAME         Judge prompt template (standard, pass-fail, json)
  --judge-score-min FLOAT       Min score value (default: 0)
  --judge-score-max FLOAT       Max score value (default: 10)

Output options:
  --output-dir PATH             Results output directory
  --run-name NAME               Human name for this run
  --shell bash|powershell       Shell dialect for exported script
  --no-parquet                  Skip Parquet output
  --no-raw-response             Omit raw LLM responses from output

Run control:
  --settings FILE               Settings file (repeatable; later overrides earlier)
  --yes                         Skip confirmation prompts
  --max-concurrent N            Override concurrency (default: total_slots from server)
  --continue-on-failure         Continue if an item fails (default: true)
  --stop-on-failure             Stop run on first item failure
  --timeout-seconds FLOAT       Per-item timeout
  --retry-count N               Retries on transient failure (default: 2)
```

### 2.3 Settings Priority in CLI

```
CLI flags → settings file (last to first) → llama-server defaults
```

The `--settings` flag may appear multiple times. Files are loaded in the order specified.

### 2.4 CLI Progress Display

Uses Spectre.Console's `Progress` with:
- One task per eval set
- Completed / total count
- Current tokens/second (rolling average)
- Estimated time remaining

```
[■■■■■■■■■░░░░░] 80/100  Translation Eval   42.3 tok/s  ETA: 0:01:12
```

### 2.5 `export-script` Command

```
Seevalocal export-script --settings FILE --shell bash|powershell [--output FILE]
```

Reads the settings file, resolves the config, calls `ShellScriptExporter.Export`, writes to stdout or `--output FILE`.

---

## 3. Avalonia UI Design (Seevalocal.Ui)

### 3.1 Application Structure

```
MainWindow
├── NavigationSidebar
│   ├── Setup Wizard (step indicator)
│   ├── Run Dashboard
│   ├── Results Viewer
│   └── Settings
└── ContentArea
    ├── WizardView
    ├── RunDashboardView
    ├── ResultsView
    └── SettingsView
```

### 3.2 Setup Wizard Steps

The wizard guides the user step-by-step. Each step has a title, help text, and input controls. Navigation: Back / Next / Skip (for optional steps).

| Step | Title | Key Controls |
|---|---|---|
| 1 | Model & Server | Radio: "Manage server" / "Connect to existing" → branches |
| 1a (Manage) | Model Source | Radio: "Local file" / "HuggingFace" → file picker or HF repo input |
| 1b (Connect) | Server URL | URL input + "Test Connection" button |
| 2 | Performance Settings | Sliders/spinners for ctx, parallel, GPU layers; all labeled "(optional — uses server default if blank)" |
| 3 | Evaluation Dataset | Radio: pipeline type → tailored data source inputs |
| 4 | Scoring | Toggle: LLM-as-judge → judge server inputs + template selector |
| 5 | Output | Output dir picker; run name; shell dialect for export |
| 6 | Review & Run | Summary of all settings; "Export Script" button; "Start Run" button |

### 3.3 Contextual Help System

Each setting in the wizard has an adjacent `(?)` icon. Clicking it opens a tooltip/popover with:
- What the setting does (from llama-server docs summary)
- When to change it from the default
- Typical values for different use cases
- Link to relevant llama-server documentation section

Example for "Context Window":
> **Context Window (tokens)**  
> The maximum number of tokens the model can "see" at once, including your prompt and its response.  
> **When to change:** If your prompts + expected responses are long (e.g., >2K tokens), increase this. Larger values use more VRAM.  
> **Typical values:** 2048 (small models), 4096–8192 (general use), 32768+ (long context tasks).  
> *Leave blank to use the model's built-in default.*

### 3.4 Run Dashboard View

While a run is active:
- Overall progress bar (items completed / total)
- Per-eval-set progress bars
- Live metrics table: current avg tok/s, items/min, pass rate
- Scrolling log of recent completions (item ID, status, key metrics)
- "Cancel Run" button (graceful: completes in-flight items)
- "Pause" button (stops dispatching new items; in-flight items finish)

### 3.5 Results Viewer

After a run (or by loading a `summary.json`):
- Summary card: total items, pass rate, avg latency, avg score
- Metric distribution charts (histogram per metric, using Avalonia charting)
- Sortable/filterable table of all eval items with their metrics
- Clicking a row expands to show: prompt, expected output, actual output, judge rationale
- "Export to CSV" button
- "Export Shell Script" button (opens dialect picker dialog)
- "Open Results Folder" button

### 3.6 Settings View

- Tree-structured settings editor mirroring the `ResolvedConfig` hierarchy
- Each field: label, input control appropriate to type, "(optional)" annotation where applicable
- "Load Settings File" / "Save Settings File" buttons
- "Reset to Defaults" (clears all to null)
- Search box that filters visible settings by name

### 3.7 Settings File Load/Save in UI

- On startup: look for `./Seevalocal.yml` and `~/.Seevalocal/default.yml`; if found, load and populate the UI
- "Load Settings File": opens file picker; merges into current settings per the layering rules
- "Save Settings File": serializes current UI state to YAML at user-chosen path
- The title bar shows "Seevalocal — [run-name] [modified indicator]" à la typical desktop apps

### 3.8 Shell Script Export Dialog

Triggered from Wizard step 6 or Results Viewer:

```
┌─ Export Shell Script ─────────────────────────────────────┐
│                                                           │
│  Shell dialect:  ○ Bash (Linux/macOS)  ○ PowerShell      │
│                                                           │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ #!/usr/bin/env bash                                  │ │
│  │ # Generated by Seevalocal — run: my-coding-eval      │ │
│  │ ...                                                  │ │
│  └──────────────────────────────────────────────────────┘ │
│                                                           │
│  [Copy to Clipboard]     [Save As...]     [Close]         │
└───────────────────────────────────────────────────────────┘
```

### 3.9 Multi-Settings-File Support in UI

The settings panel shows a **layered stack** of loaded settings files:

```
Settings Stack (bottom = highest priority)
─────────────────────────────────────────
[×] ~/.Seevalocal/default.yml     (base)
[×] ./my-model.yml               (overlay 1)
[×] ./translation-eval.yml       (overlay 2 — highest file priority)
[+ Add File]
─────────────────────────────────────────
[CLI / Session Overrides]        (always highest)
```

Hovering over a field shows which layer is providing the current value.

---

## 4. Shared ViewModel Layer

Both CLI and UI use the same `EvalRunViewModel` that wraps the `PipelineOrchestrator`:

```csharp
public sealed class EvalRunViewModel : INotifyPropertyChanged, IDisposable
{
    public ResolvedConfig Config { get; }
    public ObservableCollection<EvalResultViewModel> Results { get; }
    public double ProgressRatioPercent { get; private set; }
    public bool IsRunning { get; private set; }
    public RunSummary? Summary { get; private set; }

    public Task StartAsync(CancellationToken ct);
    public void Cancel();
}
```

The CLI renders `EvalRunViewModel` state via Spectre; the UI binds to it via Avalonia MVVM.

---

## 5. Unit/Integration Tests (Seevalocal.Cli.Tests, Seevalocal.Ui.Tests)

| Test class | Coverage |
|---|---|
| `CliArgumentParserTests` | Each flag sets correct field; null fields stay null; --settings layering |
| `CliRunCommandTests` | End-to-end with mock pipeline; progress written to Spectre; exit codes |
| `ShellScriptRoundTripTests` | Export script → re-parse args → same config |
| `WizardViewModelTests` | Step navigation; validation prevents Next on invalid step |
| `SettingsFileLoadSaveTests` | Round-trip YAML; multi-file stack ordering |
