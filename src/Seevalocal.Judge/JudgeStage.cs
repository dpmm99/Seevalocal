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
        var actualOutput = context.StageOutputs.GetValueOrDefault("PromptStage.response") as string ?? string.Empty;

        if (string.IsNullOrEmpty(actualOutput))
        {
            _logger.LogWarning(
                "[{Stage}] EvalItem {ItemId}: 'PromptStage.response' is missing or empty. " +
                "The judge will evaluate an empty actual output.",
                StageName, context.Item.Id);
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
                Metrics = BuildMetrics(parsed),
            };
        }

        _logger.LogDebug(
            "[{Stage}] EvalItem {ItemId}: judgeScore={Score:F2} passed={Passed}",
            StageName, context.Item.Id, parsed.RawScore, parsed.Passed);

        return new StageResult
        {
            Succeeded = true,
            Outputs = BuildOutputs(judgeText, parsed),
            Metrics = BuildMetrics(parsed),
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildOutputs(
        string judgeText,
        ParsedJudgeResponse parsed) =>
        new()
        {
            ["JudgeStage.rawResponse"] = judgeText,
            ["JudgeStage.score"] = parsed.RawScore,
            ["JudgeStage.passed"] = parsed.Passed,
            ["JudgeStage.rationale"] = parsed.Rationale,
        };

    private static IReadOnlyList<MetricValue> BuildMetrics(ParsedJudgeResponse parsed) =>
    [
        new MetricValue
        {
            Name        = "judgeScore",
            Value       = new MetricScalar.DoubleMetric(parsed.RawScore ?? 0.0),
            SourceStage = "JudgeStage",
        },
        new MetricValue
        {
            Name        = "judgePassedBool",
            Value       = new MetricScalar.BoolMetric(parsed.Passed ?? false),
            SourceStage = "JudgeStage",
        },
    ];

    /// <summary>
    /// Represents the result of parsing a judge score.
    /// </summary>
    public sealed class ScoreResult
    {
        public double ScoreRatio { get; init; }
        public bool Passed { get; init; }
    }

    public static ScoreResult? ParseScore(string raw, int maxScore, double passThreshold)
    {
        double? numericScore = null;

        // Try to parse numeric score from the raw response
        if (double.TryParse(raw, out var directScore))
        {
            numericScore = directScore;
        }
        // Try to extract score from JSON
        else if (raw.Trim().StartsWith('{'))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("score", out var scoreProp) && scoreProp.TryGetDouble(out var s))
                    numericScore = s;
            }
            catch
            {
                // Ignore JSON parse errors, fall through to keyword detection
            }
        }

        // If we found a numeric score, return normalized ratio
        if (numericScore.HasValue)
        {
            var ratio = maxScore > 0 ? numericScore.Value / maxScore : 0;
            return new ScoreResult
            {
                ScoreRatio = ratio,
                Passed = ratio >= passThreshold
            };
        }

        // Try to extract first number from narrative response
        var match = NumericScorePattern().Match(raw);
        if (match.Success && double.TryParse(match.Value, out var extractedScore))
        {
            var ratio = maxScore > 0 ? extractedScore / maxScore : 0;
            return new ScoreResult
            {
                ScoreRatio = ratio,
                Passed = ratio >= passThreshold
            };
        }

        // Keyword-based pass/fail detection
        if (raw.Contains("pass", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("correct", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("true", StringComparison.OrdinalIgnoreCase))
        {
            return new ScoreResult
            {
                ScoreRatio = 1.0,
                Passed = true
            };
        }

        if (raw.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("incorrect", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("false", StringComparison.OrdinalIgnoreCase))
        {
            return new ScoreResult
            {
                ScoreRatio = 0.0,
                Passed = false
            };
        }

        // Default: return null if no score could be extracted
        return null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial System.Text.RegularExpressions.Regex NumericScorePattern();
}
