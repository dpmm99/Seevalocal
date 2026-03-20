using Seevalocal.Core.Models;
using System.Collections.ObjectModel;

namespace Seevalocal.UI.ViewModels;

/// <summary>
/// Statistics for a single numeric metric across all eval items.
/// </summary>
public sealed class MetricStatsViewModel
{
    public string MetricName { get; init; } = "";
    public string StageName { get; init; } = "";
    public int Count { get; init; }
    public int MissingCount { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Average { get; init; }
}

/// <summary>
/// Aggregated statistics for all metrics from a single stage.
/// </summary>
public sealed class StageStatsViewModel
{
    public string StageName { get; init; } = "";
    public ObservableCollection<MetricStatsViewModel> Metrics { get; } = [];
    public bool IsExpanded { get; set; } = true;
}

/// <summary>
/// Collects and calculates metric statistics from eval results.
/// </summary>
public static class MetricStatsCalculator
{
    /// <summary>
    /// Calculates statistics for all numeric metrics across all items, grouped by stage.
    /// </summary>
    public static List<StageStatsViewModel> Calculate(IReadOnlyList<EvalResultViewModel> items)
    {
        // Collect all metrics by stage
        var metricsByStage = new Dictionary<string, Dictionary<string, List<double?>>>();

        foreach (var item in items)
        {
            foreach (var metric in item.Metrics)
            {
                var stageName = metric.SourceStage ?? "Unknown";
                var metricName = metric.Name;

                if (!metricsByStage.TryGetValue(stageName, out var stageMetrics))
                {
                    stageMetrics = [];
                    metricsByStage[stageName] = stageMetrics;
                }

                if (!stageMetrics.TryGetValue(metricName, out var values))
                {
                    values = [];
                    stageMetrics[metricName] = values;
                }

                // Extract numeric value
                double? numericValue = metric.Value switch
                {
                    MetricScalar.DoubleMetric dm => dm.Value,
                    MetricScalar.IntMetric im => im.Value,
                    MetricScalar.BoolMetric bm => bm.Value ? 1.0 : 0.0,
                    _ => null
                };

                values.Add(numericValue);
            }
        }

        // Build stage stats
        var result = new List<StageStatsViewModel>();

        foreach (var stageKvp in metricsByStage.OrderBy(k => k.Key))
        {
            var stageStats = new StageStatsViewModel
            {
                StageName = stageKvp.Key,
                IsExpanded = true
            };

            foreach (var metricKvp in stageKvp.Value.OrderBy(k => k.Key))
            {
                var values = metricKvp.Value;
                var numericValues = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
                var missingCount = values.Count(v => !v.HasValue);

                var stats = new MetricStatsViewModel
                {
                    MetricName = metricKvp.Key,
                    StageName = stageKvp.Key,
                    Count = numericValues.Count,
                    MissingCount = missingCount,
                    Min = numericValues.Count > 0 ? numericValues.Min() : 0,
                    Max = numericValues.Count > 0 ? numericValues.Max() : 0,
                    Average = numericValues.Count > 0 ? numericValues.Average() : 0
                };

                stageStats.Metrics.Add(stats);
            }

            result.Add(stageStats);
        }

        return result;
    }
}
