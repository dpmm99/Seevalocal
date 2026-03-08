using CsvHelper;
using CsvHelper.Configuration;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Metrics.Writers;
using System.Globalization;
using Xunit;

namespace Seevalocal.Metrics.Tests;

public class CsvResultWriterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public CsvResultWriterTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static EvalResult MakeResult(string id, bool succeeded, params MetricValue[] metrics) =>
        new()
        {
            EvalItemId = id,
            EvalSetId = "test-set",
            Succeeded = succeeded,
            DurationSeconds = 1.0,
            StartedAt = DateTimeOffset.UtcNow,
            Metrics = metrics
        };

    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));
        _ = csv.Read();
        _ = csv.ReadHeader();
        var headers = csv.HeaderRecord!;
        List<Dictionary<string, string>> rows = [];
        while (csv.Read())
        {
            Dictionary<string, string> row = [];
            foreach (var h in headers)
                row[h] = csv.GetField(h) ?? "";
            rows.Add(row);
        }
        return rows;
    }

    [Fact]
    public async Task FinalizeAsync_CreatesSummaryCsv()
    {
        var writer = new CsvResultWriter(_tempDir);
        await writer.WriteResultAsync(MakeResult("001", true,
            new MetricValue { Name = "promptTokenCount", Value = new MetricScalar.IntMetric(10) }),
            CancellationToken.None);
        await writer.FinalizeAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_tempDir, "summary.csv")));
    }

    [Fact]
    public async Task FinalizeAsync_HeadersFromUnionOfAllMetricNames()
    {
        var writer = new CsvResultWriter(_tempDir);
        await writer.WriteResultAsync(MakeResult("001", true,
            new MetricValue { Name = "promptTokenCount", Value = new MetricScalar.IntMetric(10) }),
            CancellationToken.None);
        await writer.WriteResultAsync(MakeResult("002", true,
            new MetricValue { Name = "llmLatencySeconds", Value = new MetricScalar.DoubleMetric(1.5) }),
            CancellationToken.None);
        await writer.FinalizeAsync(CancellationToken.None);

        var rows = ReadCsv(Path.Combine(_tempDir, "summary.csv"));
        Assert.All(rows, static r => Assert.True(r.ContainsKey("promptTokenCount")));
        Assert.All(rows, static r => Assert.True(r.ContainsKey("llmLatencySeconds")));
    }

    [Fact]
    public async Task FinalizeAsync_MissingMetricsAreEmptyCells()
    {
        var writer = new CsvResultWriter(_tempDir);
        await writer.WriteResultAsync(MakeResult("001", true,
            new MetricValue { Name = "promptTokenCount", Value = new MetricScalar.IntMetric(10) }),
            CancellationToken.None);
        // second result missing promptTokenCount
        await writer.WriteResultAsync(MakeResult("002", true,
            new MetricValue { Name = "llmLatencySeconds", Value = new MetricScalar.DoubleMetric(1.5) }),
            CancellationToken.None);
        await writer.FinalizeAsync(CancellationToken.None);

        var rows = ReadCsv(Path.Combine(_tempDir, "summary.csv"));
        // row for 001 should have empty llmLatencySeconds
        var row001 = rows.First(static r => r["evalItemId"] == "001");
        Assert.Equal("", row001["llmLatencySeconds"]);
        // row for 002 should have empty promptTokenCount
        var row002 = rows.First(static r => r["evalItemId"] == "002");
        Assert.Equal("", row002["promptTokenCount"]);
    }

    [Fact]
    public async Task FinalizeAsync_SucceededField_IsLowercase()
    {
        var writer = new CsvResultWriter(_tempDir);
        await writer.WriteResultAsync(MakeResult("001", true), CancellationToken.None);
        await writer.WriteResultAsync(MakeResult("002", false), CancellationToken.None);
        await writer.FinalizeAsync(CancellationToken.None);

        var rows = ReadCsv(Path.Combine(_tempDir, "summary.csv"));
        Assert.Equal("true", rows[0]["succeeded"]);
        Assert.Equal("false", rows[1]["succeeded"]);
    }

    [Fact]
    public async Task FinalizeAsync_EmptyResults_DoesNotCreateFile()
    {
        var writer = new CsvResultWriter(_tempDir);
        await writer.FinalizeAsync(CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(_tempDir, "summary.csv")));
    }
}
