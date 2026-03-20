using Spectre.Console.Cli;
using System.ComponentModel;

namespace Seevalocal.UI.Commands;

/// <summary>
/// Base settings shared by commands that need server + eval options.
/// Every nullable property maps to a nullable config field; null = "not set".
/// </summary>
public class RunCommandSettings : CommandSettings
{
    // ─── Server options ───────────────────────────────────────────────────────

    [CommandOption("--manage")]
    [Description("Start and manage llama-server")]
    public bool? Manage { get; set; }

    [CommandOption("--no-manage")]
    [Description("Connect to an existing server (sets Manage = false)")]
    public bool NoManage { get; set; }

    [CommandOption("--executable")]
    [Description("Path to llama-server binary (implies --manage)")]
    public string? ExecutablePath { get; set; }

    [CommandOption("--model-file")]
    [Description("Local model file path (implies --manage)")]
    public string? ModelFilePath { get; set; }

    [CommandOption("--hf-repo")]
    [Description("HuggingFace repo REPO:QUANT (implies --manage)")]
    public string? HfRepo { get; set; }

    [CommandOption("--hf-token")]
    [Description("HuggingFace API token")]
    public string? HfToken { get; set; }

    [CommandOption("--server-url")]
    [Description("Connect to existing server at URL (implies --no-manage)")]
    public string? ServerUrl { get; set; }

    [CommandOption("--api-key")]
    [Description("API key for server authentication")]
    public string? ApiKey { get; set; }

    [CommandOption("--host")]
    [Description("llama-server host (default: 127.0.0.1)")]
    public string? Host { get; set; }

    [CommandOption("--port")]
    [Description("llama-server port (default: 8080)")]
    public int? Port { get; set; }

    // ─── llama-server tuning ──────────────────────────────────────────────────

    [CommandOption("--ctx")]
    [Description("Context window size in tokens")]
    public int? ContextWindowTokens { get; set; }

    [CommandOption("--batch")]
    [Description("Batch size in tokens")]
    public int? BatchTokens { get; set; }

    [CommandOption("--ubatch")]
    [Description("Micro-batch size in tokens")]
    public int? UBatchTokens { get; set; }

    [CommandOption("--parallel")]
    [Description("Number of parallel slots (concurrent requests)")]
    public int? ParallelSlotCount { get; set; }

    [CommandOption("--ngl")]
    [Description("Number of GPU layers")]
    public int? GpuLayerCount { get; set; }

    [CommandOption("--flash-attn|--no-flash-attn")]
    [Description("Enable or disable flash attention")]
    public bool? EnableFlashAttention { get; set; }

    [CommandOption("--cache-prompt|--no-cache-prompt")]
    [Description("Enable or disable prompt caching")]
    public bool? EnableCachePrompt { get; set; }

    [CommandOption("--context-shift|--no-context-shift")]
    [Description("Enable or disable context shift")]
    public bool? EnableContextShift { get; set; }

    [CommandOption("--kv-type-k")]
    [Description("KV cache type for K, e.g., f16, q8_0")]
    public string? KvTypeK { get; set; }

    [CommandOption("--kv-type-v")]
    [Description("KV cache type for V")]
    public string? KvTypeV { get; set; }

    [CommandOption("--threads")]
    [Description("Number of CPU threads")]
    public int? ThreadCount { get; set; }

    [CommandOption("--temp")]
    [Description("Sampling temperature")]
    public double? SamplingTemperature { get; set; }

    [CommandOption("--top-p")]
    [Description("Top-p sampling")]
    public double? TopP { get; set; }

    [CommandOption("--top-k")]
    [Description("Top-k sampling")]
    public int? TopK { get; set; }

    [CommandOption("--min-p")]
    [Description("Min-p sampling")]
    public double? MinP { get; set; }

    [CommandOption("--seed")]
    [Description("Random seed")]
    public int? Seed { get; set; }

    [CommandOption("--chat-template")]
    [Description("Chat template name")]
    public string? ChatTemplate { get; set; }

    [CommandOption("--reasoning-format")]
    [Description("Reasoning format")]
    public string? ReasoningFormat { get; set; }

    [CommandOption("--log-verbosity")]
    [Description("Log verbosity level")]
    public int? LogVerbosity { get; set; }

    [CommandOption("--extra-arg")]
    [Description("Pass-through argument to llama-server (repeatable)")]
    public string[]? ExtraArgs { get; set; }

    // ─── Eval options ─────────────────────────────────────────────────────────

    [CommandOption("--pipeline")]
    [Description("Pipeline to run (Translation, CSharpCoding, CasualQA, or custom)")]
    public string? PipelineName { get; set; }

    [CommandOption("--prompt-dir")]
    [Description("Directory of prompt files")]
    public string? PromptDir { get; set; }

    [CommandOption("--expected-dir")]
    [Description("Directory of expected output files")]
    public string? ExpectedDir { get; set; }

    [CommandOption("--data-file")]
    [Description("Unified data file (JSON/YAML/CSV/Parquet/JSONL)")]
    public string? DataFilePath { get; set; }

    [CommandOption("--system-prompt")]
    [Description("System prompt text (inline)")]
    public string? SystemPrompt { get; set; }

    [CommandOption("--system-prompt-file")]
    [Description("Path to system prompt file")]
    public string? SystemPromptFilePath { get; set; }

    [CommandOption("--max-items")]
    [Description("Maximum eval items to process")]
    public int? MaxItems { get; set; }

    [CommandOption("--shuffle-seed")]
    [Description("Shuffle dataset with this seed")]
    public int? ShuffleSeed { get; set; }

    // ─── Judge options ────────────────────────────────────────────────────────

    [CommandOption("--judge-url")]
    [Description("Judge LLM endpoint URL")]
    public string? JudgeUrl { get; set; }

    [CommandOption("--judge-model-file")]
    [Description("Judge model file path (if managing judge server)")]
    public string? JudgeModelFilePath { get; set; }

    [CommandOption("--judge-hf-repo")]
    [Description("Judge HuggingFace repo REPO:QUANT")]
    public string? JudgeHfRepo { get; set; }

    [CommandOption("--judge-api-key")]
    [Description("API key for judge server")]
    public string? JudgeApiKey { get; set; }

    [CommandOption("--judge-template")]
    [Description("Judge prompt template (standard, pass-fail, json)")]
    public string? JudgeTemplate { get; set; }

    // ─── Output options ───────────────────────────────────────────────────────

    [CommandOption("--output-dir")]
    [Description("Results output directory")]
    public string? OutputDir { get; set; }

    [CommandOption("--run-name")]
    [Description("Human-readable name for this run")]
    public string? RunName { get; set; }

    [CommandOption("--shell")]
    [Description("Shell dialect for exported script (bash|powershell)")]
    public string? ShellDialect { get; set; }

    [CommandOption("--no-parquet")]
    [Description("Skip Parquet output")]
    public bool NoParquet { get; set; }

    [CommandOption("--no-raw-response")]
    [Description("Omit raw LLM responses from output")]
    public bool NoRawResponse { get; set; }

    // ─── Run control ──────────────────────────────────────────────────────────

    [CommandOption("--settings")]
    [Description("Settings file (repeatable; later overrides earlier)")]
    public string[]? SettingsFiles { get; set; }

    [CommandOption("--yes|-y")]
    [Description("Skip confirmation prompts")]
    public bool Yes { get; set; }

    [CommandOption("--continue")]
    [Description("Continue a previous run from checkpoint (uses existing checkpoint database)")]
    public bool ContinueFromCheckpoint { get; set; }

    [CommandOption("--max-concurrent")]
    [Description("Override concurrency (default: total_slots from server)")]
    public int? MaxConcurrent { get; set; }

    [CommandOption("--continue-on-failure")]
    [Description("Continue if an item fails (default behavior)")]
    public bool ContinueOnFailure { get; set; }

    [CommandOption("--stop-on-failure")]
    [Description("Stop run on first item failure")]
    public bool StopOnFailure { get; set; }

    [CommandOption("--timeout-seconds")]
    [Description("Per-item timeout in seconds")]
    public double? TimeoutSeconds { get; set; }

    [CommandOption("--retry-count")]
    [Description("Number of retries on transient failure (default: 2)")]
    public int? RetryCount { get; set; }
}
