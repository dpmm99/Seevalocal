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
            LlamaServer = new PartialLlamaServerSettings
            {
                ContextWindowTokens = 4096,
                SamplingTemperature = 0.5,
            },
        };

        var second = new PartialConfig
        {
            LlamaServer = new PartialLlamaServerSettings
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
            LlamaServer = new PartialLlamaServerSettings { TopK = 40 },
        };
        var second = new PartialConfig
        {
            LlamaServer = new PartialLlamaServerSettings(), // no TopK
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
            LlamaServer = new PartialLlamaServerSettings { ParallelSlotCount = 2 },
        };
        var cli = new PartialConfig
        {
            LlamaServer = new PartialLlamaServerSettings { ParallelSlotCount = 8 },
        };

        var result = _merger.Merge([file], cli);

        _ = result.LlamaServer.ParallelSlotCount.Should().Be(8);
    }

    [Fact]
    public void Merge_CliOverride_NullCliDoesNotErase()
    {
        var file = new PartialConfig
        {
            LlamaServer = new PartialLlamaServerSettings { ParallelSlotCount = 4 },
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

        _ = result.Run.OutputDirectoryPath.Should().Be("./results");
        _ = result.Run.ExportShellTarget.Should().Be(ShellTarget.Bash);
        _ = result.Run.ContinueOnEvalFailure.Should().BeTrue();
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
    // EvalSets come from highest-priority file that defines them
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_EvalSets_TakenFromLastFileWithSets()
    {
        var first = new PartialConfig
        {
            EvalSets =
            [
                new EvalSetConfig { Id = "first-set", PipelineName = "PipeA" },
            ],
        };
        var second = new PartialConfig
        {
            EvalSets =
            [
                new EvalSetConfig { Id = "second-set", PipelineName = "PipeB" },
            ],
        };

        var result = _merger.Merge([first, second]);

        _ = result.EvalSets.Should().HaveCount(1);
        _ = result.EvalSets[0].Id.Should().Be("second-set");
    }

    [Fact]
    public void Merge_EvalSets_FallsBackToFirstFile_WhenSecondHasNone()
    {
        var first = new PartialConfig
        {
            EvalSets = [new EvalSetConfig { Id = "from-first", PipelineName = "PipeA" }],
        };
        var second = new PartialConfig(); // no EvalSets

        var result = _merger.Merge([first, second]);

        _ = result.EvalSets.Should().HaveCount(1);
        _ = result.EvalSets[0].Id.Should().Be("from-first");
    }

    // -------------------------------------------------------------------------
    // Server config merging
    // -------------------------------------------------------------------------

    [Fact]
    public void Merge_ServerConfig_Defaults()
    {
        var result = _merger.Merge([]);

        _ = result.Server.Host.Should().Be("127.0.0.1");
        _ = result.Server.Port.Should().Be(8080);
        _ = result.Server.Manage.Should().BeFalse();
    }

    [Fact]
    public void Merge_ServerConfig_Override()
    {
        var file = new PartialConfig
        {
            Server = new PartialServerConfig
            {
                Manage = true,
                Host = "0.0.0.0",
                Port = 9090,
                Model = new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = "/models/model.gguf" },
            },
        };

        var result = _merger.Merge([file]);

        _ = result.Server.Manage.Should().BeTrue();
        _ = result.Server.Host.Should().Be("0.0.0.0");
        _ = result.Server.Port.Should().Be(9090);
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
            Judge = new PartialJudgeConfig { BaseUrl = "http://judge1:8080" },
        };
        var second = new PartialConfig
        {
            Judge = new PartialJudgeConfig { BaseUrl = "http://judge2:8080" },
        };

        var result = _merger.Merge([first, second]);

        _ = result.Judge!.BaseUrl.Should().Be("http://judge2:8080");
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
        _ = result.EvalSets.Should().BeEmpty();
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
            LlamaServer = new PartialLlamaServerSettings { ExtraArgs = ["--arg-from-first"] },
        };
        var second = new PartialConfig
        {
            LlamaServer = new PartialLlamaServerSettings { ExtraArgs = ["--arg-from-second"] },
        };

        var result = _merger.Merge([first, second]);

        _ = result.LlamaServer.ExtraArgs.Should().ContainSingle().Which.Should().Be("--arg-from-second");
    }
}
