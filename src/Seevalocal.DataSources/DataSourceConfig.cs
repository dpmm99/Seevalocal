namespace Seevalocal.DataSources;

public record DataSourceConfig
{
    public DataSourceKind Kind { get; init; }

    // --- Directory-based ---
    public string? PromptDirectoryPath { get; init; }
    public string? ExpectedOutputDirectoryPath { get; init; }   // null = no expected output
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

public enum DataSourceKind
{
    Directory,
    SplitDirectories,
    JsonFile,
    JsonlFile,
    YamlFile,
    CsvFile,
    ParquetFile,
    InlineList,
}

public record FieldMapping
{
    public string IdField { get; init; } = "id";
    public string UserPromptField { get; init; } = "prompt";
    public string? ExpectedOutputField { get; init; } = "expected";
    public string? SystemPromptField { get; init; }
    public IReadOnlyList<string> MetadataFields { get; init; } = [];
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
