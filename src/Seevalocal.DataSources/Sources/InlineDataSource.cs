using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.DataSources.Internal;

namespace Seevalocal.DataSources.Sources;

internal sealed class InlineDataSource(string name, DataSourceConfig config) : IDataSource
{
    private readonly DataSourceConfig _config = config;

    public string Name { get; } = name;

    public async IAsyncEnumerable<EvalItem> GetItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var items = _config.InlineItems ?? [];
        var index = 0;
        foreach (var dto in items)
        {
            ct.ThrowIfCancellationRequested();

            var id = dto.Id ?? IdGenerator.Generate(Name, index);
            var systemPrompt = dto.SystemPrompt ?? _config.DefaultSystemPrompt;

            yield return new EvalItem
            {
                Id = id,
                UserPrompt = dto.UserPrompt,
                ExpectedOutput = dto.ExpectedOutput,
                SystemPrompt = systemPrompt,
                Metadata = dto.Metadata is not null
                    ? new Dictionary<string, string>(dto.Metadata)
                    : [],
                ArtifactFilePath = dto.ArtifactFilePath,
            };

            index++;
        }

        await Task.CompletedTask; // satisfy async IAsyncEnumerable
    }

    public Task<int?> GetCountAsync(CancellationToken ct)
        => Task.FromResult(_config.InlineItems?.Count);
}
