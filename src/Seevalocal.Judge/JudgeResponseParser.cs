using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Seevalocal.Judge;

/// <summary>
/// Parses raw judge LLM output into a <see cref="ParsedJudgeResponse"/>.
/// Thread-safe; share a single instance.
/// </summary>
public sealed class JudgeResponseParser(ILogger<JudgeResponseParser> logger)
{
    // Matches the first floating-point or integer number in a string.
    private static readonly Regex FirstNumberPattern =
        new(@"(?<![.\d])-?\d+(?:\.\d+)?(?![.\d])", RegexOptions.Compiled);

    // Matches PASS or FAIL as whole words (case-insensitive).
    private static readonly Regex PassFailPattern =
        new(@"\b(?<verdict>PASS|FAIL)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Strips markdown JSON fences: ```json ... ``` or ``` ... ```
    private static readonly Regex JsonFencePattern =
        new(@"^```(?:json)?\s*\n?(.*?)\n?```\s*$", RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly ILogger<JudgeResponseParser> _logger = logger;

    /// <summary>
    /// Parses <paramref name="rawText"/> according to the format declared in <paramref name="config"/>.
    /// Never throws; returns a failed <see cref="ParsedJudgeResponse"/> on any error.
    /// </summary>
    public ParsedJudgeResponse Parse(string rawText, JudgeConfig config)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentNullException.ThrowIfNull(config);

        return config.ResponseFormat switch
        {
            JudgeResponseFormat.NumericScore => ParseNumericScore(rawText, config),
            JudgeResponseFormat.PassFail => ParsePassFail(rawText),
            JudgeResponseFormat.StructuredJson => ParseStructuredJson(rawText, config),
            _ => Failure($"Unknown JudgeResponseFormat: {config.ResponseFormat}")
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // NumericScore
    // ──────────────────────────────────────────────────────────────────────

    private ParsedJudgeResponse ParseNumericScore(string rawText, JudgeConfig config)
    {
        var match = FirstNumberPattern.Match(rawText);
        if (!match.Success)
        {
            _logger.LogError(
                "[JudgeResponseParser] Could not extract numeric score from judge response: {RawText}",
                rawText);
            return Failure($"Could not extract numeric score from judge response: \"{rawText}\"");
        }

        if (!double.TryParse(match.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var raw))
        {
            _logger.LogError(
                "[JudgeResponseParser] Extracted token '{Token}' is not a valid double from response: {RawText}",
                match.Value, rawText);
            return Failure($"Extracted token '{match.Value}' is not a valid double");
        }

        var normalized = Normalize(raw, config.ScoreMinValue, config.ScoreMaxValue);
        return new ParsedJudgeResponse
        {
            ParseSucceeded = true,
            NormalizedScore = normalized,
            Passed = normalized >= 0.5,
            Rationale = null,
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // PassFail
    // ──────────────────────────────────────────────────────────────────────

    private ParsedJudgeResponse ParsePassFail(string rawText)
    {
        var match = PassFailPattern.Match(rawText);
        if (!match.Success)
        {
            _logger.LogError(
                "[JudgeResponseParser] Could not find PASS/FAIL in judge response: {RawText}",
                rawText);
            return Failure($"Could not find PASS or FAIL in judge response: \"{rawText}\"");
        }

        var passed = match.Groups["verdict"].Value.Equals("PASS", StringComparison.OrdinalIgnoreCase);
        return new ParsedJudgeResponse
        {
            ParseSucceeded = true,
            NormalizedScore = passed ? 1.0 : 0.0,
            Passed = passed,
            Rationale = null,
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // StructuredJson
    // ──────────────────────────────────────────────────────────────────────

    private ParsedJudgeResponse ParseStructuredJson(string rawText, JudgeConfig config)
    {
        var json = StripMarkdownFences(rawText.Trim());

        JudgeJsonPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<JudgeJsonPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                "[JudgeResponseParser] Could not parse JSON from judge response: {RawText} — {Error}",
                rawText, ex.Message);
            return Failure($"Could not parse JSON from judge response: \"{rawText}\" — {ex.Message}");
        }

        if (payload is null)
        {
            _logger.LogError(
                "[JudgeResponseParser] JSON deserialization returned null for response: {RawText}",
                rawText);
            return Failure($"JSON deserialization returned null for response: \"{rawText}\"");
        }

        if (!payload.Score.HasValue)
        {
            _logger.LogError(
                "[JudgeResponseParser] JSON response missing 'score' field: {RawText}",
                rawText);
            return Failure($"JSON response missing 'score' field: \"{rawText}\"");
        }

        var normalized = Normalize(payload.Score.Value, config.ScoreMinValue, config.ScoreMaxValue);
        var passed = payload.Passed ?? (normalized >= 0.5);

        return new ParsedJudgeResponse
        {
            ParseSucceeded = true,
            NormalizedScore = normalized,
            Passed = passed,
            Rationale = payload.Rationale,
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static string StripMarkdownFences(string text)
    {
        var match = JsonFencePattern.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : text;
    }

    /// <summary>
    /// Normalizes <paramref name="raw"/> from [min, max] to [0, 1] and clamps.
    /// If min == max the result is 0.0 to avoid division by zero.
    /// </summary>
    private static double Normalize(double raw, double min, double max)
    {
        if (Math.Abs(max - min) < double.Epsilon)
            return 0.0;

        var normalized = (raw - min) / (max - min);
        return Math.Clamp(normalized, 0.0, 1.0);
    }

    private static ParsedJudgeResponse Failure(string reason) =>
        new() { ParseSucceeded = false };

    // ──────────────────────────────────────────────────────────────────────
    // Private DTO for JSON deserialization
    // ──────────────────────────────────────────────────────────────────────

    private sealed class JudgeJsonPayload
    {
        public string? Rationale { get; set; }
        public double? Score { get; set; }
        public bool? Passed { get; set; }
    }
}
