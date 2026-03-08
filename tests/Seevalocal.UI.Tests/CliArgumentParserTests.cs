using FluentAssertions;
using Seevalocal.Core.Models;
using Seevalocal.UI.Commands;
using Xunit;

namespace Seevalocal.Cli.Tests;

/// <summary>
/// Tests that each CLI flag correctly sets the corresponding PartialConfig field,
/// and that unset flags produce null (not sentinel values).
/// </summary>
public sealed class CliArgumentParserTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static PartialConfig Adapt(Action<RunCommandSettings> configure)
    {
        var s = new RunCommandSettings();
        configure(s);
        return CliSettingsAdapter.ToPartialConfig(s);
    }

    // ─── Server flags ─────────────────────────────────────────────────────────

    [Fact]
    public void ExecutablePath_Sets_ServerConfig_ExecutablePath()
    {
        var config = Adapt(static s => s.ExecutablePath = "/usr/bin/llama-server");
        _ = config.Server!.ExecutablePath.Should().Be("/usr/bin/llama-server");
    }

    [Fact]
    public void ExecutablePath_Implies_Manage_True()
    {
        var config = Adapt(static s => s.ExecutablePath = "/bin/llama-server");
        _ = config.Server!.Manage.Should().BeTrue();
    }

    [Fact]
    public void ServerUrl_Implies_Manage_False()
    {
        var config = Adapt(static s => s.ServerUrl = "http://localhost:8080");
        _ = config.Server!.Manage.Should().BeFalse();
        _ = config.Server.BaseUrl.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void NoManage_Sets_Manage_False()
    {
        var config = Adapt(static s => s.NoManage = true);
        _ = config.Server!.Manage.Should().BeFalse();
    }

    [Fact]
    public void ModelFile_Creates_LocalFile_ModelSource()
    {
        var config = Adapt(static s => s.ModelFilePath = "/models/ggml.gguf");
        _ = config.Server!.Model!.Kind.Should().Be(ModelSourceKind.LocalFile);
        _ = config.Server.Model.FilePath.Should().Be("/models/ggml.gguf");
    }

    [Fact]
    public void HfRepo_Without_Quant_Creates_HuggingFace_Source()
    {
        var config = Adapt(static s => s.HfRepo = "TheBloke/Mistral-7B-GGUF");
        _ = config.Server!.Model!.Kind.Should().Be(ModelSourceKind.HuggingFace);
        _ = config.Server.Model.HfRepo.Should().Be("TheBloke/Mistral-7B-GGUF");
        _ = config.Server.Model.HfQuant.Should().BeNull();
    }

    [Fact]
    public void HfRepo_With_Quant_Splits_Correctly()
    {
        var config = Adapt(static s => s.HfRepo = "TheBloke/Mistral-7B-GGUF:Q4_K_M");
        _ = config.Server!.Model!.HfRepo.Should().Be("TheBloke/Mistral-7B-GGUF");
        _ = config.Server.Model.HfQuant.Should().Be("Q4_K_M");
    }

    [Fact]
    public void Port_Sets_ServerConfig_Port()
    {
        var config = Adapt(static s => s.Port = 9090);
        _ = config.Server!.Port.Should().Be(9090);
    }

    // ─── llama-server tuning flags ────────────────────────────────────────────

    [Fact]
    public void ContextWindow_Sets_LlamaSettings()
    {
        var config = Adapt(static s => s.ContextWindowTokens = 4096);
        _ = config.LlamaSettings!.ContextWindowTokens.Should().Be(4096);
    }

    [Fact]
    public void GpuLayers_Sets_LlamaSettings_GpuLayerCount()
    {
        var config = Adapt(static s => s.GpuLayerCount = 35);
        _ = config.LlamaSettings!.GpuLayerCount.Should().Be(35);
    }

    [Fact]
    public void FlashAttn_Sets_EnableFlashAttention_True()
    {
        var config = Adapt(static s => s.EnableFlashAttention = true);
        _ = config.LlamaSettings!.EnableFlashAttention.Should().BeTrue();
    }

    [Fact]
    public void NoFlashAttn_Sets_EnableFlashAttention_False()
    {
        var config = Adapt(static s => s.EnableFlashAttention = false);
        _ = config.LlamaSettings!.EnableFlashAttention.Should().BeFalse();
    }

    [Fact]
    public void Temperature_Sets_SamplingTemperature()
    {
        var config = Adapt(static s => s.SamplingTemperature = 0.7);
        _ = config.LlamaSettings!.SamplingTemperature.Should().BeApproximately(0.7, 1e-9);
    }

    // ─── Null fields stay null ────────────────────────────────────────────────

    [Fact]
    public void Unset_LlamaSettings_Flags_Produce_Null_Config()
    {
        var config = Adapt(static s => s.ContextWindowTokens = null);

        // If no llama settings at all, the whole LlamaSettings object should be null
        var empty = Adapt(static _ => { });
        _ = empty.LlamaSettings.Should().BeNull();
    }

    [Fact]
    public void Unset_Server_Flags_Produce_Null_ServerConfig()
    {
        var config = Adapt(static _ => { });
        _ = config.Server.Should().BeNull();
    }

    // ─── Eval options ─────────────────────────────────────────────────────────

    [Fact]
    public void PipelineName_Sets_EvalSet_PipelineName()
    {
        var config = Adapt(static s =>
        {
            s.PipelineName = "Translation";
            s.DataFilePath = "/data/prompts.json";
        });
        _ = config.EvalSets![0].PipelineName.Should().Be("Translation");
    }

    [Fact]
    public void DataFilePath_Creates_File_DataSource()
    {
        var config = Adapt(static s => s.DataFilePath = "/data/evals.json");
        _ = config.EvalSets![0].DataSource.Kind.Should().Be(DataSourceKind.File);
        _ = config.EvalSets[0].DataSource.FilePath.Should().Be("/data/evals.json");
    }

    [Fact]
    public void PromptDir_Creates_DirectoryPair_DataSource()
    {
        var config = Adapt(static s =>
        {
            s.PromptDir = "/prompts";
            s.ExpectedDir = "/expected";
        });
        _ = config.EvalSets![0].DataSource.Kind.Should().Be(DataSourceKind.DirectoryPair);
        _ = config.EvalSets[0].DataSource.PromptDirectory.Should().Be("/prompts");
        _ = config.EvalSets[0].DataSource.ExpectedDirectory.Should().Be("/expected");
    }

    // ─── Judge flags ──────────────────────────────────────────────────────────

    [Fact]
    public void JudgeUrl_Creates_JudgeConfig()
    {
        var config = Adapt(static s =>
        {
            s.JudgeUrl = "http://localhost:8081";
            s.DataFilePath = "/data.json";   // need eval settings to get judge attached
        });
        _ = config.Judge!.BaseUrl.Should().Be("http://localhost:8081");
    }

    [Fact]
    public void JudgeScoreMin_Max_Set_Correctly()
    {
        var config = Adapt(static s =>
        {
            s.JudgeUrl = "http://localhost:8081";
            s.JudgeScoreMin = 1;
            s.JudgeScoreMax = 5;
            s.DataFilePath = "/d.json";
        });
        _ = config.Judge!.ScoreMinValue.Should().Be(1);
        _ = config.Judge.ScoreMaxValue.Should().Be(5);
    }

    // ─── Output flags ─────────────────────────────────────────────────────────

    [Fact]
    public void OutputDir_Sets_OutputConfig()
    {
        var config = Adapt(static s => s.OutputDir = "/results");
        _ = config.Output!.OutputDir.Should().Be("/results");
    }

    [Fact]
    public void ShellDialect_Bash_Maps_To_Bash_Target()
    {
        var config = Adapt(static s => { s.ShellDialect = "bash"; s.OutputDir = "/r"; });
        _ = config.Output!.ShellTarget.Should().Be(ShellTarget.Bash);
    }

    [Fact]
    public void ShellDialect_PowerShell_Maps_To_PowerShell_Target()
    {
        var config = Adapt(static s => { s.ShellDialect = "powershell"; s.OutputDir = "/r"; });
        _ = config.Output!.ShellTarget.Should().Be(ShellTarget.PowerShell);
    }

    [Fact]
    public void NoParquet_Sets_WriteResultsParquet_False()
    {
        var config = Adapt(static s => { s.NoParquet = true; s.OutputDir = "/r"; });
        _ = config.Output!.WriteResultsParquet.Should().BeFalse();
    }

    [Fact]
    public void NoRawResponse_Sets_IncludeRawLlmResponse_False()
    {
        var config = Adapt(static s => { s.NoRawResponse = true; s.OutputDir = "/r"; });
        _ = config.Output!.IncludeRawLlmResponse.Should().BeFalse();
    }

    // ─── Settings layering via multiple --settings files ──────────────────────

    [Fact]
    public void Multiple_Settings_Files_Are_Captured_In_Array()
    {
        var settings = new RunCommandSettings
        {
            SettingsFiles = ["a.yml", "b.yml", "c.yml"]
        };
        _ = settings.SettingsFiles.Should().HaveCount(3);
        _ = settings.SettingsFiles[2].Should().Be("c.yml");
    }

    // ─── Run control flags ────────────────────────────────────────────────────

    [Fact]
    public void StopOnFailure_Sets_ContinueOnEvalFailure_False()
    {
        var config = Adapt(static s => s.StopOnFailure = true);
        _ = config.Run!.ContinueOnEvalFailure.Should().BeFalse();
    }

    [Fact]
    public void TimeoutSeconds_Sets_RunMeta()
    {
        var config = Adapt(static s => s.TimeoutSeconds = 30.0);
        _ = config.Run!.TimeoutSeconds.Should().BeApproximately(30.0, 1e-9);
    }

    [Fact]
    public void RetryCount_Sets_RunMeta()
    {
        var config = Adapt(static s => s.RetryCount = 5);
        _ = config.Run!.RetryCount.Should().Be(5);
    }
}
