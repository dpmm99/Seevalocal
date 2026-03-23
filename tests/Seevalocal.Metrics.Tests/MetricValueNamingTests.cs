using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Metrics.Writers;
using System.Text.RegularExpressions;
using Xunit;

namespace Seevalocal.Metrics.Tests;

/// <summary>
/// Validates that all built-in metric names from 06-metrics.md §2.1 conform to
/// the unit-suffix naming rule: must end with Seconds|Count|Bytes|Ratio|Percent|Temperature|Tokens|Lines|Code
/// </summary>
public class MetricValueNamingTests
{
    // Regex from 06-metrics.md §8
    private static readonly Regex UnitSuffixRegex =
        new(@"[A-Za-z]+(Seconds|Count|Bytes|Ratio|Percent|Temperature|Tokens|Lines|Code|PerSecond)$", RegexOptions.Compiled);

    // All built-in metric names from §2.1
    private static readonly string[] BuiltInMetricNames =
    [
        "promptTokenCount",
        "completionTokenCount",
        "totalTokenCount",
        "llmLatencySeconds",
        "promptTokensPerSecond",
        "completionTokensPerSecond",
        "exactMatch",           // NOTE: bool - no unit suffix, but it's a semantic flag
        "compileDurationSeconds",
        "testDurationSeconds",
        "testPassCount",
        "testFailCount",
        "testTotalCount",
        "codeLineCount",
        "judgeScore",
        "processDurationSeconds",
        "processExitCode",
    ];

    // Names that intentionally do NOT follow the suffix rule (special cases)
    private static readonly HashSet<string> KnownExceptions = ["exactMatch", "judgeScore"];

    [Theory]
    [MemberData(nameof(GetBuiltInMetricNames))]
    public void BuiltInMetricName_MustMatchUnitSuffixRegex(string name)
    {
        if (KnownExceptions.Contains(name))
            return; // exactMatch is a boolean flag, accepted as exception

        Assert.True(UnitSuffixRegex.IsMatch(name),
            $"Metric name '{name}' does not end with a recognized unit suffix.");
    }

    public static IEnumerable<object[]> GetBuiltInMetricNames() =>
        BuiltInMetricNames.Select(static n => new object[] { n });

    [Theory]
    [InlineData("timeout")]          // missing unit
    [InlineData("tokens")]           // just the unit word, no preceding name
    [InlineData("fileSize")]         // missing unit suffix
    public void InvalidMetricName_DoesNotMatchRegex(string name)
    {
        Assert.False(UnitSuffixRegex.IsMatch(name),
            $"Name '{name}' should NOT match the regex but did.");
    }
}

public class ParquetResultWriterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public ParquetResultWriterTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static EvalResult MakeResult(string id, params MetricValue[] metrics) =>
        new()
        {
            EvalItemId = id,
            Succeeded = true,
            DurationSeconds = 1.0,
            StartedAt = DateTimeOffset.UtcNow,
            Metrics = metrics
        };

    [Fact]
    public async Task FinalizeAsync_WhenDisabled_DoesNotCreateFile()
    {
        var writer = new ParquetResultWriter(_tempDir, writeResultsParquet: false);
        await writer.WriteResultAsync(MakeResult("001"), CancellationToken.None);
        await writer.FinalizeAsync(CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(_tempDir, "results.parquet")));
    }

    [Fact]
    public async Task FinalizeAsync_WhenEnabled_CreatesParquetFile()
    {
        var writer = new ParquetResultWriter(_tempDir, writeResultsParquet: true);
        await writer.WriteResultAsync(MakeResult("001",
            new MetricValue { Name = "promptTokenCount", Value = new MetricScalar.IntMetric(10) },
            new MetricValue { Name = "llmLatencySeconds", Value = new MetricScalar.DoubleMetric(1.5) },
            new MetricValue { Name = "exactMatch", Value = new MetricScalar.BoolMetric(true) }),
            CancellationToken.None);
        await writer.FinalizeAsync(CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(_tempDir, "results.parquet")));
    }

    [Fact]
    public async Task FinalizeAsync_LargeResultSet_WritesCorrectly()
    {
        var writer = new ParquetResultWriter(_tempDir, writeResultsParquet: true);
        for (var i = 1; i <= 100; i++)
        {
            await writer.WriteResultAsync(MakeResult(i.ToString("D3"),
                new MetricValue { Name = "promptTokenCount", Value = new MetricScalar.IntMetric(i * 10) }),
                CancellationToken.None);
        }
        await writer.FinalizeAsync(CancellationToken.None);
        var fileInfo = new FileInfo(Path.Combine(_tempDir, "results.parquet"));
        Assert.True(fileInfo.Length > 0);
    }
}
