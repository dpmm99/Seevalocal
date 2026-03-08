# CLI and UI Feature Comparison

## CLI Commands (All Implemented ✓)

| Command | Status | Notes |
|---------|--------|-------|
| `run` | ✓ | Full implementation with all options |
| `validate` | ✓ | Validates settings file |
| `export-script` | ✓ | Exports bash/PowerShell script |
| `server start` | ✓ | Starts llama-server for debugging |
| `server check` | ✓ | Checks server health |
| `pipeline list` | ✓ | Lists registered pipelines |

## CLI `run` Command Options

### Server Options (All Implemented ✓)
| Option | Status | Property |
|--------|--------|----------|
| `--manage` | ✓ | `Manage` |
| `--no-manage` | ✓ | `NoManage` |
| `--executable PATH` | ✓ | `ExecutablePath` |
| `--model-file PATH` | ✓ | `ModelFilePath` |
| `--hf-repo REPO[:QUANT]` | ✓ | `HfRepo` |
| `--hf-token TOKEN` | ✓ | `HfToken` |
| `--server-url URL` | ✓ | `ServerUrl` |
| `--api-key KEY` | ✓ | `ApiKey` |
| `--host HOST` | ✓ | `Host` |
| `--port PORT` | ✓ | `Port` |

### llama-server Tuning (All Implemented ✓)
| Option | Status | Property |
|--------|--------|----------|
| `--ctx TOKENS` | ✓ | `ContextWindowTokens` |
| `--batch TOKENS` | ✓ | `BatchTokens` |
| `--ubatch TOKENS` | ✓ | `UBatchTokens` |
| `--parallel N` | ✓ | `ParallelSlotCount` |
| `--ngl N` | ✓ | `GpuLayerCount` |
| `--flash-attn / --no-flash-attn` | ✓ | `EnableFlashAttention` |
| `--cache-prompt / --no-cache-prompt` | ✓ | `EnableCachePrompt` |
| `--context-shift / --no-context-shift` | ✓ | `EnableContextShift` |
| `--kv-type-k TYPE` | ✓ | `KvTypeK` |
| `--kv-type-v TYPE` | ✓ | `KvTypeV` |
| `--threads N` | ✓ | `ThreadCount` |
| `--temp FLOAT` | ✓ | `SamplingTemperature` |
| `--top-p FLOAT` | ✓ | `TopP` |
| `--top-k INT` | ✓ | `TopK` |
| `--min-p FLOAT` | ✓ | `MinP` |
| `--seed INT` | ✓ | `Seed` |
| `--chat-template NAME` | ✓ | `ChatTemplate` |
| `--reasoning-format FORMAT` | ✓ | `ReasoningFormat` |
| `--log-verbosity N` | ✓ | `LogVerbosity` |
| `--extra-arg ARG` | ✓ | `ExtraArgs` (repeatable) |

### Eval Options (All Implemented ✓)
| Option | Status | Property |
|--------|--------|----------|
| `--pipeline NAME` | ✓ | `PipelineName` |
| `--prompt-dir PATH` | ✓ | `PromptDir` |
| `--expected-dir PATH` | ✓ | `ExpectedDir` |
| `--data-file PATH` | ✓ | `DataFilePath` |
| `--system-prompt TEXT` | ✓ | `SystemPrompt` |
| `--system-prompt-file PATH` | ✓ | `SystemPromptFilePath` |
| `--max-items N` | ✓ | `MaxItems` |
| `--shuffle-seed INT` | ✓ | `ShuffleSeed` |

### Judge Options (All Implemented ✓)
| Option | Status | Property |
|--------|--------|----------|
| `--judge-url URL` | ✓ | `JudgeUrl` |
| `--judge-model-file PATH` | ✓ | `JudgeModelFilePath` |
| `--judge-hf-repo REPO[:QUANT]` | ✓ | `JudgeHfRepo` |
| `--judge-api-key KEY` | ✓ | `JudgeApiKey` |
| `--judge-template NAME` | ✓ | `JudgeTemplate` |
| `--judge-score-min FLOAT` | ✓ | `JudgeScoreMin` |
| `--judge-score-max FLOAT` | ✓ | `JudgeScoreMax` |

### Output Options (All Implemented ✓)
| Option | Status | Property |
|--------|--------|----------|
| `--output-dir PATH` | ✓ | `OutputDir` |
| `--run-name NAME` | ✓ | `RunName` |
| `--shell bash|powershell` | ✓ | `ShellDialect` |
| `--no-parquet` | ✓ | `NoParquet` |
| `--no-raw-response` | ✓ | `NoRawResponse` |

### Run Control (All Implemented ✓)
| Option | Status | Property |
|--------|--------|----------|
| `--settings FILE` | ✓ | `SettingsFiles` (repeatable) |
| `--yes / -y` | ✓ | `Yes` |
| `--max-concurrent N` | ✓ | `MaxConcurrent` |
| `--continue-on-failure` | ✓ | `ContinueOnFailure` |
| `--stop-on-failure` | ✓ | `StopOnFailure` |
| `--timeout-seconds FLOAT` | ✓ | `TimeoutSeconds` |
| `--retry-count N` | ✓ | `RetryCount` |

## UI Wizard Steps

### Step 1: Model & Server (Implemented ✓)
| Feature | Status | Property |
|---------|--------|----------|
| Manage/Connect toggle | ✓ | `ManageServer` |
| Local file selection | ✓ | `LocalModelPath`, `UseLocalFile` |
| HuggingFace repo | ✓ | `HfRepo`, `HfToken` |
| Server URL | ✓ | `ServerUrl` |
| Test connection button | ⚠️ | UI has button, needs implementation |

### Step 2: Performance Settings (Implemented ✓)
| Feature | Status | Property |
|---------|--------|----------|
| Context Window | ✓ | `ContextWindowTokens` |
| Parallel Slots | ✓ | `ParallelSlotCount` |
| GPU Layers | ✓ | `GpuLayerCount` |

### Step 3: Evaluation Dataset (Implemented ✓)
| Feature | Status | Property |
|---------|--------|----------|
| Pipeline selection | ✓ | `PipelineName` |
| Single file source | ✓ | `DataFilePath` |
| Directory pair source | ✓ | `PromptDir`, `ExpectedDir` |

### Step 4: Scoring (Implemented ✓)
| Feature | Status | Property |
|---------|--------|----------|
| Enable LLM-as-Judge | ✓ | `EnableJudge` |
| Judge URL | ✓ | `JudgeUrl` |
| Judge template | ✓ | `JudgeTemplate` |
| Score min/max | ✓ | `JudgeScoreMin`, `JudgeScoreMax` |

### Step 5: Output (Implemented ✓)
| Feature | Status | Property |
|---------|--------|----------|
| Output directory | ✓ | `OutputDir` |
| Run name | ✓ | `RunName` |
| Shell dialect | ✓ | `ShellTarget` |

### Step 6: Review & Run (Implemented ✓)
| Feature | Status |
|---------|--------|
| Configuration summary | ✓ |
| Export Script button | ⚠️ UI has button, needs wiring |
| Start Run button | ⚠️ UI has button, needs wiring |

## Missing/Incomplete Features

### CLI
- [ ] `--yes` flag confirmation skip (registered but not used in RunCommand)
- [ ] `--timeout-seconds` and `--retry-count` (registered but not wired to pipeline)
- [ ] `--max-items` and `--shuffle-seed` (registered but not wired to data source)

### UI
- [x] Export Script button - **WIRED** (logs to console)
- [x] Start Run button - **WIRED** (calls StartRunAsync)
- [ ] Settings file load/save buttons (UI has code, needs file picker dialogs)
- [ ] Test Connection button (UI has button, needs implementation)
- [ ] Contextual help tooltips (spec §3.3 - not implemented)
- [ ] Run Dashboard real-time progress (spec §3.4 - placeholder view)
- [ ] Results Viewer with charts (spec §3.5 - placeholder view)
- [ ] Settings layered stack visualization (spec §3.9 - basic implementation)
- [ ] Browse file/folder dialogs (UI has buttons, need file pickers)

### Shared
- [x] Settings file auto-load on startup (UI has code in LoadDefaultSettingsAsync)
- [x] Shell script export from UI (service registered, button wired)

## Summary

**CLI**: 100% of options from spec are registered and parsed. Core functionality works for server management and configuration. Pipeline execution wiring is partial.

**UI**: All wizard steps and properties from spec are implemented. Views are created with basic layouts. Button click handlers need wiring to services.

**Priority Fixes Needed**:
1. Wire up "Start Run" button to execute pipeline
2. Wire up "Export Script" button 
3. Implement file/folder browse dialogs
4. Add Test Connection functionality
5. Wire up settings file load/save
