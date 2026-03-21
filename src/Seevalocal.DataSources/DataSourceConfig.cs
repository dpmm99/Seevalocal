using Seevalocal.Core.Models;

namespace Seevalocal.DataSources;

public record DataSourceConfig
{
    public DataSourceKind Kind { get; init; }

    // --- Directory-based ---
    public string? PromptDirectory { get; init; }
    public string? ExpectedDirectory { get; init; }   // null = no expected output
    public string? SystemPromptFilePath { get; init; }          // single file applied to all
    public string FileExtensionFilter { get; init; } = "*.txt"; // glob

    // --- File-based (JSON/YAML/CSV/Parquet/JSONL) ---
    public string? DataFilePath { get; init; }
    public FieldMapping? FieldMapping { get; init; }

    // --- Inline ---
    public IReadOnlyList<EvalItemDto>? InlineItems { get; init; }

    // --- Template injection ---
    public string? PromptTemplate { get; init; }
    public string? SystemPromptTemplate { get; init; }

    // --- Shared ---
    public string? DefaultSystemPrompt { get; init; }
    public int? MaxItemCount { get; init; }         // null = all items
    public int? ShuffleRandomSeed { get; init; }    // null = no shuffle
}

public record FieldMapping
{
    public string? IdField { get; init; }
    public string? UserPromptField { get; init; }
    public string? ExpectedOutputField { get; init; }
    public string? SystemPromptField { get; init; }
    public IReadOnlyList<string> MetadataFields { get; init; } = [];

    /// <summary>
    /// Creates a FieldMapping with default values for JSONL files.
    /// Uses 'question' for user prompt and 'answer' for expected output.
    /// </summary>
    public static FieldMapping ForJsonl() => new()
    {
        IdField = "id",
        UserPromptField = "question",
        ExpectedOutputField = "answer",
    };
}

/// <summary>DTO for inline items defined directly in settings.</summary>
public record EvalItemDto
{
    public string? Id { get; init; }
    public string? SystemPrompt { get; init; }
    public string UserPrompt { get; init; } = "";
    public string? ExpectedOutput { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public string? ArtifactFilePath { get; init; }
}
