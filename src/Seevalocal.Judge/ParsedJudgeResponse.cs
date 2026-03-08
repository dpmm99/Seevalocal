namespace Seevalocal.Judge;

public record ParsedJudgeResponse
{
    public bool ParseSucceeded { get; init; }

    /// <summary>Normalized score in [0, 1]. Null if not applicable or parsing failed.</summary>
    public double? NormalizedScore { get; init; }

    public bool? Passed { get; init; }
    public string? Rationale { get; init; }
}
