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
            // Check if this stage has already been completed (checkpoint resumption)
            // Skip all stages up to and including the last completed one
            if (!string.IsNullOrEmpty(context.LastCompletedStage))
            {
                // Find the index of the last completed stage in THIS pipeline
                var lastCompletedIndex = -1;
                for (int i = 0; i < Stages.Count; i++)
                {
                    if (Stages[i].StageName == context.LastCompletedStage)
                    {
                        lastCompletedIndex = i;
                        break;
                    }
                }

                // If LastCompletedStage is not in this pipeline, check if it's a "later" stage
                // (e.g., JudgeStage when running primary pipeline) - if so, skip all stages
                if (lastCompletedIndex < 0)
                {
                    // Check if LastCompletedStage is from a later phase/pipeline
                    // For now, if it contains "Judge", assume it's later than any non-judge stage
                    if (context.LastCompletedStage.Contains("Judge", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Skipping all stages for item {EvalItemId} - completed in later phase (last={LastCompletedStage})",
                            context.Item.Id, context.LastCompletedStage);

                        // Load all existing outputs
                        foreach (var kvp in context.StageOutputs)
                        {
                            allOutputs[kvp.Key] = kvp.Value;
                        }
                        break;  // Skip all remaining stages
                    }
                }

                // Find the index of the current stage
                var currentIndex = -1;
                for (int i = 0; i < Stages.Count; i++)
                {
                    if (Stages[i].StageName == stage.StageName)
                    {
                        currentIndex = i;
                        break;
                    }
                }

                // Skip if this stage was already completed (current index <= last completed index)
                if (lastCompletedIndex >= 0 && currentIndex >= 0 && currentIndex <= lastCompletedIndex)
                {
                    _logger.LogDebug("Skipping stage {StageName} for item {EvalItemId} - already completed (last={LastCompletedStage})",
                        stage.StageName, context.Item.Id, context.LastCompletedStage);

                    // Load existing outputs for this stage from context if available
                    foreach (var kvp in context.StageOutputs.Where(kvp => kvp.Key.StartsWith(stage.StageName + ".", StringComparison.Ordinal)))
                    {
                        allOutputs[kvp.Key] = kvp.Value;
                    }
                    continue;
                }
            }

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
                await ResultCollector.SavePartialProgressAsync(context.Item.Id, stage.StageName, ct);

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
            Succeeded = overallSucceeded,
            FailureReason = firstFailureReason,
            Metrics = allMetrics,
            AllStageOutputs = allOutputs,
            StartedAt = startedAt,
            DurationSeconds = sw.Elapsed.TotalSeconds,
            RawLlmResponse = rawResponse as string,
            FirstShown = DateTimeOffset.Now
        };

        _logger.LogDebug("Pipeline {PipelineName} completed for item {EvalItemId} in {DurationSeconds:F2}s — Succeeded={Succeeded}",
            PipelineName, context.Item.Id, result.DurationSeconds, result.Succeeded);

        return result;
    }
}
