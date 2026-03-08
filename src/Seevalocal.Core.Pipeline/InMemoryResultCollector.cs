using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Seevalocal.Core.Pipeline;

/// <summary>
/// Thread-safe in-memory result collector.
/// Suitable for use as the primary collector or as a component in a composite collector.
/// </summary>
public sealed class InMemoryResultCollector(ILogger<InMemoryResultCollector> logger) : IResultCollector
{
    private readonly ConcurrentBag<EvalResult> _results = [];
    private readonly ILogger<InMemoryResultCollector> _logger = logger;

    public Task CollectAsync(EvalResult result, CancellationToken ct)
    {
        _results.Add(result);
        _logger.LogDebug("Collected result for EvalItem {EvalItemId}: Succeeded={Succeeded}",
            result.EvalItemId, result.Succeeded);
        return Task.CompletedTask;
    }

    public Task FinalizeAsync(CancellationToken ct)
    {
        _logger.LogInformation("InMemoryResultCollector finalized with {ResultCount} results", _results.Count);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EvalResult> GetResults() =>
        _results.ToArray();   // snapshot — safe to call concurrently
}
