using Seevalocal.Core.Models;

namespace Seevalocal.Core;

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
    /// Implementations yield items one at a time without buffering the entire set.
    /// </summary>
    IAsyncEnumerable<EvalItem> GetItemsAsync(CancellationToken ct);

    /// <summary>
    /// Total count if known without full enumeration (for progress reporting).
    /// Returns null if unknown.
    /// </summary>
    Task<int?> GetCountAsync(CancellationToken ct);
}
