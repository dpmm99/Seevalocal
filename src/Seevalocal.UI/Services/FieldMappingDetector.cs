using Seevalocal.Core.Models;
using System.Text.Json;

namespace Seevalocal.UI.Services;

/// <summary>
/// Analyzes input data files to detect available fields and suggest field mappings.
/// </summary>
public static class FieldMappingDetector
{
    // Common field name patterns for user prompts
    private static readonly string[] UserPromptPatterns =
    [
        "prompt", "input", "user", "request", "question", "query", "text", "content", "instruction"
    ];

    // Common field name patterns for expected outputs
    private static readonly string[] ExpectedOutputPatterns =
    [
        "answer", "response", "result", "output", "expected", "target", "completion", "reply"
    ];

    // Common field name patterns for IDs
    private static readonly string[] IdPatterns =
    [
        "id", "identifier", "key", "index", "idx", "item_id", "itemid"
    ];

    // Common field name patterns for system prompts
    private static readonly string[] SystemPromptPatterns =
    [
        "system", "system_prompt", "systemprompt", "instruction", "context", "persona"
    ];

    /// <summary>
    /// Analyzes a JSON/JSONL file and returns detected fields with suggested mappings.
    /// </summary>
    public static async Task<FieldMappingAnalysis> AnalyzeFileAsync(string filePath, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".json" => await AnalyzeJsonFileAsync(filePath, ct),
            ".jsonl" => await AnalyzeJsonlFileAsync(filePath, ct),
            ".csv" => await AnalyzeCsvFileAsync(filePath, ct),
            ".yaml" or ".yml" => await AnalyzeYamlFileAsync(filePath, ct),
            _ => new FieldMappingAnalysis { Error = $"Unsupported file type: {extension}" }
        };
    }

    private static async Task<FieldMappingAnalysis> AnalyzeJsonFileAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            using var doc = JsonDocument.Parse(json);

            // Handle array of objects
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var firstElement = doc.RootElement[0];
                return AnalyzeObject(firstElement);
            }
            // Handle single object
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return AnalyzeObject(doc.RootElement);
            }

            return new FieldMappingAnalysis { Error = "JSON must be an array of objects or a single object" };
        }
        catch (Exception ex)
        {
            return new FieldMappingAnalysis { Error = $"Failed to parse JSON: {ex.Message}" };
        }
    }

    private static async Task<FieldMappingAnalysis> AnalyzeJsonlFileAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var fields = new HashSet<string>();
            var linesRead = 0;

            await foreach (var line in File.ReadLinesAsync(filePath, ct))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        fields.Add(prop.Name);
                    }
                }

                linesRead++;
                if (linesRead >= 10) break; // Sample first 10 lines
            }

            return CreateAnalysis(fields.ToList());
        }
        catch (Exception ex)
        {
            return new FieldMappingAnalysis { Error = $"Failed to parse JSONL: {ex.Message}" };
        }
    }

    private static async Task<FieldMappingAnalysis> AnalyzeCsvFileAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var fields = new List<string>();
            var firstLine = true;

            await foreach (var line in File.ReadLinesAsync(filePath, ct))
            {
                if (firstLine)
                {
                    // First line is headers
                    var headers = line.Split(',');
                    fields.AddRange(headers.Select(h => h.Trim().Replace("\"", "")));
                    firstLine = false;
                }
                else
                {
                    break; // Only need headers
                }
            }

            return CreateAnalysis(fields);
        }
        catch (Exception ex)
        {
            return new FieldMappingAnalysis { Error = $"Failed to parse CSV: {ex.Message}" };
        }
    }

    private static async Task<FieldMappingAnalysis> AnalyzeYamlFileAsync(string filePath, CancellationToken ct)
    {
        try
        {
            // Simple YAML parsing - just extract top-level keys
            var fields = new HashSet<string>();

            await foreach (var line in File.ReadLinesAsync(filePath, ct))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;

                var trimmed = line.TrimStart();
                if (!trimmed.Contains(':')) continue;

                var key = trimmed.Split(':')[0].Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    fields.Add(key);
                }
            }

            return CreateAnalysis(fields.ToList());
        }
        catch (Exception ex)
        {
            return new FieldMappingAnalysis { Error = $"Failed to parse YAML: {ex.Message}" };
        }
    }

    private static FieldMappingAnalysis AnalyzeObject(JsonElement obj)
    {
        var fields = new List<string>();

        foreach (var prop in obj.EnumerateObject())
        {
            fields.Add(prop.Name);
        }

        return CreateAnalysis(fields);
    }

    private static FieldMappingAnalysis CreateAnalysis(List<string> fields)
    {
        return new FieldMappingAnalysis
        {
            AvailableFields = fields,
            SuggestedMapping = new FieldMapping
            {
                IdField = FindBestMatch(fields, IdPatterns),
                UserPromptField = FindBestMatch(fields, UserPromptPatterns),
                ExpectedOutputField = FindBestMatch(fields, ExpectedOutputPatterns),
                SystemPromptField = FindBestMatch(fields, SystemPromptPatterns),
            }
        };
    }

    private static string? FindBestMatch(List<string> fields, string[] patterns)
    {
        // Case-insensitive exact match
        foreach (var pattern in patterns)
        {
            var match = fields.FirstOrDefault(f => f.Equals(pattern, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        // Contains match
        foreach (var pattern in patterns)
        {
            var match = fields.FirstOrDefault(f => f.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return null;
    }
}

/// <summary>
/// Result of field mapping analysis.
/// </summary>
public record FieldMappingAnalysis
{
    public List<string> AvailableFields { get; init; } = [];
    public FieldMapping SuggestedMapping { get; init; } = new();
    public string? Error { get; init; }
    public bool HasError => !string.IsNullOrEmpty(Error);
}
