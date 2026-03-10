namespace Seevalocal.Judge;

public record ParsedJudgeResponse
{
    public bool ParseSucceeded { get; init; }

    /// <summary>Raw score from the judge (e.g., 0-10, 1-5, etc.). Null if not applicable or parsing failed.</summary>
    public double? RawScore { get; init; }

    /// <summary>Normalized score in [0, 1]. Null if not applicable or parsing failed.</summary>
    /// <remarks>Kept for backwards compatibility. Use RawScore for display purposes.</remarks>
    public double? NormalizedScore { get; init; }

    public bool? Passed { get; init; }
    public string? Rationale { get; init; }
}
