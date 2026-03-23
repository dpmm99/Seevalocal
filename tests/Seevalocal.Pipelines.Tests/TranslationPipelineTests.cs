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
        var config = TestHelpers.MakeConfigWithPipeline("Translation");
        var pipeline = _factory.Create(config);

        Assert.Equal("Translation", pipeline.PipelineName);
        Assert.Equal(2, pipeline.Stages.Count);
        Assert.Equal("PromptStage", pipeline.Stages[0].StageName);
        Assert.Equal("JudgeStage", pipeline.Stages[1].StageName);
    }

    [Fact]
    public void Create_StagesAreInCorrectOrder_PromptBeforeJudge()
    {
        var config = TestHelpers.MakeConfigWithPipeline("Translation");
        var pipeline = _factory.Create(config);

        Assert.Equal("PromptStage", pipeline.Stages[0].StageName);
        Assert.Equal("JudgeStage", pipeline.Stages[1].StageName);
    }

    [Fact]
    public void DefaultDataSourceConfig_IsSplitDirectories()
    {
        Assert.Equal(DataSourceKind.SplitDirectories, _factory.DefaultDataSourceConfig.Kind);
        Assert.Equal("./data/source", _factory.DefaultDataSourceConfig.PromptDirectory);
        Assert.Equal("./data/reference", _factory.DefaultDataSourceConfig.ExpectedDirectory);
        Assert.Equal("*.txt", _factory.DefaultDataSourceConfig.FilePattern);
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsNoErrors()
    {
        var config = TestHelpers.MakeConfigWithPipeline("Translation", new Dictionary<string, object?>
        {
            ["sourceLanguage"] = "English",
            ["targetLanguage"] = "Spanish",
        });

        var errors = _factory.Validate(config);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NullOptions_ReturnsNoErrors()
    {
        var config = TestHelpers.MakeConfigWithPipeline("Translation", opts: null);
        var errors = _factory.Validate(config);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidTypeForSourceLanguage_ReturnsError()
    {
        var config = TestHelpers.MakeConfigWithPipeline("Translation", new Dictionary<string, object?>
        {
            ["sourceLanguage"] = 42, // wrong type
        });

        var errors = _factory.Validate(config);
        Assert.Contains(errors, static e => e.Field == "pipelineOptions.sourceLanguage");
    }
}
