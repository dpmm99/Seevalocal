using Seevalocal.Core;
using Seevalocal.Metrics.Models;

namespace Seevalocal.Metrics.Writers;

public sealed class CompositeResultWriter(IReadOnlyList<IResultWriter> writers) : IResultWriter
{
    private readonly IReadOnlyList<IResultWriter> _writers = writers ?? throw new ArgumentNullException(nameof(writers));

    public async Task WriteResultAsync(EvalResult result, CancellationToken ct)
    {
        foreach (var writer in _writers)
            await writer.WriteResultAsync(result, ct).ConfigureAwait(false);
    }

    public async Task WriteSummaryAsync(RunSummary summary, CancellationToken ct)
    {
        foreach (var writer in _writers)
            await writer.WriteSummaryAsync(summary, ct).ConfigureAwait(false);
    }

    public async Task FinalizeAsync(CancellationToken ct)
    {
        foreach (var writer in _writers)
            await writer.FinalizeAsync(ct).ConfigureAwait(false);
    }
}
