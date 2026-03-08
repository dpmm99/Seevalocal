namespace Seevalocal.Core.Models;

/// <summary>
/// A single unit of evaluation input.
/// </summary>
public record EvalItem
{
    /// <summary>
    /// Stable identifier, unique within a dataset.
    /// Auto-generated as "{sourceName}-{index:D6}" if not provided by the source.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Optional system prompt. Overrides dataset-level default.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The user-turn content sent to the model.</summary>
    public required string UserPrompt { get; init; }

    /// <summary>Reference output for scoring. Null if no expected output is available.</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// Arbitrary string key-value metadata.
    /// Examples: { "category": "greetings", "difficulty": "easy", "sourceLang": "en" }
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Optional path to an associated file artifact.
    /// E.g., the .cs file to complete, the source document for translation.
    /// </summary>
    public string? ArtifactFilePath { get; init; }
}
