using Seevalocal.Core.Models;
using Seevalocal.Pipelines.Factories;
using Xunit;

namespace Seevalocal.Pipelines.Tests;

public sealed class TranslationPipelineTests
{
    private readonly TranslationPipelineFactory _factory = new(TestHelpers.LoggerFactory);

    [Fact]
    public void Create_DefaultOptions_ProducesTwoStages_PromptAndJudge()
    {
        var evalSet = TestHelpers.MakeEvalSetConfig("Translation");
        var config = TestHelpers.MakeConfig();

        var pipeline = _factory.Create(evalSet, config);

        Assert.Equal("Translation", pipeline.PipelineName);
        Assert.Equal(2, pipeline.Stages.Count);
        Assert.Equal("PromptStage", pipeline.Stages[0].StageName);
        Assert.Equal("JudgeStage", pipeline.Stages[1].StageName);
    }

    [Fact]
    public void Create_StagesAreInCorrectOrder_PromptBeforeJudge()
    {
        var pipeline = _factory.Create(
            TestHelpers.MakeEvalSetConfig("Translation"),
            TestHelpers.MakeConfig());

        Assert.Equal("PromptStage", pipeline.Stages[0].StageName);
        Assert.Equal("JudgeStage", pipeline.Stages[1].StageName);
    }

    [Fact]
    public void DefaultDataSourceConfig_IsSplitDirectories()
    {
        Assert.Equal(DataSourceKind.SplitDirectories, _factory.DefaultDataSourceConfig.Kind);
        Assert.Equal("./data/source", _factory.DefaultDataSourceConfig.PromptDirectoryPath);
        Assert.Equal("./data/reference", _factory.DefaultDataSourceConfig.ExpectedOutputDirectoryPath);
        Assert.Equal("*.txt", _factory.DefaultDataSourceConfig.FilePattern);
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsNoErrors()
    {
        var evalSet = TestHelpers.MakeEvalSetConfig("Translation", new Dictionary<string, object?>
        {
            ["sourceLanguage"] = "English",
            ["targetLanguage"] = "Spanish",
        });

        var errors = _factory.Validate(evalSet);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NullOptions_ReturnsNoErrors()
    {
        var evalSet = TestHelpers.MakeEvalSetConfig("Translation", opts: null);
        var errors = _factory.Validate(evalSet);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidTypeForSourceLanguage_ReturnsError()
    {
        var evalSet = TestHelpers.MakeEvalSetConfig("Translation", new Dictionary<string, object?>
        {
            ["sourceLanguage"] = 42, // wrong type
        });

        var errors = _factory.Validate(evalSet);
        Assert.Contains(errors, static e => e.Field == "pipelineOptions.sourceLanguage");
    }
}
