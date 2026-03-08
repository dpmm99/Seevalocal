using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Config.Validation;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.Config.Tests;

public sealed class ConfigValidatorTests
{
    private static ConfigValidator MakeValidator(IReadOnlySet<string>? pipelines = null) =>
        new(NullLogger<ConfigValidator>.Instance, pipelines);

    private static ResolvedConfig ValidConfig(string outputDir = "/tmp/seevalocal-test-output") =>
        new()
        {
            Run = new RunMeta { OutputDirectoryPath = outputDir },
            Server = new ServerConfig { Manage = false, BaseUrl = "http://127.0.0.1:8080" },
            EvalSets =
            [
                new EvalSetConfig
                {
                    Id = "set1",
                    PipelineName = "TestPipeline",
                    DataSource = new DataSourceConfig
                    {
                        Kind = DataSourceKind.Directory,
                        PromptDirectoryPath = "/tmp/prompts",
                    },
                },
            ],
        };

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_ValidConfig_ReturnsNoErrors()
    {
        var errors = MakeValidator().Validate(ValidConfig());
        _ = errors.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Server validation
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_Manage_True_ModelMissing_ReturnsError()
    {
        var config = ValidConfig() with
        {
            Server = new ServerConfig { Manage = true, Model = null },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().ContainSingle(static e => e.Field == "server.model");
    }

    [Fact]
    public void Validate_Manage_True_ModelPresent_NoError()
    {
        var config = ValidConfig() with
        {
            Server = new ServerConfig
            {
                Manage = true,
                Model = new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = "/model.gguf" },
            },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().NotContain(static e => e.Field == "server.model");
    }

    [Fact]
    public void Validate_Manage_False_BaseUrlMissing_ReturnsError()
    {
        var config = ValidConfig() with
        {
            Server = new ServerConfig { Manage = false, BaseUrl = null },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().ContainSingle(static e => e.Field == "server.baseUrl");
    }

    [Fact]
    public void Validate_Manage_False_BaseUrlInvalid_ReturnsError()
    {
        var config = ValidConfig() with
        {
            Server = new ServerConfig { Manage = false, BaseUrl = "not-a-uri" },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().ContainSingle(static e => e.Field == "server.baseUrl");
    }

    // -------------------------------------------------------------------------
    // LlamaServer validation
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_ContextWindowTokens_Zero_ReturnsError()
    {
        var config = ValidConfig() with
        {
            LlamaServer = new LlamaServerSettings { ContextWindowTokens = 0 },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().ContainSingle(static e => e.Field == "llamaServer.contextWindowTokens");
    }

    [Fact]
    public void Validate_ContextWindowTokens_Positive_NoError()
    {
        var config = ValidConfig() with
        {
            LlamaServer = new LlamaServerSettings { ContextWindowTokens = 2048 },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().NotContain(static e => e.Field == "llamaServer.contextWindowTokens");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    [InlineData(3.0)]
    public void Validate_SamplingTemperature_OutOfRange_ReturnsError(double temperature)
    {
        var config = ValidConfig() with
        {
            LlamaServer = new LlamaServerSettings { SamplingTemperature = temperature },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().ContainSingle(static e => e.Field == "llamaServer.samplingTemperature");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Validate_SamplingTemperature_InRange_NoError(double temperature)
    {
        var config = ValidConfig() with
        {
            LlamaServer = new LlamaServerSettings { SamplingTemperature = temperature },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().NotContain(static e => e.Field == "llamaServer.samplingTemperature");
    }

    // -------------------------------------------------------------------------
    // EvalSet validation
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_EvalSets_Empty_ReturnsError()
    {
        var config = ValidConfig() with { EvalSets = [] };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().ContainSingle(static e => e.Field == "evalSets");
    }

    [Fact]
    public void Validate_EvalSets_DuplicateId_ReturnsError()
    {
        var config = ValidConfig() with
        {
            EvalSets =
            [
                new EvalSetConfig
                {
                    Id = "dup",
                    PipelineName = "PipeA",
                    DataSource = new DataSourceConfig { Kind = DataSourceKind.Directory, PromptDirectoryPath = "/p" },
                },
                new EvalSetConfig
                {
                    Id = "dup",
                    PipelineName = "PipeB",
                    DataSource = new DataSourceConfig { Kind = DataSourceKind.Directory, PromptDirectoryPath = "/p" },
                },
            ],
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().Contain(static e => e.Field.Contains("id") && e.MessageText.Contains("not unique"));
    }

    [Fact]
    public void Validate_EvalSets_EmptyId_ReturnsError()
    {
        var config = ValidConfig() with
        {
            EvalSets =
            [
                new EvalSetConfig
                {
                    Id = "",
                    PipelineName = "PipeA",
                    DataSource = new DataSourceConfig { Kind = DataSourceKind.Directory, PromptDirectoryPath = "/p" },
                },
            ],
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().Contain(static e => e.Field.Contains("id"));
    }

    [Fact]
    public void Validate_EvalSets_UnknownPipeline_ReturnsError()
    {
        HashSet<string> registered = ["KnownPipeline"];
        var config = ValidConfig() with
        {
            EvalSets =
            [
                new EvalSetConfig
                {
                    Id = "set1",
                    PipelineName = "UnknownPipeline",
                    DataSource = new DataSourceConfig { Kind = DataSourceKind.Directory, PromptDirectoryPath = "/p" },
                },
            ],
        };

        var errors = MakeValidator(registered).Validate(config);

        _ = errors.Should().Contain(static e => e.Field.Contains("pipelineName"));
    }

    [Fact]
    public void Validate_EvalSets_KnownPipeline_NoError()
    {
        HashSet<string> registered = ["TestPipeline"];
        var errors = MakeValidator(registered).Validate(ValidConfig());

        _ = errors.Should().NotContain(static e => e.Field.Contains("pipelineName"));
    }

    [Fact]
    public void Validate_EvalSets_DirectoryKind_MissingPath_ReturnsError()
    {
        var config = ValidConfig() with
        {
            EvalSets =
            [
                new EvalSetConfig
                {
                    Id = "set1",
                    PipelineName = "P",
                    DataSource = new DataSourceConfig { Kind = DataSourceKind.Directory, PromptDirectoryPath = null },
                },
            ],
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().Contain(static e => e.Field.Contains("promptDirectoryPath"));
    }

    [Theory]
    [InlineData(DataSourceKind.JsonFile)]
    [InlineData(DataSourceKind.YamlFile)]
    [InlineData(DataSourceKind.CsvFile)]
    [InlineData(DataSourceKind.ParquetFile)]
    public void Validate_EvalSets_FileKind_MissingFilePath_ReturnsError(DataSourceKind kind)
    {
        var config = ValidConfig() with
        {
            EvalSets =
            [
                new EvalSetConfig
                {
                    Id = "set1",
                    PipelineName = "P",
                    DataSource = new DataSourceConfig { Kind = kind, FilePath = null },
                },
            ],
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().Contain(static e => e.Field.Contains("filePath"));
    }

    // -------------------------------------------------------------------------
    // Judge validation
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_Judge_Null_NoError()
    {
        var config = ValidConfig() with { Judge = null };
        var errors = MakeValidator().Validate(config);
        _ = errors.Should().NotContain(static e => e.Field.StartsWith("judge"));
    }

    [Fact]
    public void Validate_Judge_InvalidUrl_ReturnsError()
    {
        var config = ValidConfig() with
        {
            Judge = new JudgeConfig { ServerConfig = new ServerConfig { BaseUrl = "not-a-url" } },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().Contain(static e => e.Field == "judge.baseUrl");
    }

    [Fact]
    public void Validate_Judge_ValidUrl_NoError()
    {
        var config = ValidConfig() with
        {
            Judge = new JudgeConfig { BaseUrl = "http://judge:8080" },
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().NotContain(static e => e.Field == "judge.baseUrl");
    }

    // -------------------------------------------------------------------------
    // Multiple errors collected
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_MultipleErrors_AllCollected()
    {
        var config = new ResolvedConfig
        {
            Run = new RunMeta { OutputDirectoryPath = "/tmp/seevalocal-test-output" },
            Server = new ServerConfig { Manage = true, Model = null },   // error: model missing
            LlamaServer = new LlamaServerSettings { ContextWindowTokens = -1 }, // error: bad value
            EvalSets = [],  // error: empty
        };

        var errors = MakeValidator().Validate(config);

        _ = errors.Should().HaveCountGreaterThanOrEqualTo(3);
    }
}
