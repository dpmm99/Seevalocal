using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Metrics.Models;
using Seevalocal.Metrics.Writers;
using System.Text.Json;
using Xunit;

namespace Seevalocal.Metrics.Tests;

public class JsonResultWriterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public JsonResultWriterTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static EvalResult MakeResult(string id = "001") => new EvalResult
    {
        EvalItemId = id,
        EvalSetId = "test-set",
        Succeeded = true,
        DurationSeconds = 1.24,
        StartedAt = new DateTimeOffset(2025, 3, 1, 12, 0, 0, TimeSpan.Zero),
        RawLlmResponse = "bonjour",
        Metrics =
        [
            new MetricValue { Name = "promptTokenCount", Value = new MetricScalar.IntMetric(42), SourceStage = "PromptStage" },
            new MetricValue { Name = "llmLatencySeconds", Value = new MetricScalar.DoubleMetric(1.24), SourceStage = "PromptStage" },
        ]
    };

    [Fact]
    public async Task WriteResultAsync_CreatesPerEvalFile()
    {
        var writer = new JsonResultWriter(_tempDir);
        var result = MakeResult("001");

        await writer.WriteResultAsync(result, CancellationToken.None);

        var expectedPath = Path.Combine(_tempDir, "evals", "eval-001.json");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task WriteResultAsync_JsonSchema_HasRequiredFields()
    {
        var writer = new JsonResultWriter(_tempDir);
        await writer.WriteResultAsync(MakeResult("002"), CancellationToken.None);

        var filePath = Path.Combine(_tempDir, "evals", "eval-002.json");
        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("002", root.GetProperty("evalItemId").GetString());
        Assert.Equal("test-set", root.GetProperty("evalSetId").GetString());
        Assert.True(root.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1.24, root.GetProperty("durationSeconds").GetDouble(), precision: 5);
        Assert.True(root.TryGetProperty("metrics", out var metricsEl));
        Assert.Equal(2, metricsEl.GetArrayLength());
    }

    [Fact]
    public async Task WriteResultAsync_IncludesRawLlmResponse_WhenEnabled()
    {
        var writer = new JsonResultWriter(_tempDir, includeRawLlmResponse: true);
        await writer.WriteResultAsync(MakeResult("003"), CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, "evals", "eval-003.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("bonjour", doc.RootElement.GetProperty("rawLlmResponse").GetString());
    }

    [Fact]
    public async Task WriteResultAsync_ExcludesRawLlmResponse_WhenDisabled()
    {
        var writer = new JsonResultWriter(_tempDir, includeRawLlmResponse: false);
        await writer.WriteResultAsync(MakeResult("004"), CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, "evals", "eval-004.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("rawLlmResponse", out _));
    }

    [Fact]
    public async Task WriteSummaryAsync_CreatesSummaryJsonFile()
    {
        var writer = new JsonResultWriter(_tempDir);
        var summary = new RunSummary
        {
            EvalSetId = "test-set",
            RunName = "run1",
            TotalItemCount = 5,
            SucceededItemCount = 4,
            FailedItemCount = 1,
            SuccessRatioPercent = 80.0,
            TotalDurationSeconds = 10.0,
            StartedAt = DateTimeOffset.UtcNow
        };

        await writer.WriteSummaryAsync(summary, CancellationToken.None);

        var filePath = Path.Combine(_tempDir, "summary.json");
        Assert.True(File.Exists(filePath));
        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("test-set", doc.RootElement.GetProperty("evalSetId").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("totalItemCount").GetInt32());
    }

    [Fact]
    public async Task WriteResultAsync_MultipleResults_AllFilesCreated()
    {
        var writer = new JsonResultWriter(_tempDir);
        for (var i = 1; i <= 5; i++)
            await writer.WriteResultAsync(MakeResult(i.ToString("D3")), CancellationToken.None);

        for (var i = 1; i <= 5; i++)
            Assert.True(File.Exists(Path.Combine(_tempDir, "evals", $"eval-{i:D3}.json")));
    }
}
