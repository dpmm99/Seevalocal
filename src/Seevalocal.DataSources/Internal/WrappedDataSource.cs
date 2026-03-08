using Seevalocal.Core;
using Seevalocal.Core.Models;

namespace Seevalocal.DataSources.Internal;

/// <summary>
/// Wraps any IDataSource to apply post-processing:
/// - Template injection
/// - Shuffle (if configured)
/// - MaxItemCount limit
/// - Duplicate ID detection
/// </summary>
internal sealed class WrappedDataSource(
    IDataSource inner,
    DataSourceConfig config,
    PromptTemplateEngine templateEngine) : IDataSource
{
    private readonly IDataSource _inner = inner;
    private readonly DataSourceConfig _config = config;
    private readonly PromptTemplateEngine _templateEngine = templateEngine;

    public string Name => _inner.Name;

    public async IAsyncEnumerable<EvalItem> GetItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        HashSet<string> seenIds = [];
        var emitted = 0;

        // If shuffle is requested, we must buffer all items first
        if (_config.ShuffleRandomSeed.HasValue)
        {
            List<EvalItem> buffer = [];
            await foreach (var item in _inner.GetItemsAsync(ct))
                buffer.Add(item);

            var rng = new Random(_config.ShuffleRandomSeed.Value);
            for (var i = buffer.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }

            foreach (var item in buffer)
            {
                ct.ThrowIfCancellationRequested();
                if (_config.MaxItemCount.HasValue && emitted >= _config.MaxItemCount.Value)
                    yield break;

                var processed = ProcessItem(item, seenIds);
                if (processed is not null)
                {
                    yield return processed;
                    emitted++;
                }
            }
        }
        else
        {
            await foreach (var item in _inner.GetItemsAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                if (_config.MaxItemCount.HasValue && emitted >= _config.MaxItemCount.Value)
                    yield break;

                var processed = ProcessItem(item, seenIds);
                if (processed is not null)
                {
                    yield return processed;
                    emitted++;
                }
            }
        }
    }

    private EvalItem? ProcessItem(EvalItem item, HashSet<string> seenIds)
    {
        return !seenIds.Add(item.Id)
            ? throw new InvalidDataException(
                $"[DataSource:{Name}] Duplicate ID '{item.Id}' detected. " +
                "All IDs within a dataset must be unique.")
            : PromptTemplateEngine.Apply(item, _config);
    }

    public Task<int?> GetCountAsync(CancellationToken ct)
    {
        return _config.MaxItemCount.HasValue ? Task.FromResult<int?>(_config.MaxItemCount.Value) : _inner.GetCountAsync(ct);
    }
}
