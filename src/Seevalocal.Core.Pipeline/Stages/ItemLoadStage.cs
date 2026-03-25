using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Seevalocal.Core.Pipeline.Stages;

/// <summary>
/// Saves the input item data (user prompt, system prompt, expected output) to StageOutputs.
/// This ensures the checkpoint database contains all inputs for complete join queries
/// showing input -> response -> judgment side by side.
/// Thread-safe: all mutable state is passed via EvalStageContext.
/// </summary>
public sealed class ItemLoadStage(ILogger<ItemLoadStage> logger) : IEvalStage
{
    private readonly ILogger<ItemLoadStage> _logger = logger;

    public string StageName => "ItemLoadStage";

    public Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        var item = context.Item;

        _logger.LogDebug("[ItemLoadStage] Loading item {EvalItemId}", item.Id);

        var outputs = new Dictionary<string, object?>
        {
            ["ItemLoadStage.userPrompt"] = item.UserPrompt,
            ["ItemLoadStage.systemPrompt"] = item.SystemPrompt,
            ["ItemLoadStage.expectedOutput"] = item.ExpectedOutput,
            ["ItemLoadStage.id"] = item.Id,
        };

        // Also save metadata if present (as JSON for queryability)
        if (item.Metadata is { Count: > 0 })
        {
            outputs["ItemLoadStage.metadata"] = JsonSerializer.Serialize(item.Metadata);
        }

        // Save artifact file path if present
        if (item.ArtifactFilePath is not null)
        {
            outputs["ItemLoadStage.artifactFilePath"] = item.ArtifactFilePath;
        }

        _logger.LogDebug(
            "[ItemLoadStage] Item {EvalItemId} loaded: userPrompt={PromptLength} chars, expectedOutput={HasExpectedOutput}",
            item.Id,
            item.UserPrompt?.Length ?? 0,
            item.ExpectedOutput != null ? "yes" : "no");

        return Task.FromResult(StageResult.Success(outputs, []));
    }
}
