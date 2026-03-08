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
        var evalSet = TestHelpers.MakeEvalSetConfig("CSharpCoding");
        var pipeline = _factory.Create(evalSet, TestHelpers.MakeConfig());

        Assert.Equal("CSharpCoding", pipeline.PipelineName);
        Assert.Equal(2, pipeline.Stages.Count);
        Assert.Equal("PromptStage", pipeline.Stages[0].StageName);
        Assert.Equal("CSharpEvalSetupStage", pipeline.Stages[1].StageName);
    }

    [Fact]
    public void Create_ScoreWithJudge_True_AddsJudgeStageAsThird()
    {
        var evalSet = TestHelpers.MakeEvalSetConfig("CSharpCoding", new Dictionary<string, object?>
        {
            ["scoreStyleWithJudge"] = true,
        });

        var pipeline = _factory.Create(evalSet, TestHelpers.MakeConfig());

        Assert.Equal(3, pipeline.Stages.Count);
        Assert.Equal("JudgeStage", pipeline.Stages[2].StageName);
    }

    [Fact]
    public void DefaultDataSourceConfig_IsDirectoryKind()
    {
        Assert.Equal(DataSourceKind.Directory, _factory.DefaultDataSourceConfig.Kind);
        Assert.Equal("./data/prompts", _factory.DefaultDataSourceConfig.PromptDirectoryPath);
    }

    [Fact]
    public void Validate_InvalidCompileTimeout_ReturnsError()
    {
        var evalSet = TestHelpers.MakeEvalSetConfig("CSharpCoding", new Dictionary<string, object?>
        {
            ["compileTimeoutSeconds"] = new object(), // wrong type
        });

        var errors = _factory.Validate(evalSet);
        Assert.Contains(errors, static e => e.Field == "pipelineOptions.compileTimeoutSeconds");
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsNoErrors()
    {
        var evalSet = TestHelpers.MakeEvalSetConfig("CSharpCoding", new Dictionary<string, object?>
        {
            ["compileTimeoutSeconds"] = 45.0,
            ["testTimeoutSeconds"] = 90.0,
        });

        var errors = _factory.Validate(evalSet);
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
