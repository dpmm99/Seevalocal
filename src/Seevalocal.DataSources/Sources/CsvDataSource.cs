using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.DataSources.Internal;
using System.Globalization;

namespace Seevalocal.DataSources.Sources;

internal sealed class CsvDataSource(string name, DataSourceConfig config, ILogger logger) : IDataSource
{
    private readonly DataSourceConfig _config = config;
    private readonly ILogger _logger = logger;

    public string Name { get; } = name;

    public async IAsyncEnumerable<EvalItem> GetItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var mapping = _config.FieldMapping ?? new FieldMapping();
        var filePath = Path.GetFullPath(_config.DataFilePath!);
        _logger.LogDebug("[{Name}] Loading CSV from {Path}", Name, filePath);

        // Read entire file to avoid issues with async streaming from CsvHelper
        var csvContent = await File.ReadAllTextAsync(filePath, ct);

        using var reader = new StringReader(csvContent);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };
        using var csv = new CsvReader(reader, csvConfig);

        _ = await csv.ReadAsync();
        _ = csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];

        ValidateHeaders(headers, mapping, filePath);

        var index = 0;
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            var id = HasColumn(headers, mapping.IdField)
                ? csv.GetField<string>(mapping.IdField)
                : null;
            id ??= IdGenerator.Generate(Name, index);

            var userPrompt = csv.GetField<string>(mapping.UserPromptField) ?? "";
            var expectedOutput = mapping.ExpectedOutputField is not null && HasColumn(headers, mapping.ExpectedOutputField)
                ? csv.GetField<string>(mapping.ExpectedOutputField)
                : null;
            var systemPrompt = mapping.SystemPromptField is not null && HasColumn(headers, mapping.SystemPromptField)
                ? csv.GetField<string>(mapping.SystemPromptField)
                : _config.DefaultSystemPrompt;

            Dictionary<string, string> metadata = [];
            foreach (var field in mapping.MetadataFields)
            {
                if (HasColumn(headers, field))
                {
                    var val = csv.GetField<string>(field);
                    if (val is not null)
                        metadata[field] = val;
                }
            }

            yield return new EvalItem
            {
                Id = id,
                UserPrompt = userPrompt,
                ExpectedOutput = expectedOutput,
                SystemPrompt = systemPrompt,
                Metadata = metadata,
            };

            index++;
        }
    }

    private static bool HasColumn(string[] headers, string name)
        => Array.Exists(headers, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void ValidateHeaders(string[] headers, FieldMapping mapping, string filePath)
    {
        if (!HasColumn(headers, mapping.UserPromptField))
            throw new InvalidDataException(
                $"[{Name}] Column '{mapping.UserPromptField}' not found in {filePath}. Available: {string.Join(", ", headers)}");

        foreach (var field in mapping.MetadataFields)
        {
            if (!HasColumn(headers, field))
                _logger.LogWarning("[{Name}] MetadataField '{Field}' not found in CSV columns", Name, field);
        }
    }

    public async Task<int?> GetCountAsync(CancellationToken ct)
    {
        if (_config.DataFilePath is null) return null;
        var filePath = Path.GetFullPath(_config.DataFilePath);
        if (!File.Exists(filePath)) return null;

        var lines = await File.ReadAllLinesAsync(filePath, ct);
        // subtract 1 for header
        return Math.Max(0, lines.Count(static l => !string.IsNullOrWhiteSpace(l)) - 1);
    }
}
