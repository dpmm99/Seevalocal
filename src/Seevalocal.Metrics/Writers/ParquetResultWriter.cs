using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Metrics.Models;

namespace Seevalocal.Metrics.Writers;

/// <summary>
/// Writes results.parquet at finalization with typed columns.
/// One row per eval result, one column per unique metric name.
/// </summary>
public sealed class ParquetResultWriter(
    string outputDir,
    bool writeResultsParquet = false,
    ILogger<ParquetResultWriter>? logger = null) : IResultWriter
{
    private readonly string _outputDir = outputDir ?? throw new ArgumentNullException(nameof(outputDir));
    private readonly bool _writeResultsParquet = writeResultsParquet;
    private readonly ILogger<ParquetResultWriter> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ParquetResultWriter>.Instance;
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
        if (!_writeResultsParquet) return;

        List<EvalResult> snapshot;
        lock (_bufferLock)
            snapshot = [.. _buffer];

        if (snapshot.Count == 0) return;

        // Determine metric names and their dominant types
        var metricTypeMap = DetectMetricTypes(snapshot);

        // Build schema
        List<Field> fields =
        [
            new DataField<string>("evalItemId"),
            new DataField<bool>("succeeded"),
            new DataField<double>("durationSeconds"),
        ];

        foreach ((var name, var type) in metricTypeMap)
        {
            fields.Add(type switch
            {
                MetricType.Int => new DataField<int?>(name),
                MetricType.Double => new DataField<double?>(name),
                MetricType.Bool => new DataField<bool?>(name),
                _ => new DataField<string?>(name)
            });
        }

        var schema = new ParquetSchema(fields);
        var filePath = Path.Combine(_outputDir, "results.parquet");

        await using var fileStream = File.Create(filePath);
        await using var writer = await ParquetWriter.CreateAsync(schema, fileStream, cancellationToken: ct)
            .ConfigureAwait(false);

        using var groupWriter = writer.CreateRowGroup();

        // Standard columns
        var evalItemIds = snapshot.Select(r => r.EvalItemId).ToArray();
        var succeeded = snapshot.Select(r => r.Succeeded).ToArray();
        var durations = snapshot.Select(r => r.DurationSeconds).ToArray();

        await groupWriter.WriteColumnAsync(new DataColumn((DataField)fields[0], evalItemIds), ct).ConfigureAwait(false);
        await groupWriter.WriteColumnAsync(new DataColumn((DataField)fields[1], succeeded), ct).ConfigureAwait(false);
        await groupWriter.WriteColumnAsync(new DataColumn((DataField)fields[2], durations), ct).ConfigureAwait(false);

        // Metric columns
        var fieldIndex = 3;
        foreach ((var name, var type) in metricTypeMap)
        {
            var field = (DataField)fields[fieldIndex++];
            var metricLookups = snapshot.Select(r =>
                r.Metrics.LastOrDefault(m => m.Name == name)?.Value);

            switch (type)
            {
                case MetricType.Int:
                    {
                        var values = metricLookups.Select(v => v is MetricScalar.IntMetric i ? (int?)i.Value : null).ToArray();
                        await groupWriter.WriteColumnAsync(new DataColumn(field, values), ct).ConfigureAwait(false);
                        break;
                    }
                case MetricType.Double:
                    {
                        var values = metricLookups.Select(v => v switch
                        {
                            MetricScalar.DoubleMetric d => d.Value,
                            MetricScalar.IntMetric i => (double?)i.Value,
                            _ => null
                        }).ToArray();
                        await groupWriter.WriteColumnAsync(new DataColumn(field, values), ct).ConfigureAwait(false);
                        break;
                    }
                case MetricType.Bool:
                    {
                        var values = metricLookups.Select(v => v is MetricScalar.BoolMetric b ? (bool?)b.Value : null).ToArray();
                        await groupWriter.WriteColumnAsync(new DataColumn(field, values), ct).ConfigureAwait(false);
                        break;
                    }
                default:
                    {
                        var values = metricLookups.Select(v => v?.ToObject()?.ToString()).ToArray();
                        await groupWriter.WriteColumnAsync(new DataColumn(field, values), ct).ConfigureAwait(false);
                        break;
                    }
            }
        }

        _logger.LogInformation("[ParquetResultWriter] Wrote {Count} rows to {FilePath}", snapshot.Count, filePath);
    }

    private static Dictionary<string, MetricType> DetectMetricTypes(List<EvalResult> results)
    {
        Dictionary<string, MetricType> typeMap = [];
        foreach (var result in results)
        {
            foreach (var metric in result.Metrics)
            {
                if (!typeMap.ContainsKey(metric.Name))
                {
                    typeMap[metric.Name] = metric.Value switch
                    {
                        MetricScalar.IntMetric => MetricType.Int,
                        MetricScalar.DoubleMetric => MetricType.Double,
                        MetricScalar.BoolMetric => MetricType.Bool,
                        _ => MetricType.String
                    };
                }
            }
        }
        return typeMap;
    }
}
