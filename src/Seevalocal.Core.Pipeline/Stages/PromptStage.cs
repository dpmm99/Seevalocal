using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using System.Diagnostics;

namespace Seevalocal.Core.Pipeline.Stages;

/// <summary>
/// Sends the item's SystemPrompt + UserPrompt to the primary LLM endpoint
/// and captures the response and timing metrics.
/// Thread-safe: all mutable state is passed via EvalStageContext.
/// </summary>
public sealed class PromptStage(ILogger<PromptStage> logger) : IEvalStage
{
    private readonly ILogger<PromptStage> _logger = logger;

    public string StageName => "PromptStage";

    /// <summary>Maximum number of completion tokens to request. Null = server default.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Stop sequences to pass to the model.</summary>
    public IReadOnlyList<string> StopSequences { get; init; } = [];

    public async Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        var item = context.Item;
        var ct = context.CancellationToken;

        // Check if we already have a response from a previous run (checkpoint resumption)
        if (context.StageOutputs.TryGetValue("PromptStage.response", out var existingResponse) && existingResponse is string existingResponseText && !string.IsNullOrEmpty(existingResponseText))
        {
            _logger.LogDebug("[PromptStage] Using cached response for item {EvalItemId}", item.Id);

            // Return cached response without making API call
            var cachedOutputs = new Dictionary<string, object?>
            {
                ["PromptStage.response"] = existingResponseText,
                ["PromptStage.userPrompt"] = item.UserPrompt,
                ["PromptStage.systemPrompt"] = item.SystemPrompt
            };

            // Try to restore rawResponse if available
            if (context.StageOutputs.TryGetValue("PromptStage.rawResponse", out var existingRawResponse))
            {
                cachedOutputs["PromptStage.rawResponse"] = existingRawResponse;
            }

            return StageResult.Success(cachedOutputs, []);  // No new metrics on cache hit
        }

        var messages = BuildMessages(item);

        var request = new ChatCompletionRequest
        {
            Model = "",   // llama-server uses the loaded model; field may be empty
            Messages = messages,
            MaxTokens = MaxTokens,
            Stop = StopSequences.Count > 0 ? StopSequences : null
        };

        _logger.LogDebug("[PromptStage] Sending request for item {EvalItemId}", item.Id);

        var sw = Stopwatch.StartNew();
        var result = await (context.PrimaryClient ?? context.JudgeClient)!.ChatCompletionAsync(request, ct);
        sw.Stop();

        if (result.IsFailed)
        {
            var errorMsg = string.Join("; ", result.Errors.Select(static e => e.Message));
            _logger.LogError("[PromptStage] Request failed for item {EvalItemId}: {Error}", item.Id, errorMsg);
            return StageResult.Failure($"[PromptStage] LLM request failed: {errorMsg}");
        }

        var response = result.Value;
        var responseText = response.Choices.FirstOrDefault()?.Message?.Content ?? "";

        var outputs = new Dictionary<string, object?>
        {
            ["PromptStage.response"] = responseText,
            ["PromptStage.rawResponse"] = response,
            ["PromptStage.userPrompt"] = item.UserPrompt,
            ["PromptStage.systemPrompt"] = item.SystemPrompt
        };

        var metrics = BuildMetrics(response, sw.Elapsed.TotalSeconds);

        _logger.LogDebug(
            "[PromptStage] Item {EvalItemId} completed: promptTokens={PromptTokenCount}, completionTokens={CompletionTokenCount}, latency={LatencySeconds:F3}s",
            item.Id,
            response.Usage?.PromptTokens ?? 0,
            response.Usage?.CompletionTokens ?? 0,
            sw.Elapsed.TotalSeconds);

        return StageResult.Success(outputs, metrics);
    }

    private static List<ChatMessage> BuildMessages(EvalItem item)
    {
        List<ChatMessage> messages = [];

        if (item.SystemPrompt is not null)
            messages.Add(new ChatMessage { Role = "system", Content = item.SystemPrompt });

        messages.Add(new ChatMessage { Role = "user", Content = item.UserPrompt });

        return messages;
    }

    private static List<MetricValue> BuildMetrics(ChatCompletionResponse response, double wallClockSeconds)
    {
        List<MetricValue> metrics = [];

        var promptTokenCount = response.Usage?.PromptTokens ?? 0;
        var completionTokenCount = response.Usage?.CompletionTokens ?? 0;
        var totalTokenCount = response.Usage?.TotalTokens ?? (promptTokenCount + completionTokenCount);

        metrics.Add(new MetricValue { Name = "promptTokenCount", Value = new MetricScalar.IntMetric(promptTokenCount) });
        metrics.Add(new MetricValue { Name = "completionTokenCount", Value = new MetricScalar.IntMetric(completionTokenCount) });
        metrics.Add(new MetricValue { Name = "totalTokenCount", Value = new MetricScalar.IntMetric(totalTokenCount) });

        // Prefer server-reported timing if available; fall back to wall-clock
        var latencySeconds = response.Timings is not null
            ? (response.Timings.PromptMs + response.Timings.PredictedMs) / 1000.0
            : wallClockSeconds;

        metrics.Add(new MetricValue { Name = "llmLatencySeconds", Value = new MetricScalar.DoubleMetric(latencySeconds) });

        if (latencySeconds > 0)
        {
            var promptTps = promptTokenCount > 0
                ? promptTokenCount / (response.Timings?.PromptMs / 1000.0 ?? latencySeconds)
                : 0.0;

            var completionTps = completionTokenCount > 0 && response.Timings is not null
                ? completionTokenCount / (response.Timings.PredictedMs / 1000.0)
                : completionTokenCount > 0
                    ? completionTokenCount / latencySeconds
                    : 0.0;

            metrics.Add(new MetricValue { Name = "promptTokensPerSecond", Value = new MetricScalar.DoubleMetric(promptTps) });
            metrics.Add(new MetricValue { Name = "completionTokensPerSecond", Value = new MetricScalar.DoubleMetric(completionTps) });
        }

        return metrics;
    }
}
