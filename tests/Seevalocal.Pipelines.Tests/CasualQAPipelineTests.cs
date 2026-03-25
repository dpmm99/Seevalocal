using Seevalocal.Core.Models;
using Seevalocal.Pipelines.Factories;
using Xunit;

namespace Seevalocal.Pipelines.Tests;

public sealed class CasualQAPipelineTests
{
    private readonly CasualQAPipelineFactory _factory = new(TestHelpers.LoggerFactory);

    [Fact]
    public void Create_ExactMatchDisabled_ProducesTwoStages_PromptAndJudge()
    {
        var config = TestHelpers.MakeConfigWithPipeline("CasualQA", new Dictionary<string, object?>
        {
            ["enableExactMatch"] = false,
        });

        var pipeline = _factory.Create(config);

        Assert.Equal("CasualQA", pipeline.PipelineName);
        Assert.Equal(3, pipeline.Stages.Count);
        Assert.Equal("PromptStage", pipeline.Stages[1].StageName);
        Assert.Equal("JudgeStage", pipeline.Stages[2].StageName);
    }

    [Fact]
    public void Create_ExactMatchEnabled_ProducesThreeStages()
    {
        var config = TestHelpers.MakeConfigWithPipeline("CasualQA", new Dictionary<string, object?>
        {
            ["enableExactMatch"] = true,
        });

        var pipeline = _factory.Create(config);

        Assert.Equal(4, pipeline.Stages.Count);
        Assert.Equal("ItemLoadStage", pipeline.Stages[0].StageName);
        Assert.Equal("PromptStage", pipeline.Stages[1].StageName);
        Assert.Equal("ExactMatchStage", pipeline.Stages[2].StageName);
        Assert.Equal("JudgeStage", pipeline.Stages[3].StageName);
    }

    [Fact]
    public void Create_DefaultOptions_ExactMatchSkippedByDefault()
    {
        var config = TestHelpers.MakeConfigWithPipeline("CasualQA");
        var pipeline = _factory.Create(config);

        // ExactMatchStage must not be present
        Assert.DoesNotContain(pipeline.Stages, static s => s.StageName == "ExactMatchStage");
    }

    [Fact]
    public void DefaultDataSourceConfig_IsJsonFile()
    {
        var ds = _factory.DefaultDataSourceConfig;
        Assert.Equal(DataSourceKind.JsonFile, ds.Kind);
        Assert.Equal("./data/qa.json", ds.FilePath);
        Assert.Equal("id", ds.FieldMapping?.IdField);
        Assert.Equal("question", ds.FieldMapping?.UserPromptField);
        Assert.Equal("answer", ds.FieldMapping?.ExpectedOutputField);
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsNoErrors()
    {
        var config = TestHelpers.MakeConfigWithPipeline("CasualQA", new Dictionary<string, object?>
        {
            ["judgeMaxScore"] = 10,
            ["judgePassThresholdRatio"] = 0.7,
        });

        var errors = _factory.Validate(config);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidJudgeMaxScore_ReturnsError()
    {
        var config = TestHelpers.MakeConfigWithPipeline("CasualQA", new Dictionary<string, object?>
        {
            ["judgeMaxScore"] = new object(), // wrong type
        });

        var errors = _factory.Validate(config);
        Assert.Contains(errors, static e => e.Field == "pipelineOptions.judgeMaxScore");
    }
}
