# Seevalocal — Part 3: Data Source Abstraction

> **Read `00-conventions.md` before this file.**
> Interfaces referenced here are defined in `10-interfaces.md`.
> This part is implemented in project `Seevalocal.DataSources`.

---

## 1. Responsibilities

- Define the canonical `EvalItem` record that flows through the pipeline.
- Load `EvalItem` streams from any supported source format.
- Support template injection (system prompts, prompt wrappers).
- Handle all supported file formats: directory trees, JSON, YAML, CSV, Parquet, one-item-per-line.
- Support split sources (prompts in one location, expected outputs in another).
- Deduplicate and validate items.

---

## 2. The EvalItem Record

```csharp
/// <summary>
/// A single unit of evaluation input.
/// All fields except Id and UserPrompt are optional.
/// </summary>
public record EvalItem
{
    /// <summary>Stable identifier unique within a dataset.</summary>
    public string Id { get; init; } = "";

    /// <summary>Optional system prompt. Overrides any dataset-level default.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The user-turn prompt sent to the model.</summary>
    public string UserPrompt { get; init; } = "";

    /// <summary>Expected/reference output used by scoring stages.</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>Arbitrary key-value metadata (e.g., category, difficulty, language pair).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Optional path to an external artifact associated with this item
    /// (e.g., a .cs file the model should complete, a source document for translation).
    /// </summary>
    public string? ArtifactFilePath { get; init; }
}
```

---

## 3. Data Source Interface

```csharp
/// <summary>
/// Streams EvalItems from a configured source.
/// Implementations must be stateless after construction.
/// </summary>
public interface IDataSource
{
    /// <summary>Human-readable name for logging.</summary>
    string Name { get; }

    /// <summary>
    /// Returns an async stream of items.
    /// Implementations should yield items one at a time without buffering the entire set.
    /// </summary>
    IAsyncEnumerable<EvalItem> GetItemsAsync(CancellationToken ct);

    /// <summary>
    /// Optional: total count if known without full enumeration (for progress reporting).
    /// Returns null if unknown.
    /// </summary>
    Task<int?> GetCountAsync(CancellationToken ct);
}
```

---

## 4. DataSourceConfig

```csharp
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

    // --- Template injection ---
    public string? PromptTemplate { get; init; }   // use {prompt} as placeholder
    public string? SystemPromptTemplate { get; init; }

    // --- Shared ---
    public string? DefaultSystemPrompt { get; init; }
    public int? MaxItemCount { get; init; }         // null = all items
    public int? ShuffleRandomSeed { get; init; }    // null = no shuffle
}

public enum DataSourceKind
{
    Directory,          // prompts in files, one per file
    SplitDirectories,   // prompts in one dir, expected outputs in another
    JsonFile,           // JSON array of objects
    JsonlFile,          // one JSON object per line
    YamlFile,           // YAML sequence
    CsvFile,            // CSV with header row
    ParquetFile,        // Parquet columnar file
    InlineList,         // items defined directly in the settings file
}

public record FieldMapping
{
    public string IdField { get; init; } = "id";
    public string UserPromptField { get; init; } = "prompt";
    public string? ExpectedOutputField { get; init; } = "expected";
    public string? SystemPromptField { get; init; }
    public IReadOnlyList<string> MetadataFields { get; init; } = [];
}
```

---

## 5. Supported Formats in Detail

### 5.1 Directory Source

```
prompts/
  001.txt      → UserPrompt = file contents, Id = "001"
  002.txt
  ...
expected/
  001.txt      → ExpectedOutput for "001"
  002.txt
system-prompt.txt  → applied to all items
```

Matching between prompt and expected files: by **filename without extension**. Missing expected files are allowed (ExpectedOutput = null).

### 5.2 Unified File Formats

#### JSON Array
```json
[
  { "id": "001", "prompt": "Translate to French: hello", "expected": "bonjour" },
  { "id": "002", "prompt": "Translate to French: goodbye", "expected": "au revoir" }
]
```

#### JSONL (one JSON object per line)
```
{"id":"001","prompt":"...","expected":"..."}
{"id":"002","prompt":"...","expected":"..."}
```

#### YAML Sequence
```yaml
- id: "001"
  prompt: "Translate to French: hello"
  expected: "bonjour"
```

#### CSV
```
id,prompt,expected,category
001,"Translate: hello","bonjour",greetings
```

#### Parquet
Same columns as CSV, read via `Parquet.Net`. All columns are read as strings unless otherwise mapped.

#### Inline (in settings file)
```yaml
dataSource:
  kind: inlineList
  items:
    - id: q1
      userPrompt: "What is 2+2?"
      expectedOutput: "4"
```

### 5.3 Auto-ID Generation

If the source does not provide an `id` field, one is generated as:
```
{DataSourceName}-{zeroBasedIndex:D6}
```

Duplicate IDs within a dataset cause a validation error (not silent deduplication).

---

## 6. Template Injection

Templates allow wrapping prompts without modifying the source data.

```csharp
public sealed class PromptTemplateEngine
{
    // Applies templates to an EvalItem, returning a new item with substituted fields.
    // Available variables: {prompt}, {expected}, {id}, plus any metadata key as {meta.key}.
    public EvalItem Apply(EvalItem item, DataSourceConfig config);
}
```

Example template:
```
You are a professional French translator. Translate the following English text to French.\n\nText: {prompt}\n\nTranslation:
```

Result: `UserPrompt` becomes the expanded string; original `UserPrompt` is saved to `Metadata["originalPrompt"]`.

---

## 7. Data Source Factory

```csharp
public sealed class DataSourceFactory
{
    public IDataSource Create(DataSourceConfig config);
}
```

Dispatches to the correct `IDataSource` implementation by `Kind`. Each implementation is a separate internal class.

---

## 8. Validation

The data source layer validates:
- File/directory paths exist and are readable.
- `FieldMapping` refers to columns that exist in the file.
- No duplicate IDs.
- `PromptTemplate` contains `{prompt}` placeholder.

---

## 9. Unit Tests (Seevalocal.DataSources.Tests)

| Test class | Coverage |
|---|---|
| `DirectoryDataSourceTests` | Single dir; split dirs; missing expected file; file filter |
| `JsonDataSourceTests` | Array; JSONL; missing id field → auto-generated |
| `CsvDataSourceTests` | Header mapping; custom FieldMapping; unicode content |
| `ParquetDataSourceTests` | Column types coerced to string; large file streaming |
| `PromptTemplateEngineTests` | Placeholder substitution; metadata variables; missing placeholder error |
| `DataSourceFactoryTests` | Correct implementation returned per Kind |
| `DuplicateIdTests` | Duplicate triggers validation error |
