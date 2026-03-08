using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;

namespace Seevalocal.Core.Pipeline.Stages;

/// <summary>
/// Compares the LLM response to EvalItem.ExpectedOutput.
/// Emits a boolean metric "exactMatch" (stored as 0/1).
/// Thread-safe.
/// </summary>
public sealed class ExactMatchStage(ILogger<ExactMatchStage> logger, bool caseSensitive = false) : IEvalStage
{
    private readonly ILogger<ExactMatchStage> _logger = logger;

    public string StageName => "ExactMatchStage";

    public bool CaseSensitive { get; init; } = caseSensitive;
    public bool TrimWhitespace { get; init; } = true;

    /// <summary>
    /// Key in StageOutputs to read the model response from.
    /// Defaults to PromptStage output.
    /// </summary>
    public string InputStageOutputKey { get; init; } = "PromptStage.response";

    public Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        var item = context.Item;

        if (item.ExpectedOutput is null)
        {
            _logger.LogWarning("[ExactMatchStage] Item {EvalItemId} has no ExpectedOutput; skipping match", item.Id);
            return Task.FromResult(StageResult.Success(
                new Dictionary<string, object?> { ["ExactMatchStage.skipped"] = true },
                []));  // no exactMatch metric emitted when skipped
        }

        if (!context.StageOutputs.TryGetValue(InputStageOutputKey, out var rawActual)
            || rawActual is not string actualOutput)
        {
            _logger.LogWarning(
                "[ExactMatchStage] Stage output key '{Key}' not found or not a string for item {EvalItemId}",
                InputStageOutputKey, item.Id);
            return Task.FromResult(StageResult.Failure(
                $"[ExactMatchStage] Stage output '{InputStageOutputKey}' not available or not a string"));
        }

        var expected = item.ExpectedOutput;
        var actual = actualOutput;

        if (TrimWhitespace)
        {
            expected = expected.Trim();
            actual = actual.Trim();
        }

        var comparison = CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var isMatch = string.Equals(expected, actual, comparison);

        _logger.LogDebug("[ExactMatchStage] Item {EvalItemId}: exactMatch={IsMatch}", item.Id, isMatch);

        return Task.FromResult(StageResult.Success(
            new Dictionary<string, object?> { ["ExactMatchStage.isMatch"] = isMatch },
            [new MetricValue { Name = "exactMatch", Value = new MetricScalar.BoolMetric(isMatch) }]));
    }
}
