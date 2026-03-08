using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.Judge.Tests;

public class JudgeResponseParserTests
{
    private static JudgeResponseParser CreateParser() =>
        new(NullLogger<JudgeResponseParser>.Instance);

    private static JudgeConfig NumericConfig(double min = 0, double max = 10) =>
        new() { ResponseFormat = JudgeResponseFormat.NumericScore, ScoreMinValue = min, ScoreMaxValue = max };

    private static JudgeConfig PassFailConfig() =>
        new() { ResponseFormat = JudgeResponseFormat.PassFail, ScoreMinValue = 0, ScoreMaxValue = 10 };

    private static JudgeConfig JsonConfig(double min = 0, double max = 10) =>
        new() { ResponseFormat = JudgeResponseFormat.StructuredJson, ScoreMinValue = min, ScoreMaxValue = max };

    // ──────────────────────────────────────────────────────────────────────
    // NumericScore
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("7", 7.0)]
    [InlineData("7.5", 7.5)]
    [InlineData("10", 10.0)]
    [InlineData("0", 0.0)]
    public void NumericScore_ExtractsInteger_OrFloat(string rawText, double expectedRaw)
    {
        var parser = CreateParser();
        var result = parser.Parse(rawText, NumericConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().BeApproximately(expectedRaw / 10.0, 1e-9);
    }

    [Fact]
    public void NumericScore_ExtractsFromSurroundingText()
    {
        var parser = CreateParser();
        var result = parser.Parse("Score: 7.5/10", NumericConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public void NumericScore_NoNumberInResponse_ReturnsFailed()
    {
        var parser = CreateParser();
        var result = parser.Parse("The answer is great!", NumericConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeFalse();
    }

    [Fact]
    public void NumericScore_AboveMax_ClampsTo1()
    {
        var parser = CreateParser();
        var result = parser.Parse("12", NumericConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().Be(1.0);
    }

    [Fact]
    public void NumericScore_BelowMin_ClampsTo0()
    {
        var parser = CreateParser();
        var result = parser.Parse("-5", NumericConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().Be(0.0);
    }

    // ──────────────────────────────────────────────────────────────────────
    // PassFail
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("PASS", true, 1.0)]
    [InlineData("FAIL", false, 0.0)]
    [InlineData("pass", true, 1.0)]
    [InlineData("fail", false, 0.0)]
    [InlineData("Pass", true, 1.0)]
    [InlineData("The result is PASS overall.", true, 1.0)]
    public void PassFail_ParsesVerdictCorrectly(string rawText, bool expectedPassed, double expectedScore)
    {
        var parser = CreateParser();
        var result = parser.Parse(rawText, PassFailConfig());

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Passed.Should().Be(expectedPassed);
        _ = result.NormalizedScore.Should().BeApproximately(expectedScore, 1e-9);
    }

    [Fact]
    public void PassFail_NeitherKeyword_ReturnsFailed()
    {
        var parser = CreateParser();
        var result = parser.Parse("The answer is mostly correct.", PassFailConfig());

        _ = result.ParseSucceeded.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // StructuredJson
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void StructuredJson_ValidPayload_ParsesAllFields()
    {
        var parser = CreateParser();
        var json = """{"rationale":"Looks good","score":8,"passed":true}""";
        var result = parser.Parse(json, JsonConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().BeApproximately(0.8, 1e-9);
        _ = result.Passed.Should().BeTrue();
        _ = result.Rationale.Should().Be("Looks good");
    }

    [Fact]
    public void StructuredJson_StripsMarkdownFences()
    {
        var parser = CreateParser();
        var json = "```json\n{\"rationale\":\"ok\",\"score\":5,\"passed\":false}\n```";
        var result = parser.Parse(json, JsonConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().BeApproximately(0.5, 1e-9);
        _ = result.Passed.Should().BeFalse();
    }

    [Fact]
    public void StructuredJson_Malformed_ReturnsFailed()
    {
        var parser = CreateParser();
        var result = parser.Parse("This is not JSON at all!", JsonConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeFalse();
    }

    [Fact]
    public void StructuredJson_MissingScoreField_ReturnsFailed()
    {
        var parser = CreateParser();
        var result = parser.Parse("""{"rationale":"ok","passed":true}""", JsonConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeFalse();
    }

    [Fact]
    public void StructuredJson_PassedInferredFromScore_WhenMissing()
    {
        var parser = CreateParser();
        // score=8 → normalized 0.8 → >= 0.5 → passed=true
        var result = parser.Parse("""{"rationale":"good","score":8}""", JsonConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.Passed.Should().BeTrue();
    }

    [Fact]
    public void StructuredJson_CaseInsensitiveFields()
    {
        var parser = CreateParser();
        var json = """{"Rationale":"test","Score":6,"Passed":false}""";
        var result = parser.Parse(json, JsonConfig(0, 10));

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().BeApproximately(0.6, 1e-9);
    }
}
