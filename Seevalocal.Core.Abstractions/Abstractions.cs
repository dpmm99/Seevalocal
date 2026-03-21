using FluentResults;
using Seevalocal.Core.Models;

namespace Seevalocal.Core;

// ── IEvalStage ────────────────────────────────────────────────────────────────

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

// ── EvalStageContext ──────────────────────────────────────────────────────────

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

    /// <summary>
    /// The name of the last completed stage for this item (for checkpoint resumption).
    /// If set, stages up to and including this stage will be skipped.
    /// </summary>
    public string? LastCompletedStage { get; init; }

    /// <summary>The fully resolved run configuration.</summary>
    public required ResolvedConfig Config { get; init; }

    /// <summary>HTTP client for the primary LLM endpoint. Might be null during judge phase in two-phase configuration.</summary>
    public required ILlamaServerClient? PrimaryClient { get; init; }

    /// <summary>HTTP client for the judge LLM endpoint. Null if no judge is configured.</summary>
    public ILlamaServerClient? JudgeClient { get; init; }

    /// <summary>Cancellation token. All async calls must respect this.</summary>
    public CancellationToken CancellationToken { get; init; }
}

// ── StageResult ───────────────────────────────────────────────────────────────

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

// ── EvalResult ────────────────────────────────────────────────────────────────

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

// ── EvalProgress ──────────────────────────────────────────────────────────────

public record EvalProgress
{
    public required string EvalItemId { get; init; }
    public bool Succeeded { get; init; }
    public int CompletedCount { get; init; }
    public int TotalCount { get; init; }   // -1 if unknown
    public double ElapsedSeconds { get; init; }
    public double? EstimatedRemainingSeconds { get; init; }
    public double? AverageCompletionTokensPerSecond { get; init; }
}

// ── IResultCollector ──────────────────────────────────────────────────────────

public interface IResultCollector
{
    /// <summary>Called for each completed EvalResult (may be called concurrently).</summary>
    Task CollectAsync(EvalResult result, CancellationToken ct);

    /// <summary>Called once after all items are processed. Flush any pending state.</summary>
    Task FinalizeAsync(CancellationToken ct);

    /// <summary>Returns all collected results. Safe to call during or after the run.</summary>
    IReadOnlyList<EvalResult> GetResults();
}

public interface ILlamaServerClient
{
    Task<Result<AnthropicMessageResponse>> AnthropicMessageAsync(AnthropicMessageRequest request, CancellationToken ct);
    Task<Result<ChatCompletionResponse>> ChatCompletionAsync(ChatCompletionRequest request, CancellationToken ct);
    Task<Result<EmbeddingsResponse>> GetEmbeddingsAsync(EmbeddingsRequest request, CancellationToken ct);
    Task<Result<HealthStatus>> GetHealthAsync(CancellationToken ct);
    Task<Result<ServerProps>> GetPropsAsync(CancellationToken ct);
    Task<Result<TokenizeResponse>> TokenizeAsync(string content, bool addSpecial, CancellationToken ct);
}