using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;

namespace Seevalocal.Judge;

/// <summary>
/// <para>
/// Pipeline stage that sends the item's prompt, expected output, and primary LLM response
/// to a judge LLM for scoring. Produces <c>judgeScore</c> (raw score from judge) and <c>judgePassedBool</c>
/// metrics.
/// </para>
/// <para>Thread-safe; a single instance is shared across all concurrently running eval items.</para>
/// </summary>
public sealed partial class JudgeStage(
    JudgeConfig config,
    JudgePromptRenderer renderer,
    JudgeResponseParser parser,
    ILogger<JudgeStage> logger) : IEvalStage
{
    public string StageName => "JudgeStage";

    private readonly JudgeConfig _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly JudgePromptRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly JudgeResponseParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    private readonly ILogger<JudgeStage> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Convenience constructor that creates internal collaborators from DI-injected loggers.
    /// Uses the provided JudgeConfig for all judge settings.
    /// </summary>
    public JudgeStage(
        JudgeConfig config,
        ILogger<JudgeStage> logger,
        ILogger<JudgePromptRenderer> rendererLogger,
        ILogger<JudgeResponseParser> parserLogger)
        : this(config,
               new JudgePromptRenderer(rendererLogger),
               new JudgeResponseParser(parserLogger),
               logger)
    { }

    /// <summary>
    /// Resolves a template name to its full content using reflection.
    /// If the input looks like a template name (short, no newlines), looks it up in DefaultTemplates.
    /// Otherwise returns the input as-is (assumed to be full template content).
    /// </summary>
    private static string ResolveTemplate(string templateNameOrContent)
    {
        if (string.IsNullOrEmpty(templateNameOrContent))
            return DefaultTemplates.Standard;

        // If it contains newlines or is long, assume it's already the full template
        if (templateNameOrContent.Contains('\n') || templateNameOrContent.Length > 100)
            return templateNameOrContent;

        // Use reflection to find the matching template in DefaultTemplates
        var templateType = typeof(DefaultTemplates);
        var constants = templateType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .ToList();

        // Try to find a matching constant by name (convert kebab-case to PascalCase)
        var pascalCaseName = string.Concat(templateNameOrContent.Split('-').Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1)));
        var match = constants.FirstOrDefault(f => f.Name.Equals(pascalCaseName, StringComparison.OrdinalIgnoreCase));

        if (match != null)
            return (string)match.GetValue(null)!;

        // Also try direct match for constants that already have the right casing
        match = constants.FirstOrDefault(f => f.Name.Equals(templateNameOrContent, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            return (string)match.GetValue(null)!;

        // Fallback to standard template
        return DefaultTemplates.Standard;
    }

    /// <inheritdoc />
    public async Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ── 1. Resolve the judge client ────────────────────────────────────
        var judgeClient = context.JudgeClient
            ?? throw new InvalidOperationException(
                $"[{StageName}] JudgeStage requires a judge client but EvalStageContext.JudgeClient is null. " +
                "Ensure the pipeline orchestrator initialises a judge endpoint.");

        // ── 2. Retrieve the primary LLM's actual output ────────────────────
        var actualOutput = context.StageOutputs.GetValueOrDefault("PromptStage.response") as string;

        if (string.IsNullOrEmpty(actualOutput))
        {
            _logger.LogWarning(
                "[{Stage}] EvalItem {ItemId}: 'PromptStage.response' is missing or empty. Skipping judge evaluation.",
                StageName, context.Item.Id);
            return StageResult.Failure($"[{StageName}] Skipping item - primary LLM output is missing or empty.");
        }

        // ── 3. Render judge prompt ─────────────────────────────────────────
        var templateContent = ResolveTemplate(_config.JudgePromptTemplate);
        var judgePrompt = _renderer.Render(
            templateContent,
            context.Item.UserPrompt,
            context.Item.ExpectedOutput ?? string.Empty,
            actualOutput,
            context.Item.Metadata);

        _logger.LogDebug(
            "[{Stage}] EvalItem {ItemId}: sending request to judge endpoint",
            StageName, context.Item.Id);

        // ── 4. Call the judge endpoint ─────────────────────────────────────
        var request = new ChatCompletionRequest
        {
            Model = string.Empty, // use whatever model the judge server has loaded
            Messages =
            [
                new ChatMessage { Role = "system", Content = _config.JudgeSystemPrompt ?? string.Empty },
                new ChatMessage { Role = "user",   Content = judgePrompt },
            ],
            MaxTokens = _config.JudgeMaxTokenCount,
            Temperature = _config.JudgeSamplingTemperature,
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Result<ChatCompletionResponse> response;
        try
        {
            response = await judgeClient.ChatCompletionAsync(request, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "[{Stage}] EvalItem {ItemId}: judge request cancelled",
                StageName, context.Item.Id);
            return StageResult.Failure($"[{StageName}] Request to judge endpoint was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{Stage}] EvalItem {ItemId}: unexpected exception calling judge endpoint",
                StageName, context.Item.Id);
            return StageResult.Failure($"[{StageName}] Unexpected error: {ex.Message}");
        }
        sw.Stop();

        if (response.IsFailed)
        {
            var firstError = response.Errors.FirstOrDefault()?.Message ?? "unknown error";
            _logger.LogError(
                "[{Stage}] EvalItem {ItemId}: judge endpoint returned failure: {Error}",
                StageName, context.Item.Id, firstError);
            return StageResult.Failure($"[{StageName}] Judge endpoint error: {firstError}");
        }

        // ── 5. Parse the judge's response ─────────────────────────────────
        var judgeText = response.Value.Choices.Count > 0
            ? response.Value.Choices[0].Message.Content
            : string.Empty;

        _logger.LogTrace(
            "[{Stage}] EvalItem {ItemId}: raw judge response: {JudgeText}",
            StageName, context.Item.Id, judgeText);

        var parsed = _parser.Parse(judgeText, _config);

        if (!parsed.ParseSucceeded)
        {
            _logger.LogWarning(
                "[{Stage}] EvalItem {ItemId}: failed to parse judge response: {JudgeText}",
                StageName, context.Item.Id, judgeText);
            return new StageResult
            {
                Succeeded = false,
                FailureReason = $"[{StageName}] Could not parse judge response: \"{judgeText}\"",
                Outputs = BuildOutputs(judgeText, parsed),
                Metrics = BuildMetrics(response.Value, sw.Elapsed.TotalSeconds),
            };
        }

        _logger.LogDebug(
            "[{Stage}] EvalItem {ItemId}: parsed {MetricCount} metrics from judge response",
            StageName, context.Item.Id, parsed.Metrics.Count);

        return new StageResult
        {
            Succeeded = true,
            Outputs = BuildOutputs(judgeText, parsed),
            Metrics = BuildMetrics(response.Value, sw.Elapsed.TotalSeconds, parsed),
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildOutputs(
        string judgeText,
        ParsedJudgeResponse parsed)
    {
        var outputs = new Dictionary<string, object?>
        {
            ["JudgeStage.rawResponse"] = judgeText,
        };
        
        if (!string.IsNullOrEmpty(parsed.Rationale))
        {
            outputs["JudgeStage.rationale"] = parsed.Rationale;
        }
        
        return outputs;
    }

    private static IReadOnlyList<MetricValue> BuildMetrics(ChatCompletionResponse response, double wallClockSeconds, ParsedJudgeResponse? parsed = null)
    {
        var metrics = new List<MetricValue>();

        // Add token count and speed metrics (same as PromptStage)
        var promptTokenCount = response.Usage?.PromptTokens ?? 0;
        var completionTokenCount = response.Usage?.CompletionTokens ?? 0;
        var totalTokenCount = response.Usage?.TotalTokens ?? (promptTokenCount + completionTokenCount);

        metrics.Add(new MetricValue { Name = "judge.promptTokenCount", Value = new MetricScalar.IntMetric(promptTokenCount), SourceStage = "JudgeStage" });
        metrics.Add(new MetricValue { Name = "judge.completionTokenCount", Value = new MetricScalar.IntMetric(completionTokenCount), SourceStage = "JudgeStage" });
        metrics.Add(new MetricValue { Name = "judge.totalTokenCount", Value = new MetricScalar.IntMetric(totalTokenCount), SourceStage = "JudgeStage" });

        // Prefer server-reported timing if available; fall back to wall-clock
        var latencySeconds = response.Timings is not null
            ? (response.Timings.PromptMs + response.Timings.PredictedMs) / 1000.0
            : wallClockSeconds;

        metrics.Add(new MetricValue { Name = "judge.llmLatencySeconds", Value = new MetricScalar.DoubleMetric(Math.Round(latencySeconds, 2)), SourceStage = "JudgeStage" });

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

            metrics.Add(new MetricValue { Name = "judge.promptTokensPerSecond", Value = new MetricScalar.DoubleMetric(Math.Round(promptTps, 2)), SourceStage = "JudgeStage" });
            metrics.Add(new MetricValue { Name = "judge.completionTokensPerSecond", Value = new MetricScalar.DoubleMetric(Math.Round(completionTps, 2)), SourceStage = "JudgeStage" });
        }

        // Add all parsed metrics with "judge." prefix
        if (parsed != null)
        {
            foreach (var kvp in parsed.Metrics)
            {
                MetricScalar metricValue = kvp.Value switch
                {
                    null => new MetricScalar.DoubleMetric(0),
                    bool b => new MetricScalar.BoolMetric(b),
                    double d => new MetricScalar.DoubleMetric(d),
                    int i => new MetricScalar.IntMetric(i),
                    long l => new MetricScalar.IntMetric((int)l),
                    float f => new MetricScalar.DoubleMetric(f),
                    decimal m => new MetricScalar.DoubleMetric((double)m),
                    string s => new MetricScalar.StringMetric(s),
                    _ => new MetricScalar.StringMetric(kvp.Value?.ToString() ?? ""),
                };

                metrics.Add(new MetricValue
                {
                    Name = $"judge.{kvp.Key}",
                    Value = metricValue,
                    SourceStage = "JudgeStage",
                });
            }
        }

        return metrics;
    }
}
