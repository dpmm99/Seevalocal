using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Seevalocal.Judge;

/// <summary>
/// Parses raw judge LLM JSON output into a <see cref="ParsedJudgeResponse"/>.
/// Field-agnostic: extracts all top-level JSON fields as metrics.
/// Thread-safe; share a single instance.
/// </summary>
public sealed partial class JudgeResponseParser(ILogger<JudgeResponseParser> logger)
{
    private readonly ILogger<JudgeResponseParser> _logger = logger;

    /// <summary>
    /// Parses <paramref name="rawText"/> as JSON and extracts all top-level fields as metrics.
    /// Never throws; returns a failed <see cref="ParsedJudgeResponse"/> on any error.
    /// </summary>
    public ParsedJudgeResponse Parse(string rawText, JudgeConfig config)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentNullException.ThrowIfNull(config);

        // Strip markdown JSON fences
        var json = StripMarkdownFences(rawText.Trim());

        JsonElement? root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                "[JudgeResponseParser] Could not parse JSON from judge response: {RawText} — {Error}",
                rawText, ex.Message);
            return Failure();
        }

        if (root is not { ValueKind: JsonValueKind.Object } element)
        {
            _logger.LogError(
                "[JudgeResponseParser] JSON response is not an object: {RawText}",
                rawText);
            return Failure();
        }

        var metrics = new Dictionary<string, object?>();
        string? rationale = null;

        // Extract all top-level fields as metrics
        // Look for "rationale" field specifically (case-insensitive)
        foreach (var prop in element.EnumerateObject())
        {
            var value = ExtractJsonValue(prop.Value);

            // Check for rationale field (case-insensitive)
            if (prop.Name.Equals("rationale", StringComparison.OrdinalIgnoreCase))
            {
                rationale = value as string;
            }
            else
            {
                metrics[prop.Name] = value;
            }
        }

        return new ParsedJudgeResponse
        {
            ParseSucceeded = true,
            RawResponse = json,
            Rationale = rationale,
            Metrics = metrics,
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static string StripMarkdownFences(string text)
    {
        var match = GenJsonFencePattern().Match(text);
        return match.Success ? match.Groups[2].Value.Trim() : text;
    }

    /// <summary>
    /// Extracts a .NET value from a JsonElement.
    /// Handles primitives, null, and leaves complex types as JSON strings.
    /// </summary>
    private static object? ExtractJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetDouble(out var d) ? d : element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array or JsonValueKind.Object => element.GetRawText(),
            _ => element.GetRawText(),
        };
    }

    private static ParsedJudgeResponse Failure() =>
        new() { ParseSucceeded = false };

    [GeneratedRegex("```(json)?(.*)```", RegexOptions.Singleline)]
    private static partial Regex GenJsonFencePattern();
}
