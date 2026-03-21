using Seevalocal.Core;
using Seevalocal.Metrics.Models;

namespace Seevalocal.Metrics;

public interface IResultWriter
{
    /// <summary>Called as each result arrives (streaming support).</summary>
    Task WriteResultAsync(EvalResult result, CancellationToken ct);

    /// <summary>Called once after all results are collected.</summary>
    Task WriteSummaryAsync(RunSummary summary, CancellationToken ct);

    /// <summary>Flush and close.</summary>
    Task FinalizeAsync(CancellationToken ct);
}