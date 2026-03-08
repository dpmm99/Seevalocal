using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using System.Diagnostics;

namespace Seevalocal.Core.Pipeline;

/// <summary>
/// An ordered list of stages that are executed sequentially for each EvalItem.
/// The pipeline itself is stateless and thread-safe; state is carried in EvalStageContext.
/// Supports checkpoint saving for crash recovery.
/// </summary>
public sealed class EvalPipeline(ILogger<EvalPipeline> logger)
{
    private readonly ILogger<EvalPipeline> _logger = logger;

    public string PipelineName { get; init; } = "";
    public IReadOnlyList<IEvalStage> Stages { get; init; } = [];
    public PersistentResultCollector? ResultCollector { get; set; }

    /// <summary>
    /// Run all stages for a single item. Stages execute sequentially.
    /// If a stage fails and continueOnStageFailure is false, remaining stages are skipped.
    /// Returns a complete EvalResult regardless of success/failure. Never throws.
    /// Saves progress after each stage for checkpoint/resume capability.
    /// </summary>
    public async Task<EvalResult> RunItemAsync(
        EvalStageContext context,
        bool continueOnStageFailure,
        string evalSetId,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        Dictionary<string, object?> allOutputs = [];
        List<MetricValue> allMetrics = [];
        var overallSucceeded = true;
        string? firstFailureReason = null;
        _logger.LogDebug("Pipeline {PipelineName} starting for item {EvalItemId}",
            PipelineName, context.Item.Id);

        foreach (var stage in Stages)
        {
            if (!overallSucceeded && !continueOnStageFailure)
            {
                _logger.LogDebug("Skipping stage {StageName} for item {EvalItemId} due to prior failure",
                    stage.StageName, context.Item.Id);
                continue;
            }

            // Build updated context with accumulated outputs
            var stageContext = context with { StageOutputs = allOutputs };

            StageResult stageResult;
            try
            {
                _logger.LogDebug("Executing stage {StageName} for item {EvalItemId}",
                    stage.StageName, context.Item.Id);

                stageResult = await stage.ExecuteAsync(stageContext);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Stage {StageName} cancelled for item {EvalItemId}",
                    stage.StageName, context.Item.Id);
                throw; // propagate cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{StageName}] Unhandled exception for item {EvalItemId}",
                    stage.StageName, context.Item.Id);
                stageResult = StageResult.Failure(
                    $"[{stage.StageName}] Unhandled exception: {ex.Message}");
            }

            // Merge outputs
            foreach ((var key, var value) in stageResult.Outputs)
                allOutputs[key] = value;

            allMetrics.AddRange(stageResult.Metrics);

            if (!stageResult.Succeeded)
            {
                overallSucceeded = false;
                firstFailureReason ??= stageResult.FailureReason;

                _logger.LogWarning("Stage {StageName} failed for item {EvalItemId}: {FailureReason}",
                    stage.StageName, context.Item.Id, stageResult.FailureReason);
            }

            // Save stage outputs to checkpoint database
            var lastCompletedStage = stage.StageName;
            if (ResultCollector != null)
            {
                // Save partial progress in case of crash
                await ResultCollector.SavePartialProgressAsync(context.Item.Id, evalSetId, stage.StageName, ct);

                // Save each stage output
                foreach ((var key, var value) in stageResult.Outputs)
                {
                    await ResultCollector.SaveStageOutputAsync(context.Item.Id, stage.StageName, key, value, ct);
                }
            }
        }

        sw.Stop();

        // Extract raw LLM response from PromptStage output if present
        _ = allOutputs.TryGetValue("PromptStage.response", out var rawResponse);

        var result = new EvalResult
        {
            EvalItemId = context.Item.Id,
            EvalSetId = evalSetId,
            Succeeded = overallSucceeded,
            FailureReason = firstFailureReason,
            Metrics = allMetrics,
            AllStageOutputs = allOutputs,
            StartedAt = startedAt,
            DurationSeconds = sw.Elapsed.TotalSeconds,
            RawLlmResponse = rawResponse as string
        };

        _logger.LogDebug("Pipeline {PipelineName} completed for item {EvalItemId} in {DurationSeconds:F2}s — Succeeded={Succeeded}",
            PipelineName, context.Item.Id, result.DurationSeconds, result.Succeeded);

        return result;
    }
}
