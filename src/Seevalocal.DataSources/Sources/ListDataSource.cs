using Seevalocal.Core;
using Seevalocal.Core.Models;

namespace Seevalocal.DataSources.Sources;

/// <summary>
/// An in-memory data source that yields items from a list.
/// Useful for testing or inline data sources.
/// </summary>
public sealed class ListDataSource(IEnumerable<EvalItem> items) : IDataSource
{
    private readonly IReadOnlyList<EvalItem> _items = items.ToList().AsReadOnly();

    public string Name => "ListDataSource";

    public Task<int?> GetCountAsync(CancellationToken ct)
        => Task.FromResult<int?>(_items.Count);

    public async IAsyncEnumerable<EvalItem> GetItemsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var item in _items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }
}
