using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Schema;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.DataSources.Internal;

namespace Seevalocal.DataSources.Sources;

internal sealed class ParquetDataSource(string name, DataSourceConfig config, ILogger logger) : IDataSource
{
    private readonly DataSourceConfig _config = config;
    private readonly ILogger _logger = logger;

    public string Name { get; } = name;

    public async IAsyncEnumerable<EvalItem> GetItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var mapping = _config.FieldMapping ?? new FieldMapping();
        var filePath = Path.GetFullPath(_config.DataFilePath!);
        _logger.LogDebug("[{Name}] Loading Parquet from {Path}", Name, filePath);

        await using var fileStream = File.OpenRead(filePath);
        using var reader = await ParquetReader.CreateAsync(fileStream, cancellationToken: ct);

        var globalIndex = 0;
        for (var rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(rg);
            var fields = reader.Schema.Fields;

            // Read all columns as string dictionaries
            Dictionary<string, string?[]> columns = [];
            var rowCount = 0;

            foreach (var field in fields)
            {
                if (field is not DataField dataField) continue;

                var column = await rowGroupReader.ReadColumnAsync(dataField, ct);
                rowCount = column.Data.Length;
                columns[field.Name] = column.Data.Cast<object?>()
                    .Select(static v => v?.ToString())
                    .ToArray();
            }

            for (var row = 0; row < rowCount; row++)
            {
                ct.ThrowIfCancellationRequested();

                var idField = mapping.IdField ?? "id";
                var userPromptField = mapping.UserPromptField ?? "prompt";
                var expectedOutputField = mapping.ExpectedOutputField ?? "expected";
                var systemPromptField = mapping.SystemPromptField;

                var id = GetValue(columns, idField, row)
                         ?? IdGenerator.Generate(Name, globalIndex);
                var userPrompt = GetValue(columns, userPromptField, row) ?? "";
                var expectedOutput = expectedOutputField is not null
                    ? GetValue(columns, expectedOutputField, row)
                    : null;
                var systemPrompt = systemPromptField is not null
                    ? GetValue(columns, systemPromptField, row)
                    : _config.DefaultSystemPrompt;

                Dictionary<string, string> metadata = [];
                foreach (var field in mapping.MetadataFields)
                {
                    var val = GetValue(columns, field, row);
                    if (val is not null)
                        metadata[field] = val;
                }

                yield return new EvalItem
                {
                    Id = id,
                    UserPrompt = userPrompt,
                    ExpectedOutput = expectedOutput,
                    SystemPrompt = systemPrompt,
                    Metadata = metadata,
                };

                globalIndex++;
            }
        }
    }

    private static string? GetValue(Dictionary<string, string?[]> columns, string key, int row)
    {
        // case-insensitive lookup
        var matchedKey = columns.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (matchedKey is null) return null;
        var arr = columns[matchedKey];
        return row < arr.Length ? arr[row] : null;
    }

    public async Task<int?> GetCountAsync(CancellationToken ct)
    {
        if (_config.DataFilePath is null) return null;
        var filePath = Path.GetFullPath(_config.DataFilePath);
        if (!File.Exists(filePath)) return null;

        await using var fileStream = File.OpenRead(filePath);
        using var reader = await ParquetReader.CreateAsync(fileStream, cancellationToken: ct);
        long total = 0;
        for (var rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(rg);
            total += rowGroupReader.RowCount;
        }
        return (int)total;
    }
}
