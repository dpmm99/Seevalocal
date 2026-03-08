using Seevalocal.Core.Models;

namespace Seevalocal.Core.Pipeline.Tests;

/// <summary>
/// A stage that always succeeds and emits the specified outputs and metrics.
/// </summary>
public sealed class SucceedingStage(
    string name = "SucceedingStage",
    Dictionary<string, object?>? outputs = null,
    List<MetricValue>? metrics = null) : IEvalStage
{
    private readonly Dictionary<string, object?> _outputs = outputs ?? [];
    private readonly List<MetricValue> _metrics = metrics ?? [];

    public string StageName { get; } = name;
    public int CallCount { get; private set; }

    public Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        CallCount++;
        return Task.FromResult(StageResult.Success(_outputs, _metrics));
    }
}

/// <summary>
/// A stage that always fails with the specified reason.
/// </summary>
public sealed class FailingStage(string name = "FailingStage", string? failureReason = null) : IEvalStage
{
    public string StageName { get; } = name;
    public int CallCount { get; private set; }

    public string FailureReason { get; } = failureReason ?? $"{name} failed by design";

    public Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        CallCount++;
        return Task.FromResult(StageResult.Failure(FailureReason));
    }
}

/// <summary>
/// A stage that always throws an exception.
/// </summary>
public sealed class ThrowingStage(string name = "ThrowingStage", Exception? exception = null) : IEvalStage
{
    public string StageName { get; } = name;

    public Exception Exception { get; } = exception ?? new InvalidOperationException("Intentional exception");

    public Task<StageResult> ExecuteAsync(EvalStageContext context)
        => throw Exception;
}

/// <summary>
/// A stage that captures its context for later inspection.
/// </summary>
public sealed class CapturingStage(string name = "CapturingStage") : IEvalStage
{
    public string StageName { get; } = name;
    public EvalStageContext? CapturedContext { get; private set; }

    public Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        CapturedContext = context;
        return Task.FromResult(StageResult.Success(new Dictionary<string, object?>(), []));
    }
}

/// <summary>
/// A result collector that captures all results in memory.
/// </summary>
public sealed class CapturingResultCollector : IResultCollector
{
    private readonly List<EvalResult> _results = [];

    public Task CollectAsync(EvalResult result, CancellationToken ct)
    {
        _results.Add(result);
        return Task.CompletedTask;
    }

    public Task FinalizeAsync(CancellationToken ct) => Task.CompletedTask;

    public IReadOnlyList<EvalResult> GetResults() => _results.AsReadOnly();
}
