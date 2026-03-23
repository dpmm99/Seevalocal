using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.Judge.Tests;

/// <summary>
/// Tests for judge metric extraction and type handling.
/// </summary>
public class JudgeMetricExtractionTests
{
    private static JudgeResponseParser CreateParser() =>
        new(NullLogger<JudgeResponseParser>.Instance);

    private static JudgeConfig DefaultConfig() =>
        new() { ResponseFormat = JudgeResponseFormat.StructuredJson, ScoreMinValue = 0, ScoreMaxValue = 10 };

    [Fact]
    public void Metrics_ExtractsNumericValues()
    {
        var parser = CreateParser();
        var json = """{"score": 8.5, "count": 42}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics["score"].Should().Be(8.5);
        _ = result.Metrics["count"].Should().Be(42.0);
    }

    [Fact]
    public void Metrics_ExtractsBooleanValues()
    {
        var parser = CreateParser();
        var json = """{"passed": true, "failed": false}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics["passed"].Should().Be(true);
        _ = result.Metrics["failed"].Should().Be(false);
    }

    [Fact]
    public void Metrics_ExtractsStringValues()
    {
        var parser = CreateParser();
        var json = """{"rationale": "Good answer", "category": "quality"}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        // Rationale is extracted separately, not in Metrics
        _ = result.Rationale.Should().Be("Good answer");
        _ = result.Metrics["category"].Should().Be("quality");
    }

    [Fact]
    public void Metrics_HandlesNullValues()
    {
        var parser = CreateParser();
        var json = """{"score": null, "optional": null}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics["score"].Should().BeNull();
        _ = result.Metrics["optional"].Should().BeNull();
    }

    [Fact]
    public void Metrics_HandlesArbitraryFieldNames()
    {
        var parser = CreateParser();
        var json = """{"accuracy": 0.95, "completeness": 0.8, "relevance": 7}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics.Should().ContainKey("accuracy");
        _ = result.Metrics.Should().ContainKey("completeness");
        _ = result.Metrics.Should().ContainKey("relevance");
    }

    [Fact]
    public void Metrics_RationaleIsExtractedCaseInsensitively()
    {
        var parser = CreateParser();

        var json1 = """{"Rationale": "test1", "score": 8}""";
        var result1 = parser.Parse(json1, DefaultConfig());
        _ = result1.Rationale.Should().Be("test1");

        var json2 = """{"RATIONALE": "test2", "score": 9}""";
        var result2 = parser.Parse(json2, DefaultConfig());
        _ = result2.Rationale.Should().Be("test2");

        var json3 = """{"rationale": "test3", "score": 7}""";
        var result3 = parser.Parse(json3, DefaultConfig());
        _ = result3.Rationale.Should().Be("test3");
    }
}
