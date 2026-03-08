using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.Judge.Tests;

public class JudgeNormalizationTests
{
    private static JudgeResponseParser CreateParser() =>
        new(NullLogger<JudgeResponseParser>.Instance);

    [Theory]
    [InlineData(0, 0, 10, 0.0)]
    [InlineData(5, 0, 10, 0.5)]
    [InlineData(10, 0, 10, 1.0)]
    [InlineData(1, 1, 5, 0.0)]   // min edge
    [InlineData(5, 1, 5, 1.0)]   // max edge
    [InlineData(3, 1, 5, 0.5)]   // midpoint
    public void Numeric_NormalizesCorrectly(double raw, double min, double max, double expected)
    {
        var parser = CreateParser();
        var config = new JudgeConfig
        {
            ResponseFormat = JudgeResponseFormat.NumericScore,
            ScoreMinValue = min,
            ScoreMaxValue = max,
        };
        var result = parser.Parse(raw.ToString(System.Globalization.CultureInfo.InvariantCulture), config);

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData(15, 0, 10, 1.0)]   // above max → clamp to 1
    [InlineData(-3, 0, 10, 0.0)]   // below min → clamp to 0
    public void Numeric_ClampsOutOfRangeValues(double raw, double min, double max, double expected)
    {
        var parser = CreateParser();
        var config = new JudgeConfig
        {
            ResponseFormat = JudgeResponseFormat.NumericScore,
            ScoreMinValue = min,
            ScoreMaxValue = max,
        };
        var result = parser.Parse(raw.ToString(System.Globalization.CultureInfo.InvariantCulture), config);

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().Be(expected);
    }

    [Fact]
    public void Numeric_MinEqualsMax_ReturnsZero()
    {
        // Degenerate config (min == max) should not throw.
        var parser = CreateParser();
        var config = new JudgeConfig
        {
            ResponseFormat = JudgeResponseFormat.NumericScore,
            ScoreMinValue = 5,
            ScoreMaxValue = 5,
        };
        var result = parser.Parse("5", config);

        _ = result.ParseSucceeded.Should().BeTrue();
        _ = result.NormalizedScore.Should().Be(0.0);
    }

    [Fact]
    public void Json_NormalizesWithCustomRange()
    {
        var parser = CreateParser();
        var config = new JudgeConfig
        {
            ResponseFormat = JudgeResponseFormat.StructuredJson,
            ScoreMinValue = 1,
            ScoreMaxValue = 5,
        };
        var json = """{"rationale":"ok","score":3,"passed":true}""";
        var result = parser.Parse(json, config);

        _ = result.ParseSucceeded.Should().BeTrue();
        // (3 - 1) / (5 - 1) = 0.5
        _ = result.NormalizedScore.Should().BeApproximately(0.5, 1e-9);
    }
}
