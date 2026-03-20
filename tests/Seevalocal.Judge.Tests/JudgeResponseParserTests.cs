using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Judge;
using Xunit;

namespace Seevalocal.Judge.Tests;

public class JudgeResponseParserTests
{
    private static JudgeResponseParser CreateParser() =>
        new(NullLogger<JudgeResponseParser>.Instance);

    private static JudgeConfig DefaultConfig() =>
        new() { ResponseFormat = JudgeResponseFormat.StructuredJson, ScoreMinValue = 0, ScoreMaxValue = 10 };

    // ──────────────────────────────────────────────────────────────────────
    // Basic JSON Parsing
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidJson_ExtractsAllFieldsAsMetrics()
    {
        var parser = CreateParser();
        var json = """{"score": 8.5, "passed": true, "rationale": "Good answer"}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics.Should().ContainKey("score");
        _ = result.Metrics["score"].Should().Be(8.5);
        _ = result.Metrics.Should().ContainKey("passed");
        _ = result.Metrics["passed"].Should().Be(true);
        _ = result.Rationale.Should().Be("Good answer");
    }

    [Fact]
    public void Parse_StripsMarkdownFences()
    {
        var parser = CreateParser();
        var json = "```json\n{\"score\": 5, \"rationale\": \"ok\"}\n```";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics.Should().ContainKey("score");
        _ = result.Metrics["score"].Should().Be(5.0);
        _ = result.Rationale.Should().Be("ok");
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsFailed()
    {
        var parser = CreateParser();
        var result = parser.Parse("This is not JSON at all!", DefaultConfig());

        _ = result.ParseSucceeded.Should().BeFalse();
        _ = result.Metrics.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NotAnObject_ReturnsFailed()
    {
        var parser = CreateParser();
        var result = parser.Parse("[1, 2, 3]", DefaultConfig());

        _ = result.ParseSucceeded.Should().BeFalse();
    }

    [Fact]
    public void Parse_CaseInsensitiveRationaleField()
    {
        var parser = CreateParser();
        var json = """{"Rationale": "test explanation", "score": 6}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        // Rationale is extracted case-insensitively to the Rationale property
        _ = result.Rationale.Should().Be("test explanation");
        // Rationale is NOT in Metrics (it's extracted separately)
        _ = result.Metrics.Should().ContainKey("score");
    }

    [Fact]
    public void Parse_NullValues_AreHandled()
    {
        var parser = CreateParser();
        var json = """{"score": null, "rationale": "test"}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics.Should().ContainKey("score");
        _ = result.Metrics["score"].Should().BeNull();
    }

    [Fact]
    public void Parse_IntegerValues_AreHandled()
    {
        var parser = CreateParser();
        var json = """{"score": 8, "count": 42}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics["score"].Should().Be(8.0);
        _ = result.Metrics["count"].Should().Be(42.0);
    }

    [Fact]
    public void Parse_BooleanValues_AreHandled()
    {
        var parser = CreateParser();
        var json = """{"passed": true, "failed": false}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics["passed"].Should().Be(true);
        _ = result.Metrics["failed"].Should().Be(false);
    }

    [Fact]
    public void Parse_EmptyObject_ReturnsSuccessWithNoMetrics()
    {
        var parser = CreateParser();
        var json = """{}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics.Should().BeEmpty();
        _ = result.Rationale.Should().BeNull();
    }

    [Fact]
    public void Parse_NestedObjects_AreSerializedAsJson()
    {
        var parser = CreateParser();
        var json = """{"score": 8, "details": {"accuracy": 0.95, "completeness": 0.8}}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics.Should().ContainKey("details");
        // JSON serialization may have different whitespace, so check structure
        var detailsJson = result.Metrics["details"]!.ToString()!;
        _ = detailsJson.Should().Contain("accuracy").And.Contain("0.95");
        _ = detailsJson.Should().Contain("completeness").And.Contain("0.8");
    }

    [Fact]
    public void Parse_Arrays_AreSerializedAsJson()
    {
        var parser = CreateParser();
        var json = """{"tags": ["good", "accurate"], "score": 9}""";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Metrics.Should().ContainKey("tags");
        // JSON serialization may have different whitespace, so check structure
        var tagsJson = result.Metrics["tags"]!.ToString()!;
        _ = tagsJson.Should().Contain("good").And.Contain("accurate");
    }

    [Fact]
    public void Parse_RawResponse_ContainsStrippedJson()
    {
        var parser = CreateParser();
        var json = "```json\n{\"score\": 7}\n```";
        var result = parser.Parse(json, DefaultConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.RawResponse.Should().Be("""{"score": 7}""");
    }
}
