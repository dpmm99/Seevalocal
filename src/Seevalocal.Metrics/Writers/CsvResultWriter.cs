using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Metrics.Models;
using System.Globalization;
using System.Text;

namespace Seevalocal.Metrics.Writers;

/// <summary>
/// Writes summary.csv at finalization with one row per eval result.
/// Columns include standard fields plus one column per unique metric name.
/// Thread-safe buffering of results; file written only at finalization.
/// </summary>
public sealed class CsvResultWriter(
    string outputDir,
    bool writeSummaryCsv = true,
    ILogger<CsvResultWriter>? logger = null) : IResultWriter
{
    private readonly string _outputDir = outputDir ?? throw new ArgumentNullException(nameof(outputDir));
    private readonly bool _writeSummaryCsv = writeSummaryCsv;
    private readonly ILogger<CsvResultWriter> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CsvResultWriter>.Instance;
    private readonly List<EvalResult> _buffer = [];
    private readonly Lock _bufferLock = new();

    public Task WriteResultAsync(EvalResult result, CancellationToken ct)
    {
        lock (_bufferLock)
            _buffer.Add(result);
        return Task.CompletedTask;
    }

    public Task WriteSummaryAsync(RunSummary summary, CancellationToken ct) => Task.CompletedTask;

    public async Task FinalizeAsync(CancellationToken ct)
    {
        if (!_writeSummaryCsv) return;

        List<EvalResult> snapshot;
        lock (_bufferLock)
            snapshot = [.. _buffer];

        if (snapshot.Count == 0) return;

        // Collect all unique metric names (preserving first-seen order)
        var metricNames = snapshot
            .SelectMany(static r => r.Metrics.Select(static m => m.Name))
            .Distinct()
            .ToList();

        var filePath = Path.Combine(_outputDir, "summary.csv");
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true };

        await using var writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
        await using var csv = new CsvWriter(writer, config);

        // Write header
        csv.WriteField("evalItemId");
        csv.WriteField("succeeded");
        csv.WriteField("durationSeconds");
        foreach (var name in metricNames)
            csv.WriteField(name);
        await csv.NextRecordAsync().ConfigureAwait(false);

        // Write rows
        foreach (var result in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            var metricLookup = result.Metrics
                .GroupBy(static m => m.Name)
                .ToDictionary(static g => g.Key, static g => g.Last().Value.ToObject());

            csv.WriteField(result.EvalItemId);
            csv.WriteField(result.Succeeded ? "true" : "false");
            csv.WriteField(result.DurationSeconds.ToString("F6", CultureInfo.InvariantCulture));

            foreach (var name in metricNames)
            {
                if (metricLookup.TryGetValue(name, out var val) && val is not null)
                    csv.WriteField(Convert.ToString(val, CultureInfo.InvariantCulture));
                else
                    csv.WriteField("");
            }

            await csv.NextRecordAsync().ConfigureAwait(false);
        }

        _logger.LogInformation("[CsvResultWriter] Wrote {Count} rows to {FilePath}", snapshot.Count, filePath);
    }
}
