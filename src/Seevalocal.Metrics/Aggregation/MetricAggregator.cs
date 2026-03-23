using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Metrics.Models;

namespace Seevalocal.Metrics.Aggregation;

/// <summary>
/// Aggregates per-item EvalResult lists into run-level summaries.
/// </summary>
public sealed class MetricAggregator
{
    /// <summary>
    /// Compute per-metric summary statistics across all results.
    /// String metrics: mode (most common value) only.
    /// Bool metrics: true count, false count, true ratio.
    /// Numeric metrics: min, max, mean, median, p25, p75, p95, stddev, sum.
    /// </summary>
    public static RunSummary Aggregate(
        IReadOnlyList<EvalResult> results,
        string runName = "",
        DateTimeOffset? startedAt = null)
    {
        var succeeded = results.Count(static r => r.Succeeded);
        var failed = results.Count - succeeded;
        var totalDurationSeconds = results.Sum(static r => r.DurationSeconds);

        // Group all metrics by name
        Dictionary<string, List<MetricScalar>> metricsByName = [];
        foreach (var result in results)
        {
            foreach (var metric in result.Metrics)
            {
                if (!metricsByName.TryGetValue(metric.Name, out var list))
                {
                    list = [];
                    metricsByName[metric.Name] = list;
                }
                list.Add(metric.Value);
            }
        }

        Dictionary<string, MetricSummary> summaries = [];
        foreach ((var name, var values) in metricsByName)
        {
            summaries[name] = ComputeSummary(name, values);
        }

        return new RunSummary
        {
            RunName = runName,
            StartedAt = startedAt ?? DateTimeOffset.UtcNow,
            TotalDurationSeconds = totalDurationSeconds,
            TotalItemCount = results.Count,
            SucceededItemCount = succeeded,
            FailedItemCount = failed,
            SuccessRatioPercent = results.Count == 0 ? 0.0 : (succeeded / (double)results.Count) * 100.0,
            MetricSummaries = summaries
        };
    }

    private static MetricSummary ComputeSummary(string name, List<MetricScalar> values)
    {
        return values.Count == 0
            ? new MetricSummary { MetricName = name, Type = MetricType.String }
            : values[0] switch
            {
                MetricScalar.IntMetric => ComputeNumericSummary(name, MetricType.Int,
                    values.OfType<MetricScalar.IntMetric>().Select(static v => (double)v.Value).ToList()),
                MetricScalar.DoubleMetric => ComputeNumericSummary(name, MetricType.Double,
                    values.OfType<MetricScalar.DoubleMetric>().Select(static v => v.Value).ToList()),
                MetricScalar.BoolMetric => ComputeBoolSummary(name,
                    values.OfType<MetricScalar.BoolMetric>().Select(static v => v.Value).ToList()),
                MetricScalar.StringMetric => ComputeStringSummary(name,
                    values.OfType<MetricScalar.StringMetric>().Select(static v => v.Value).ToList()),
                _ => new MetricSummary { MetricName = name, Type = MetricType.String }
            };
    }

    private static MetricSummary ComputeNumericSummary(string name, MetricType type, List<double> values)
    {
        if (values.Count == 0)
            return new MetricSummary { MetricName = name, Type = type };

        var sorted = values.Order().ToList();
        var mean = sorted.Average();
        var sum = sorted.Sum();
        var variance = values.Count > 1
            ? sorted.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1)
            : 0.0;

        return new MetricSummary
        {
            MetricName = name,
            Type = type,
            MinValue = sorted[0],
            MaxValue = sorted[^1],
            MeanValue = mean,
            MedianValue = Percentile(sorted, 0.50),
            P25Value = Percentile(sorted, 0.25),
            P75Value = Percentile(sorted, 0.75),
            P95Value = Percentile(sorted, 0.95),
            StdDevValue = Math.Sqrt(variance),
            SumValue = sum
        };
    }

    private static MetricSummary ComputeBoolSummary(string name, List<bool> values)
    {
        var trueCount = values.Count(static v => v);
        var falseCount = values.Count - trueCount;
        return new MetricSummary
        {
            MetricName = name,
            Type = MetricType.Bool,
            TrueCount = trueCount,
            FalseCount = falseCount,
            TrueRatioPercent = values.Count == 0 ? 0.0 : (trueCount / (double)values.Count) * 100.0
        };
    }

    private static MetricSummary ComputeStringSummary(string name, List<string> values)
    {
        var mode = values
            .GroupBy(static v => v)
            .OrderByDescending(static g => g.Count())
            .FirstOrDefault()?.Key;

        return new MetricSummary
        {
            MetricName = name,
            Type = MetricType.String,
            ModeValue = mode
        };
    }

    /// <summary>
    /// Compute percentile using nearest-rank method on a pre-sorted list.
    /// </summary>
    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0.0;
        if (sorted.Count == 1) return sorted[0];

        var rank = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        var fraction = rank - lower;

        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }
}
