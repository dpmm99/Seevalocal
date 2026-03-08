using FluentAssertions;
using Seevalocal.Core.Models;
using Seevalocal.UI.Commands;
using Xunit;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Additional tests for CliSettingsAdapter covering more scenarios.
/// </summary>
public sealed class CliSettingsAdapterTests
{
    #region Server Configuration

    [Fact]
    public void ToPartialConfig_No_Settings_Returns_Null_Server()
    {
        // Arrange
        var settings = new RunCommandSettings();

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server.Should().BeNull();
    }

    [Fact]
    public void ToPartialConfig_Manage_True_Creates_ServerConfig()
    {
        // Arrange
        var settings = new RunCommandSettings { Manage = true };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server.Should().NotBeNull();
        _ = config.Server!.Manage.Should().BeTrue();
    }

    [Fact]
    public void ToPartialConfig_NoManage_Sets_Manage_False()
    {
        // Arrange
        var settings = new RunCommandSettings { NoManage = true };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server!.Manage.Should().BeFalse();
    }

    [Fact]
    public void ToPartialConfig_ExecutablePath_Implies_Manage_True()
    {
        // Arrange
        var settings = new RunCommandSettings { ExecutablePath = "/usr/bin/llama-server" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server!.Manage.Should().BeTrue();
        _ = config.Server.ExecutablePath.Should().Be("/usr/bin/llama-server");
    }

    [Fact]
    public void ToPartialConfig_ServerUrl_Implies_Manage_False()
    {
        // Arrange
        var settings = new RunCommandSettings { ServerUrl = "http://localhost:8080" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server!.Manage.Should().BeFalse();
        _ = config.Server.BaseUrl.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void ToPartialConfig_Host_And_Port_Set_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { Host = "0.0.0.0", Port = 9000 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server!.Host.Should().Be("0.0.0.0");
        _ = config.Server.Port.Should().Be(9000);
    }

    [Fact]
    public void ToPartialConfig_ApiKey_Set_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { ApiKey = "secret-key" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server!.ApiKey.Should().Be("secret-key");
    }

    [Fact]
    public void ToPartialConfig_ExtraArgs_Are_Preserved()
    {
        // Arrange
        var settings = new RunCommandSettings { ExtraArgs = ["--arg1", "--arg2", "value"] };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert - ExtraArgs are only included if server config is created
        // Since no other server settings are set, Server config might be null
        // Let's add a server setting to ensure Server config is created
        settings.Manage = true;
        config = CliSettingsAdapter.ToPartialConfig(settings);
        
        _ = config.Server!.ExtraArgs.Should().HaveCount(3);
        _ = config.Server.ExtraArgs![0].Should().Be("--arg1");
        _ = config.Server.ExtraArgs[1].Should().Be("--arg2");
        _ = config.Server.ExtraArgs[2].Should().Be("value");
    }

    #endregion

    #region Model Source

    [Fact]
    public void ToPartialConfig_ModelFilePath_Creates_LocalFile_Source()
    {
        // Arrange
        var settings = new RunCommandSettings { ModelFilePath = "/models/test.gguf" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server!.Model!.Kind.Should().Be(ModelSourceKind.LocalFile);
        _ = config.Server.Model.FilePath.Should().Be("/models/test.gguf");
    }

    [Fact]
    public void ToPartialConfig_HfRepo_Without_Quant_Creates_HuggingFace_Source()
    {
        // Arrange
        var settings = new RunCommandSettings { HfRepo = "TheBloke/Mistral-7B-GGUF" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server!.Model!.Kind.Should().Be(ModelSourceKind.HuggingFace);
        _ = config.Server.Model.HfRepo.Should().Be("TheBloke/Mistral-7B-GGUF");
        _ = config.Server.Model.HfQuant.Should().BeNull();
    }

    [Fact]
    public void ToPartialConfig_HfRepo_With_Quant_Splits_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { HfRepo = "TheBloke/Mistral-7B-GGUF:Q5_K_M" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server!.Model!.Kind.Should().Be(ModelSourceKind.HuggingFace);
        _ = config.Server.Model.HfRepo.Should().Be("TheBloke/Mistral-7B-GGUF");
        _ = config.Server.Model.HfQuant.Should().Be("Q5_K_M");
    }

    [Fact]
    public void ToPartialConfig_HfRepo_With_Token_Includes_Token()
    {
        // Arrange
        var settings = new RunCommandSettings
        {
            HfRepo = "TheBloke/Mistral-7B-GGUF",
            HfToken = "hf_test_token"
        };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Server!.Model!.HfToken.Should().Be("hf_test_token");
    }

    #endregion

    #region Llama Server Settings

    [Fact]
    public void ToPartialConfig_No_LlamaSettings_Returns_Null_LlamaSettings()
    {
        // Arrange
        var settings = new RunCommandSettings();

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings.Should().BeNull();
    }

    [Fact]
    public void ToPartialConfig_ContextWindowTokens_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { ContextWindowTokens = 16384 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.ContextWindowTokens.Should().Be(16384);
    }

    [Fact]
    public void ToPartialConfig_BatchTokens_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { BatchTokens = 2048 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.BatchSizeTokens.Should().Be(2048);
    }

    [Fact]
    public void ToPartialConfig_UBatchTokens_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { UBatchTokens = 512 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.UbatchSizeTokens.Should().Be(512);
    }

    [Fact]
    public void ToPartialConfig_ParallelSlotCount_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { ParallelSlotCount = 8 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.ParallelSlotCount.Should().Be(8);
    }

    [Fact]
    public void ToPartialConfig_EnableFlashAttention_True_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { EnableFlashAttention = true };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.EnableFlashAttention.Should().BeTrue();
    }

    [Fact]
    public void ToPartialConfig_EnableFlashAttention_False_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { EnableFlashAttention = false };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.EnableFlashAttention.Should().BeFalse();
    }

    [Fact]
    public void ToPartialConfig_EnableCachePrompt_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { EnableCachePrompt = false };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.EnableCachePrompt.Should().BeFalse();
    }

    [Fact]
    public void ToPartialConfig_EnableContextShift_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { EnableContextShift = true };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.EnableContextShift.Should().BeTrue();
    }

    [Fact]
    public void ToPartialConfig_KvTypeK_And_V_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { KvTypeK = "q8_0", KvTypeV = "f16" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.KvCacheTypeK.Should().Be("q8_0");
        _ = config.LlamaSettings.KvCacheTypeV.Should().Be("f16");
    }

    [Fact]
    public void ToPartialConfig_ThreadCount_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { ThreadCount = 16 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.ThreadCount.Should().Be(16);
    }

    [Fact]
    public void ToPartialConfig_SamplingSettings_Are_Set_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings
        {
            SamplingTemperature = 0.9,
            TopP = 0.95,
            TopK = 50,
            MinP = 0.1,
            Seed = 12345
        };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.SamplingTemperature.Should().BeApproximately(0.9, 1e-9);
        _ = config.LlamaSettings.TopP.Should().BeApproximately(0.95, 1e-9);
        _ = config.LlamaSettings.TopK.Should().Be(50);
        _ = config.LlamaSettings.MinP.Should().BeApproximately(0.1, 1e-9);
        _ = config.LlamaSettings.Seed.Should().Be(12345);
    }

    [Fact]
    public void ToPartialConfig_ChatTemplate_And_ReasoningFormat_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings
        {
            ChatTemplate = "chatml",
            ReasoningFormat = "deepseek"
        };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.ChatTemplate.Should().Be("chatml");
        _ = config.LlamaSettings.ReasoningFormat.Should().Be("deepseek");
    }

    [Fact]
    public void ToPartialConfig_LogVerbosity_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { LogVerbosity = 2 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.LlamaSettings!.LogVerbosity.Should().Be(2);
    }

    #endregion

    #region Judge Configuration

    [Fact]
    public void ToPartialConfig_No_JudgeSettings_Returns_Null_Judge()
    {
        // Arrange
        var settings = new RunCommandSettings();

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Judge.Should().BeNull();
    }

    [Fact]
    public void ToPartialConfig_JudgeUrl_Creates_JudgeConfig()
    {
        // Arrange
        var settings = new RunCommandSettings { JudgeUrl = "http://localhost:8081" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Judge.Should().NotBeNull();
        _ = config.Judge!.BaseUrl.Should().Be("http://localhost:8081");
        _ = config.Judge.Manage.Should().BeFalse();
    }

    [Fact]
    public void ToPartialConfig_JudgeModelFilePath_Creates_JudgeConfig_With_Model()
    {
        // Arrange
        var settings = new RunCommandSettings { JudgeModelFilePath = "/models/judge.gguf" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Judge.Should().NotBeNull();
        _ = config.Judge!.ServerSettings!.ModelAlias.Should().Be("/models/judge.gguf");
    }

    [Fact]
    public void ToPartialConfig_JudgeHfRepo_Creates_JudgeConfig_With_Model()
    {
        // Arrange
        var settings = new RunCommandSettings { JudgeHfRepo = "TheBloke/Mistral-7B-GGUF" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Judge.Should().NotBeNull();
        _ = config.Judge!.ServerSettings!.ModelAlias.Should().Be("TheBloke/Mistral-7B-GGUF");
    }

    [Fact]
    public void ToPartialConfig_JudgeApiKey_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings
        {
            JudgeUrl = "http://judge.example.com",
            JudgeApiKey = "judge-secret"
        };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Judge!.ServerConfig!.ApiKey.Should().Be("judge-secret");
    }

    [Fact]
    public void ToPartialConfig_JudgeTemplate_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings
        {
            JudgeUrl = "http://localhost:8081",
            JudgeTemplate = "pass-fail"
        };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Judge!.JudgePromptTemplate.Should().Be("pass-fail");
    }

    [Fact]
    public void ToPartialConfig_JudgeScoreMin_And_Max_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings
        {
            JudgeUrl = "http://localhost:8081",
            JudgeScoreMin = 0,
            JudgeScoreMax = 5
        };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Judge!.ScoreMinValue.Should().Be(0);
        _ = config.Judge.ScoreMaxValue.Should().Be(5);
    }

    #endregion

    #region Eval Set Configuration

    [Fact]
    public void ToPartialConfig_No_EvalSettings_Returns_Null_EvalSets()
    {
        // Arrange
        var settings = new RunCommandSettings();

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.EvalSets.Should().BeNull();
    }

    [Fact]
    public void ToPartialConfig_PipelineName_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings
        {
            PipelineName = "Translation",
            DataFilePath = "/data.json"
        };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.EvalSets![0].PipelineName.Should().Be("Translation");
    }

    [Fact]
    public void ToPartialConfig_DataFilePath_Creates_File_DataSource()
    {
        // Arrange
        var settings = new RunCommandSettings { DataFilePath = "/data/evals.json" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.EvalSets![0].DataSource.Kind.Should().Be(DataSourceKind.File);
        _ = config.EvalSets[0].DataSource.FilePath.Should().Be("/data/evals.json");
    }

    [Fact]
    public void ToPartialConfig_PromptDir_Creates_DirectoryPair_DataSource()
    {
        // Arrange
        var settings = new RunCommandSettings
        {
            PromptDir = "/prompts",
            ExpectedDir = "/expected"
        };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.EvalSets![0].DataSource.Kind.Should().Be(DataSourceKind.DirectoryPair);
        _ = config.EvalSets[0].DataSource.PromptDirectoryPath.Should().Be("/prompts");
        _ = config.EvalSets[0].DataSource.ExpectedOutputDirectoryPath.Should().Be("/expected");
    }

    [Fact]
    public void ToPartialConfig_Default_PipelineName_Is_CasualQA()
    {
        // Arrange
        var settings = new RunCommandSettings { DataFilePath = "/data.json" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.EvalSets![0].PipelineName.Should().Be("CasualQA");
    }

    #endregion

    #region Output Configuration

    [Fact]
    public void ToPartialConfig_No_OutputSettings_Returns_Null_Output()
    {
        // Arrange
        var settings = new RunCommandSettings();

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Output.Should().BeNull();
    }

    [Fact]
    public void ToPartialConfig_OutputDir_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { OutputDir = "/results" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Output!.OutputDir.Should().Be("/results");
    }

    [Fact]
    public void ToPartialConfig_ShellDialect_Bash_Maps_To_Bash()
    {
        // Arrange
        var settings = new RunCommandSettings { ShellDialect = "bash", OutputDir = "/r" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Output!.ShellTarget.Should().Be(ShellTarget.Bash);
    }

    [Fact]
    public void ToPartialConfig_ShellDialect_PowerShell_Maps_To_PowerShell()
    {
        // Arrange
        var settings = new RunCommandSettings { ShellDialect = "powershell", OutputDir = "/r" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Output!.ShellTarget.Should().Be(ShellTarget.PowerShell);
    }

    [Fact]
    public void ToPartialConfig_ShellDialect_Ps_Maps_To_PowerShell()
    {
        // Arrange
        var settings = new RunCommandSettings { ShellDialect = "ps", OutputDir = "/r" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Output!.ShellTarget.Should().Be(ShellTarget.PowerShell);
    }

    [Fact]
    public void ToPartialConfig_NoParquet_Sets_WriteResultsParquet_False()
    {
        // Arrange
        var settings = new RunCommandSettings { NoParquet = true, OutputDir = "/r" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Output!.WriteResultsParquet.Should().BeFalse();
    }

    [Fact]
    public void ToPartialConfig_NoRawResponse_Sets_IncludeRawLlmResponse_False()
    {
        // Arrange
        var settings = new RunCommandSettings { NoRawResponse = true, OutputDir = "/r" };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Output!.IncludeRawLlmResponse.Should().BeFalse();
    }

    #endregion

    #region Run Meta Configuration

    [Fact]
    public void ToPartialConfig_No_RunSettings_Returns_Null_Run()
    {
        // Arrange
        var settings = new RunCommandSettings();

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Run.Should().BeNull();
    }

    [Fact]
    public void ToPartialConfig_MaxConcurrent_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { MaxConcurrent = 4 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Run!.MaxConcurrentEvals.Should().Be(4);
    }

    [Fact]
    public void ToPartialConfig_StopOnFailure_Sets_ContinueOnEvalFailure_False()
    {
        // Arrange
        var settings = new RunCommandSettings { StopOnFailure = true };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Run!.ContinueOnEvalFailure.Should().BeFalse();
    }

    [Fact]
    public void ToPartialConfig_ContinueOnFailure_Sets_ContinueOnEvalFailure_True()
    {
        // Arrange
        var settings = new RunCommandSettings { ContinueOnFailure = true };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Run!.ContinueOnEvalFailure.Should().BeTrue();
    }

    [Fact]
    public void ToPartialConfig_TimeoutSeconds_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { TimeoutSeconds = 120.5 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Run!.TimeoutSeconds.Should().BeApproximately(120.5, 1e-9);
    }

    [Fact]
    public void ToPartialConfig_RetryCount_Sets_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings { RetryCount = 3 };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert
        _ = config.Run!.RetryCount.Should().Be(3);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ToPartialConfig_Full_Configuration_Builds_Correctly()
    {
        // Arrange
        var settings = new RunCommandSettings
        {
            // Server
            Manage = true,
            ModelFilePath = "/models/test.gguf",
            Host = "0.0.0.0",
            Port = 9000,
            ApiKey = "test-key",

            // Llama settings
            ContextWindowTokens = 8192,
            GpuLayerCount = 35,
            EnableFlashAttention = true,
            ThreadCount = 8,
            SamplingTemperature = 0.7,

            // Eval
            PipelineName = "Translation",
            DataFilePath = "/data/evals.json",

            // Judge
            JudgeUrl = "http://localhost:8081",
            JudgeTemplate = "pass-fail",
            JudgeScoreMin = 0,
            JudgeScoreMax = 1,

            // Output
            OutputDir = "/results",
            ShellDialect = "bash",
            NoParquet = false,
            NoRawResponse = false,

            // Run control
            MaxConcurrent = 4,
            TimeoutSeconds = 300
        };

        // Act
        var config = CliSettingsAdapter.ToPartialConfig(settings);

        // Assert - Server
        _ = config.Server.Should().NotBeNull();
        _ = config.Server!.Manage.Should().BeTrue();
        _ = config.Server.Model!.Kind.Should().Be(ModelSourceKind.LocalFile);
        _ = config.Server.Host.Should().Be("0.0.0.0");
        _ = config.Server.Port.Should().Be(9000);

        // Assert - Llama settings
        _ = config.LlamaSettings!.ContextWindowTokens.Should().Be(8192);
        _ = config.LlamaSettings.GpuLayerCount.Should().Be(35);
        _ = config.LlamaSettings.EnableFlashAttention.Should().BeTrue();

        // Assert - Eval
        _ = config.EvalSets![0].PipelineName.Should().Be("Translation");

        // Assert - Judge
        _ = config.Judge!.BaseUrl.Should().Be("http://localhost:8081");

        // Assert - Output
        _ = config.Output!.OutputDir.Should().Be("/results");
        _ = config.Output.ShellTarget.Should().Be(ShellTarget.Bash);

        // Assert - Run
        _ = config.Run!.MaxConcurrentEvals.Should().Be(4);
        _ = config.Run.TimeoutSeconds.Should().BeApproximately(300, 1e-9);
    }

    #endregion
}
