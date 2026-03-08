using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.DataSources.Internal;
using System.Text.Json;

namespace Seevalocal.DataSources.Sources;

internal sealed class JsonDataSource(string name, DataSourceConfig config, bool isJsonl, ILogger logger) : IDataSource
{
    private readonly DataSourceConfig _config = config;
    private readonly bool _isJsonl = isJsonl;
    private readonly ILogger _logger = logger;

    public string Name { get; } = name;

    public async IAsyncEnumerable<EvalItem> GetItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var mapping = _config.FieldMapping ?? new FieldMapping();
        var filePath = Path.GetFullPath(_config.DataFilePath!);
        _logger.LogDebug("[{Name}] Loading from {Path} (jsonl={IsJsonl})", Name, filePath, _isJsonl);

        if (_isJsonl)
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct);
            var index = 0;
            foreach (var line in lines)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;

                using var doc = JsonDocument.Parse(line);
                yield return ParseObject(doc.RootElement, mapping, index++);
            }
        }
        else
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"[{Name}] Expected a JSON array in {filePath}");

            var index = 0;
            foreach (var element in root.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                yield return ParseObject(element, mapping, index++);
            }
        }
    }

    private EvalItem ParseObject(JsonElement el, FieldMapping mapping, int index)
    {
        var id = GetString(el, mapping.IdField)
                 ?? IdGenerator.Generate(Name, index);

        var userPrompt = GetString(el, mapping.UserPromptField) ?? "";
        var expectedOutput = mapping.ExpectedOutputField is not null
            ? GetString(el, mapping.ExpectedOutputField)
            : null;
        var systemPrompt = mapping.SystemPromptField is not null
            ? GetString(el, mapping.SystemPromptField)
            : _config.DefaultSystemPrompt;

        Dictionary<string, string> metadata = [];
        foreach (var field in mapping.MetadataFields)
        {
            var val = GetString(el, field);
            if (val is not null)
                metadata[field] = val;
        }

        return new EvalItem
        {
            Id = id,
            UserPrompt = userPrompt,
            ExpectedOutput = expectedOutput,
            SystemPrompt = systemPrompt,
            Metadata = metadata,
        };
    }

    private static string? GetString(JsonElement el, string key)
    {
        return el.TryGetProperty(key, out var prop) ? prop.ValueKind == JsonValueKind.Null ? null : prop.GetString() : null;
    }

    public async Task<int?> GetCountAsync(CancellationToken ct)
    {
        if (_config.DataFilePath is null) return null;
        var filePath = Path.GetFullPath(_config.DataFilePath);
        if (!File.Exists(filePath)) return null;

        if (_isJsonl)
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct);
            return lines.Count(static l => !string.IsNullOrWhiteSpace(l));
        }

        var json = await File.ReadAllTextAsync(filePath, ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : null;
    }
}
