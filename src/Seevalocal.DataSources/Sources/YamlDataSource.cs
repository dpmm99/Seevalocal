using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.DataSources.Internal;
using YamlDotNet.RepresentationModel;

namespace Seevalocal.DataSources.Sources;

internal sealed class YamlDataSource(string name, DataSourceConfig config, ILogger logger) : IDataSource
{
    private readonly DataSourceConfig _config = config;
    private readonly ILogger _logger = logger;

    public string Name { get; } = name;

    public async IAsyncEnumerable<EvalItem> GetItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var mapping = _config.FieldMapping ?? new FieldMapping();
        var filePath = Path.GetFullPath(_config.DataFilePath!);
        _logger.LogDebug("[{Name}] Loading YAML from {Path}", Name, filePath);

        var yaml = await File.ReadAllTextAsync(filePath, ct);
        YamlStream stream = [];
        stream.Load(new StringReader(yaml));

        if (stream.Documents.Count == 0)
            yield break;

        var root = stream.Documents[0].RootNode;
        if (root is not YamlSequenceNode sequence)
            throw new InvalidDataException($"[{Name}] Expected a YAML sequence at root in {filePath}");

        var index = 0;
        foreach (var node in sequence.Children)
        {
            ct.ThrowIfCancellationRequested();
            if (node is not YamlMappingNode mapping_node)
                continue;

            yield return ParseNode(mapping_node, mapping, index++);
        }
    }

    private EvalItem ParseNode(YamlMappingNode node, FieldMapping mapping, int index)
    {
        var idField = mapping.IdField ?? "id";
        var userPromptField = mapping.UserPromptField ?? "prompt";
        var expectedOutputField = mapping.ExpectedOutputField ?? "expected";
        var systemPromptField = mapping.SystemPromptField;

        var id = GetString(node, idField) ?? IdGenerator.Generate(Name, index);
        var userPrompt = GetString(node, userPromptField) ?? "";
        var expectedOutput = expectedOutputField is not null
            ? GetString(node, expectedOutputField)
            : null;
        var systemPrompt = systemPromptField is not null
            ? GetString(node, systemPromptField)
            : _config.DefaultSystemPrompt;

        Dictionary<string, string> metadata = [];
        foreach (var field in mapping.MetadataFields)
        {
            var val = GetString(node, field);
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

    private static string? GetString(YamlMappingNode node, string key)
    {
        var keyNode = new YamlScalarNode(key);
        return node.Children.TryGetValue(keyNode, out var value) && value is YamlScalarNode scalar ? scalar.Value : null;
    }

    public async Task<int?> GetCountAsync(CancellationToken ct)
    {
        if (_config.DataFilePath is null) return null;
        var filePath = Path.GetFullPath(_config.DataFilePath);
        if (!File.Exists(filePath)) return null;

        var yaml = await File.ReadAllTextAsync(filePath, ct);
        YamlStream stream = [];
        stream.Load(new StringReader(yaml));
        return stream.Documents.Count == 0 ? 0 : stream.Documents[0].RootNode is YamlSequenceNode seq ? seq.Children.Count : null;
    }
}
