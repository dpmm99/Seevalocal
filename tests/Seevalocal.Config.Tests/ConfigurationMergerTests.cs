using FluentAssertions;
using Seevalocal.Config.Merging;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.Config.Tests;

public sealed class ConfigurationMergerTests
{
    private readonly ConfigurationMerger _merger = new();

    // -------------------------------------------------------------------------
    // Two-file merge: second file overrides first
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_TwoFiles_LaterFileWins()
    {
        var first = new PartialConfig
        {
            LlamaSettings = new PartialLlamaServerSettings
            {
                ContextWindowTokens = 4096,
                SamplingTemperature = 0.5,
            },
        };

        var second = new PartialConfig
        {
            LlamaSettings = new PartialLlamaServerSettings
            {
                ContextWindowTokens = 8192, // overrides first
                // SamplingTemperature not set → falls back to first
            },
        };

        var result = _merger.Merge([first, second]);

        _ = result.LlamaServer.ContextWindowTokens.Should().Be(8192);
        _ = result.LlamaServer.SamplingTemperature.Should().Be(0.5);
    }

    [Fact]
    public void Merge_TwoFiles_NullsPassThrough()
    {
        var first = new PartialConfig
        {
            LlamaSettings = new PartialLlamaServerSettings { TopK = 40 },
        };
        var second = new PartialConfig
        {
            LlamaSettings = new PartialLlamaServerSettings(), // no TopK
        };

        var result = _merger.Merge([first, second]);

        _ = result.LlamaServer.TopK.Should().Be(40, "second file has no value so first file wins");
    }

    // -------------------------------------------------------------------------
    // CLI override wins over all settings files
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_CliOverride_WinsOverSettingsFiles()
    {
        var file = new PartialConfig
        {
            LlamaSettings = new PartialLlamaServerSettings { ParallelSlotCount = 2 },
        };
        var cli = new PartialConfig
        {
            LlamaSettings = new PartialLlamaServerSettings { ParallelSlotCount = 8 },
        };

        var result = _merger.Merge([file], cli);

        _ = result.LlamaServer.ParallelSlotCount.Should().Be(8);
    }

    [Fact]
    public void Merge_CliOverride_NullCliDoesNotErase()
    {
        var file = new PartialConfig
        {
            LlamaSettings = new PartialLlamaServerSettings { ParallelSlotCount = 4 },
        };
        var cli = new PartialConfig(); // no LlamaServer override

        var result = _merger.Merge([file], cli);

        _ = result.LlamaServer.ParallelSlotCount.Should().Be(4);
    }

    // -------------------------------------------------------------------------
    // RunMeta merging
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_RunMeta_DefaultsApplied_WhenNoFiles()
    {
        var result = _merger.Merge([]);

        _ = result.Run.OutputDirectoryPath.Should().BeNull();
        _ = result.Run.ExportShellTarget.Should().BeNull();
        _ = result.Run.ContinueOnEvalFailure.Should().BeNull();
    }

    [Fact]
    public void Merge_RunMeta_OverrideFromFile()
    {
        var file = new PartialConfig
        {
            Run = new PartialRunMeta
            {
                RunName = "my-run",
                OutputDirectoryPath = "/tmp/results",
                ExportShellTarget = ShellTarget.PowerShell,
                ContinueOnEvalFailure = false,
                MaxConcurrentEvals = 3,
            },
        };

        var result = _merger.Merge([file]);

        _ = result.Run.RunName.Should().Be("my-run");
        _ = result.Run.OutputDirectoryPath.Should().Be("/tmp/results");
        _ = result.Run.ExportShellTarget.Should().Be(ShellTarget.PowerShell);
        _ = result.Run.ContinueOnEvalFailure.Should().BeFalse();
        _ = result.Run.MaxConcurrentEvals.Should().Be(3);
    }

    // -------------------------------------------------------------------------
    // PipelineName comes from highest-priority file that defines it
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_PipelineName_TakenFromLastFile()
    {
        var first = new PartialConfig
        {
            Run = new PartialRunMeta { PipelineName = "PipeA" },
        };
        var second = new PartialConfig
        {
            Run = new PartialRunMeta { PipelineName = "PipeB" },
        };

        var result = _merger.Merge([first, second]);

        _ = result.Run.PipelineName.Should().Be("PipeB");
    }

    [Fact]
    public void Merge_PipelineName_FallsBackToFirstFile_WhenSecondHasNone()
    {
        var first = new PartialConfig { Run = new PartialRunMeta { PipelineName = "PipeA" } };
        var second = new PartialConfig(); // no pipeline name

        var result = _merger.Merge([first, second]);

        _ = result.Run.PipelineName.Should().Be("PipeA");
    }

    // -------------------------------------------------------------------------
    // Server config merging
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_ServerConfig_Defaults()
    {
        var result = _merger.Merge([]);

        _ = result.Server.Manage.Should().BeNull();
    }

    [Fact]
    public void Merge_ServerConfig_Override()
    {
        var file = new PartialConfig
        {
            Server = new PartialServerConfig
            {
                Manage = true,
                BaseUrl = "http://localhost:8080",
                Model = new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = "/models/model.gguf" },
            },
        };

        var result = _merger.Merge([file]);

        _ = result.Server.Manage.Should().BeTrue();
        _ = result.Server.BaseUrl.Should().Be("http://localhost:8080");
        _ = result.Server.Model.Should().NotBeNull();
        _ = result.Server.Model!.FilePath.Should().Be("/models/model.gguf");
    }

    // -------------------------------------------------------------------------
    // Judge config
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_JudgeConfig_NullByDefault()
    {
        var result = _merger.Merge([]);
        _ = result.Judge.Should().BeNull();
    }

    [Fact]
    public void Merge_JudgeConfig_TakenFromLastFileWithJudge()
    {
        var first = new PartialConfig
        {
            Judge = new PartialJudgeConfig { ServerConfig = new PartialServerConfig { BaseUrl = "http://judge1:8080" } },
        };
        var second = new PartialConfig
        {
            Judge = new PartialJudgeConfig { ServerConfig = new PartialServerConfig { BaseUrl = "http://judge2:8080" } },
        };

        var result = _merger.Merge([first, second]);

        _ = result.Judge!.ServerConfig.BaseUrl.Should().Be("http://judge2:8080");
    }

    // -------------------------------------------------------------------------
    // Empty list produces defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_EmptyList_ProducesAllDefaults()
    {
        var result = _merger.Merge([]);

        _ = result.LlamaServer.ContextWindowTokens.Should().BeNull();
        _ = result.LlamaServer.SamplingTemperature.Should().BeNull();
        _ = result.LlamaServer.ExtraArgs.Should().BeEmpty();
        _ = result.Judge.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // ExtraArgs taken from highest-priority non-empty list
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_LlamaExtraArgs_TakenFromHighestPriorityNonEmptyList()
    {
        var first = new PartialConfig
        {
            LlamaSettings = new PartialLlamaServerSettings { ExtraArgs = ["--arg-from-first"] },
        };
        var second = new PartialConfig
        {
            LlamaSettings = new PartialLlamaServerSettings { ExtraArgs = ["--arg-from-second"] },
        };

        var result = _merger.Merge([first, second]);

        _ = result.LlamaServer.ExtraArgs.Should().ContainSingle().Which.Should().Be("--arg-from-second");
    }

    // -------------------------------------------------------------------------
    // DataSource config merging
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_DataSource_NullByDefault()
    {
        var result = _merger.Merge([]);
        _ = result.DataSource.Kind.Should().Be(DataSourceKind.SplitDirectories);
        _ = result.DataSource.FilePath.Should().BeNull();
    }

    [Fact]
    public void Merge_DataSource_TakenFromLastFileWithDataSource()
    {
        var first = new PartialConfig
        {
            DataSource = new PartialDataSourceConfig
            {
                Kind = DataSourceKind.SingleFile,
                FilePath = "/path/to/first.json",
            },
        };
        var second = new PartialConfig
        {
            DataSource = new PartialDataSourceConfig
            {
                Kind = DataSourceKind.JsonlFile,
                FilePath = "/path/to/second.jsonl",
            },
        };

        var result = _merger.Merge([first, second]);

        _ = result.DataSource.Kind.Should().Be(DataSourceKind.JsonlFile);
        _ = result.DataSource.FilePath.Should().Be("/path/to/second.jsonl");
    }

    [Fact]
    public void Merge_DataSource_FallsBackToFirstFile_WhenSecondHasNone()
    {
        var first = new PartialConfig
        {
            DataSource = new PartialDataSourceConfig
            {
                Kind = DataSourceKind.SplitDirectories,
                PromptDirectory = "/prompts",
                ExpectedDirectory = "/expected",
            },
        };
        var second = new PartialConfig(); // no DataSource

        var result = _merger.Merge([first, second]);

        _ = result.DataSource.Kind.Should().Be(DataSourceKind.SplitDirectories);
        _ = result.DataSource.PromptDirectory.Should().Be("/prompts");
        _ = result.DataSource.ExpectedDirectory.Should().Be("/expected");
    }

    // -------------------------------------------------------------------------
    // Judge ServerSettings merging
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_JudgeServerSettings_AllFieldsMerged()
    {
        var first = new PartialConfig
        {
            Judge = new PartialJudgeConfig
            {
                Enable = true,
                ServerSettings = new PartialLlamaServerSettings
                {
                    ContextWindowTokens = 4096,
                    GpuLayerCount = 35,
                    SamplingTemperature = 0.5,
                },
            },
        };
        var second = new PartialConfig
        {
            Judge = new PartialJudgeConfig
            {
                ServerSettings = new PartialLlamaServerSettings
                {
                    ContextWindowTokens = 8192, // overrides first
                    // GpuLayerCount and SamplingTemperature not set → falls back to first
                },
            },
        };

        var result = _merger.Merge([first, second]);

        _ = result.Judge.Should().NotBeNull();
        _ = result.Judge!.Enable.Should().BeTrue();
        _ = result.Judge.ServerSettings!.ContextWindowTokens.Should().Be(8192);
        _ = result.Judge.ServerSettings.GpuLayerCount.Should().Be(35);
        _ = result.Judge.ServerSettings.SamplingTemperature.Should().Be(0.5);
    }

    [Fact]
    public void Merge_JudgeServerConfig_AllFieldsMerged()
    {
        var first = new PartialConfig
        {
            Judge = new PartialJudgeConfig
            {
                Enable = true,
                ServerConfig = new PartialServerConfig
                {
                    BaseUrl = "http://localhost:8080",
                    ApiKey = "key1",
                    Model = new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = "/judge1.gguf" },
                },
            },
        };
        var second = new PartialConfig
        {
            Judge = new PartialJudgeConfig
            {
                ServerConfig = new PartialServerConfig
                {
                    BaseUrl = "http://127.0.0.1:9090", // overrides first
                    // ApiKey and Model not set → falls back to first
                },
            },
        };

        var result = _merger.Merge([first, second]);

        _ = result.Judge!.ServerConfig!.BaseUrl.Should().Be("http://127.0.0.1:9090");
        _ = result.Judge.ServerConfig.ApiKey.Should().Be("key1");
        _ = result.Judge.ServerConfig.Model!.FilePath.Should().Be("/judge1.gguf");
    }
}
