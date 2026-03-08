using Microsoft.Extensions.Logging.Abstractions;

namespace Seevalocal.DataSources.Tests;

/// <summary>
/// Test helper methods and constants for data source tests.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// A null logger factory for tests that don't need logging.
    /// </summary>
    public static readonly NullLoggerFactory NullLoggerFactory = new();

    /// <summary>
    /// Collects all items from an IDataSource into a list.
    /// </summary>
    public static async Task<List<Core.Models.EvalItem>> CollectAsync(
        Core.IDataSource dataSource,
        CancellationToken ct = default)
    {
        var items = new List<Core.Models.EvalItem>();
        await foreach (var item in dataSource.GetItemsAsync(ct))
        {
            items.Add(item);
        }
        return items;
    }
}
