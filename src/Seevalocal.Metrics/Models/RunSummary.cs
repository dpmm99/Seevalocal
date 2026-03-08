using Seevalocal.Core.Models;

namespace Seevalocal.Metrics.Models;

public record RunSummary
{
    public string EvalSetId { get; init; } = "";
    public string RunName { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public double TotalDurationSeconds { get; init; }
    public int TotalItemCount { get; init; }
    public int SucceededItemCount { get; init; }
    public int FailedItemCount { get; init; }
    public double SuccessRatioPercent { get; init; }
    public IReadOnlyDictionary<string, MetricSummary> MetricSummaries { get; init; }
        = new Dictionary<string, MetricSummary>();
}

public record MetricSummary
{
    public string MetricName { get; init; } = "";
    public MetricType Type { get; init; }

    // Numeric fields (null for non-numeric types)
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public double? MeanValue { get; init; }
    public double? MedianValue { get; init; }
    public double? P25Value { get; init; }
    public double? P75Value { get; init; }
    public double? P95Value { get; init; }
    public double? StdDevValue { get; init; }
    public double? SumValue { get; init; }

    // Bool fields
    public int? TrueCount { get; init; }
    public int? FalseCount { get; init; }
    public double? TrueRatioPercent { get; init; }

    // String field
    public string? ModeValue { get; init; }
}
