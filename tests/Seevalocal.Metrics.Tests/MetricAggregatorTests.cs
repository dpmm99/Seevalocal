using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Metrics.Aggregation;
using Xunit;

namespace Seevalocal.Metrics.Tests;

public class MetricAggregatorTests
{
    private static EvalResult MakeResult(string id, bool succeeded, params MetricValue[] metrics) =>
        new EvalResult
        {
            EvalItemId = id,
            EvalSetId = "test-set",
            Succeeded = succeeded,
            DurationSeconds = 1.0,
            StartedAt = DateTimeOffset.UtcNow,
            Metrics = metrics
        };

    private static MetricValue IntMetric(string name, int value, string? stage = null) =>
        new MetricValue { Name = name, Value = new MetricScalar.IntMetric(value), SourceStage = stage };

    private static MetricValue DoubleMetric(string name, double value, string? stage = null) =>
        new MetricValue { Name = name, Value = new MetricScalar.DoubleMetric(value), SourceStage = stage };

    private static MetricValue BoolMetric(string name, bool value) =>
        new MetricValue { Name = name, Value = new MetricScalar.BoolMetric(value) };

    private static MetricValue StringMetric(string name, string value) =>
        new MetricValue { Name = name, Value = new MetricScalar.StringMetric(value) };

    [Fact]
    public void Aggregate_EmptyResults_ReturnsZeroCounts()
    {
        var agg = new MetricAggregator();
        var summary = agg.Aggregate("set1", []);
        Assert.Equal(0, summary.TotalItemCount);
        Assert.Equal(0, summary.SucceededItemCount);
        Assert.Equal(0, summary.FailedItemCount);
        Assert.Equal(0.0, summary.SuccessRatioPercent);
        Assert.Empty(summary.MetricSummaries);
    }

    [Fact]
    public void Aggregate_SuccessRatioPercent_IsCorrect()
    {
        var results = new[]
        {
            MakeResult("1", true),
            MakeResult("2", true),
            MakeResult("3", false),
        };
        var agg = new MetricAggregator();
        var summary = agg.Aggregate("set1", results);
        Assert.Equal(3, summary.TotalItemCount);
        Assert.Equal(2, summary.SucceededItemCount);
        Assert.Equal(1, summary.FailedItemCount);
        Assert.Equal(200.0 / 3.0, summary.SuccessRatioPercent, precision: 5);
    }

    [Fact]
    public void Aggregate_NumericMetrics_MeanMedianP95Correct()
    {
        // 10 values: 1..10
        var results = Enumerable.Range(1, 10)
            .Select(static i => MakeResult(i.ToString(), true, DoubleMetric("llmLatencySeconds", i)))
            .ToList();

        var agg = new MetricAggregator();
        var summary = agg.Aggregate("set1", results);

        Assert.True(summary.MetricSummaries.ContainsKey("llmLatencySeconds"));
        var m = summary.MetricSummaries["llmLatencySeconds"];
        Assert.Equal(MetricType.Double, m.Type);
        Assert.Equal(1.0, m.MinValue!.Value, precision: 9);
        Assert.Equal(10.0, m.MaxValue!.Value, precision: 9);
        Assert.Equal(5.5, m.MeanValue!.Value, precision: 9);
        Assert.Equal(5.5, m.MedianValue!.Value, precision: 9); // interpolated
        Assert.Equal(55.0, m.SumValue!.Value, precision: 9);
        // P95 of 1..10 (sorted): rank = 0.95 * 9 = 8.55 → between index 8 (9.0) and 9 (10.0)
        Assert.InRange(m.P95Value!.Value, 9.0, 10.0);
    }

    [Fact]
    public void Aggregate_IntMetrics_TypeIsInt()
    {
        var results = new[]
        {
            MakeResult("1", true, IntMetric("promptTokenCount", 10)),
            MakeResult("2", true, IntMetric("promptTokenCount", 20)),
            MakeResult("3", true, IntMetric("promptTokenCount", 30)),
        };
        var agg = new MetricAggregator();
        var summary = agg.Aggregate("set1", results);
        var m = summary.MetricSummaries["promptTokenCount"];
        Assert.Equal(MetricType.Int, m.Type);
        Assert.Equal(10.0, m.MinValue!.Value, precision: 9);
        Assert.Equal(30.0, m.MaxValue!.Value, precision: 9);
        Assert.Equal(20.0, m.MeanValue!.Value, precision: 9);
    }

    [Fact]
    public void Aggregate_BoolMetrics_TrueRatioCorrect()
    {
        var results = new[]
        {
            MakeResult("1", true, BoolMetric("exactMatch", true)),
            MakeResult("2", true, BoolMetric("exactMatch", false)),
            MakeResult("3", true, BoolMetric("exactMatch", true)),
            MakeResult("4", true, BoolMetric("exactMatch", true)),
        };
        var agg = new MetricAggregator();
        var summary = agg.Aggregate("set1", results);
        var m = summary.MetricSummaries["exactMatch"];
        Assert.Equal(MetricType.Bool, m.Type);
        Assert.Equal(3, m.TrueCount);
        Assert.Equal(1, m.FalseCount);
        Assert.Equal(75.0, m.TrueRatioPercent!.Value, precision: 5);
    }

    [Fact]
    public void Aggregate_BoolMetrics_AllFalse()
    {
        var results = new[]
        {
            MakeResult("1", true, BoolMetric("exactMatch", false)),
            MakeResult("2", true, BoolMetric("exactMatch", false)),
        };
        var agg = new MetricAggregator();
        var summary = agg.Aggregate("set1", results);
        var m = summary.MetricSummaries["exactMatch"];
        Assert.Equal(0, m.TrueCount);
        Assert.Equal(2, m.FalseCount);
        Assert.Equal(0.0, m.TrueRatioPercent!.Value);
    }

    [Fact]
    public void Aggregate_StringMetrics_ModeIsCorrect()
    {
        var results = new[]
        {
            MakeResult("1", true, StringMetric("language", "en")),
            MakeResult("2", true, StringMetric("language", "fr")),
            MakeResult("3", true, StringMetric("language", "en")),
            MakeResult("4", true, StringMetric("language", "en")),
        };
        var agg = new MetricAggregator();
        var summary = agg.Aggregate("set1", results);
        var m = summary.MetricSummaries["language"];
        Assert.Equal(MetricType.String, m.Type);
        Assert.Equal("en", m.ModeValue);
    }

    [Fact]
    public void Aggregate_SingleNumericValue_StdDevIsZero()
    {
        var results = new[]
        {
            MakeResult("1", true, DoubleMetric("llmLatencySeconds", 5.0)),
        };
        var agg = new MetricAggregator();
        var summary = agg.Aggregate("set1", results);
        var m = summary.MetricSummaries["llmLatencySeconds"];
        Assert.Equal(0.0, m.StdDevValue!.Value);
    }

    [Fact]
    public void Aggregate_MultipleMetricNames_AllPresent()
    {
        var results = new[]
        {
            MakeResult("1", true,
                IntMetric("promptTokenCount", 10),
                DoubleMetric("llmLatencySeconds", 1.5),
                BoolMetric("exactMatch", true)),
        };
        var agg = new MetricAggregator();
        var summary = agg.Aggregate("set1", results);
        Assert.Contains("promptTokenCount", summary.MetricSummaries.Keys);
        Assert.Contains("llmLatencySeconds", summary.MetricSummaries.Keys);
        Assert.Contains("exactMatch", summary.MetricSummaries.Keys);
    }

    [Fact]
    public void Aggregate_EvalSetIdPreserved()
    {
        var agg = new MetricAggregator();
        var summary = agg.Aggregate("my-eval-set", []);
        Assert.Equal("my-eval-set", summary.EvalSetId);
    }
}
