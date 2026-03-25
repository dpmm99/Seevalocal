using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline.Stages;
using Seevalocal.Pipelines.Factories;
using Xunit;

namespace Seevalocal.Pipelines.Tests;

public sealed class CSharpCodingPipelineTests
{
    private readonly CSharpCodingPipelineFactory _factory = new(TestHelpers.LoggerFactory);

    [Fact]
    public void Create_DefaultOptions_ProducesTwoStages_PromptAndSetup()
    {
        var config = TestHelpers.MakeConfigWithPipeline("CSharpCoding");
        var pipeline = _factory.Create(config);

        Assert.Equal("CSharpCoding", pipeline.PipelineName);
        Assert.Equal(3, pipeline.Stages.Count);
        Assert.Equal("PromptStage", pipeline.Stages[1].StageName);
        Assert.Equal("CSharpEvalSetupStage", pipeline.Stages[2].StageName);
    }

    [Fact]
    public void Create_ScoreWithJudge_True_AddsJudgeStageAsThird()
    {
        var config = TestHelpers.MakeConfigWithPipeline("CSharpCoding", new Dictionary<string, object?>
        {
            ["scoreStyleWithJudge"] = true,
        });

        var pipeline = _factory.Create(config);

        Assert.Equal(4, pipeline.Stages.Count);
        Assert.Equal("JudgeStage", pipeline.Stages[3].StageName);
    }

    [Fact]
    public void DefaultDataSourceConfig_IsDirectoryKind()
    {
        Assert.Equal(DataSourceKind.SplitDirectories, _factory.DefaultDataSourceConfig.Kind);
        Assert.Equal("./data/prompts", _factory.DefaultDataSourceConfig.PromptDirectory);
    }

    [Fact]
    public void Validate_InvalidCompileTimeout_ReturnsError()
    {
        var config = TestHelpers.MakeConfigWithPipeline("CSharpCoding", new Dictionary<string, object?>
        {
            ["compileTimeoutSeconds"] = new object(), // wrong type
        });

        var errors = _factory.Validate(config);
        Assert.Contains(errors, static e => e.Field == "pipelineOptions.compileTimeoutSeconds");
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsNoErrors()
    {
        var config = TestHelpers.MakeConfigWithPipeline("CSharpCoding", new Dictionary<string, object?>
        {
            ["compileTimeoutSeconds"] = 45.0,
            ["testTimeoutSeconds"] = 90.0,
        });

        var errors = _factory.Validate(config);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("```csharp\nvar x = 1;\n```", "var x = 1;")]
    [InlineData("```\nreturn true;\n```", "return true;")]
    [InlineData("var x = 1;", "var x = 1;")]
    [InlineData("```cs\nint n = 0;\n// comment\n```", "int n = 0;\n// comment")]
    public void FileWriterStage_StripMarkdownFences_StripsCorrectly(string input, string expected)
    {
        var result = FileWriterStage.StripMarkdownFences(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FileWriterStage_NoFences_ReturnsInputTrimmed()
    {
        const string input = "  var x = 1;  ";
        Assert.Equal("var x = 1;", FileWriterStage.StripMarkdownFences(input));
    }
}
