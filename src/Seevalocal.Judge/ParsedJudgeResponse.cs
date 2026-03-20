namespace Seevalocal.Judge;

/// <summary>
/// Result of parsing a judge LLM's JSON response.
/// Contains the raw response, optional rationale, and arbitrary metrics extracted from the JSON.
/// </summary>
public record ParsedJudgeResponse
{
    /// <summary>True if JSON was successfully parsed.</summary>
    public bool ParseSucceeded { get; init; }

    /// <summary>The raw JSON response text (with markdown fences stripped).</summary>
    public string? RawResponse { get; init; }

    /// <summary>Optional rationale/explanation field from the JSON response.</summary>
    public string? Rationale { get; init; }

    /// <summary>Arbitrary metrics extracted from the JSON (flat key-value pairs).</summary>
    public Dictionary<string, object?> Metrics { get; init; } = [];
}
