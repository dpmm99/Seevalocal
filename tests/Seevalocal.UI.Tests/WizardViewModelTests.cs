using FluentAssertions;
using NSubstitute;
using Seevalocal.Core.Models;
using Seevalocal.UI.Services;
using Seevalocal.UI.ViewModels;
using Xunit;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Tests for the WizardViewModel.
/// </summary>
public sealed class WizardViewModelTests
{
    private readonly IFilePickerService _filePicker;

    public WizardViewModelTests()
    {
        _filePicker = Substitute.For<IFilePickerService>();
    }

    #region Initialization

    [Fact]
    public void Constructor_Sets_Default_Values()
    {
        // Arrange & Act
        var vm = new WizardViewModel(_filePicker);

        // Assert
        _ = vm.CurrentStep.Should().Be(WizardStepKind.ContinueRun);
        _ = vm.ManageServer.Should().BeTrue();
        _ = vm.UseLocalFile.Should().BeTrue();
        _ = vm.PipelineName.Should().Be("CasualQA");
        _ = vm.OutputDir.Should().Be("./results");
        _ = vm.EnableCachePrompt.Should().BeNull();
        _ = vm.EnableKvOffload.Should().BeNull();
        _ = vm.EnableMmap.Should().BeNull();
        _ = vm.SamplingTemperature.Should().BeNull();
        _ = vm.TopP.Should().BeNull();
        _ = vm.TopK.Should().BeNull();
        _ = vm.MinP.Should().BeNull();
        _ = vm.RepeatPenalty.Should().BeNull();
        _ = vm.RepeatLastNTokens.Should().BeNull();
        _ = vm.SplitMode.Should().BeNull();
        _ = vm.JudgeTemplate.Should().Be("standard");
        _ = vm.JudgeScoreMin.Should().Be(0);
        _ = vm.JudgeScoreMax.Should().Be(10);
    }

    [Fact]
    public void Constructor_Initializes_Commands()
    {
        // Arrange & Act
        var vm = new WizardViewModel(_filePicker);

        // Assert
        _ = vm.GoBackCommand.Should().NotBeNull();
        _ = vm.GoForwardCommand.Should().NotBeNull();
        _ = vm.ExportScriptCommand.Should().NotBeNull();
        _ = vm.BrowseLocalModelCommand.Should().NotBeNull();
        _ = vm.BrowseDataFileCommand.Should().NotBeNull();
        _ = vm.BrowsePromptDirCommand.Should().NotBeNull();
        _ = vm.BrowseExpectedDirCommand.Should().NotBeNull();
        _ = vm.BrowseOutputDirCommand.Should().NotBeNull();
        _ = vm.BrowseJudgeModelCommand.Should().NotBeNull();
        _ = vm.TestConnectionCommand.Should().NotBeNull();
        _ = vm.TestJudgeConnectionCommand.Should().NotBeNull();
    }

    #endregion

    #region Navigation

    [Fact]
    public void CanGoBack_Is_False_At_First_Step()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);

        // Act & Assert
        _ = vm.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void CanGoForward_Is_True_When_Step_Is_Valid()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);

        // Act & Assert
        _ = vm.CanGoForward.Should().BeTrue();
    }

    [Fact]
    public async Task GoBack_Moves_To_Previous_StepAsync()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        await vm.GoForwardAsync(); // Move to ModelAndServer (valid with default settings)

        // Act
        // Can't go forward without valid settings, but we can go back
        vm.GoBack();

        // Assert
        _ = vm.CurrentStep.Should().Be(WizardStepKind.ContinueRun);
        _ = vm.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public async Task GoForward_Moves_To_Next_StepAsync()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);

        // Act
        await vm.GoForwardAsync();

        // Assert
        _ = vm.CurrentStep.Should().Be(WizardStepKind.ModelAndServer);
        _ = vm.CanGoBack.Should().BeTrue();
    }

    [Fact]
    public void GoBack_Does_Nothing_At_First_Step()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);

        // Act
        vm.GoBack();

        // Assert
        _ = vm.CurrentStep.Should().Be(WizardStepKind.ContinueRun);
    }

    [Fact]
    public async Task GoForward_AutoSelects_ShellTarget_On_Output_StepAsync()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        // Set up valid state for each step
        vm.ManageServer = true;
        vm.UseLocalFile = true;
        vm.LocalModelPath = "test.gguf";

        // Act - navigate through steps
        await vm.GoForwardAsync(); // ModelAndServer (valid)
        await vm.GoForwardAsync(); // PerformanceSettings (no validation)
        await vm.GoForwardAsync(); // EvaluationDataset (need valid data source)
        vm.UseSingleFileDataSource = true;
        vm.DataFilePath = Path.GetTempFileName();
        await vm.GoForwardAsync(); // Scoring (no validation when judge disabled)
        await vm.GoForwardAsync(); // Output

        // Assert
        _ = vm.CurrentStep.Should().Be(WizardStepKind.Output);
        _ = vm.ShellTarget.Should().NotBeNull();
    }

    [Fact]
    public async Task CanGoForward_Is_False_When_Validation_FailsAsync()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        await vm.GoForwardAsync(); // Move to ModelAndServer
        vm.ManageServer = true;
        vm.UseLocalFile = true;
        vm.LocalModelPath = null; // Invalid - no model path

        // Act & Assert
        _ = vm.CanGoForward.Should().BeFalse();
    }

    #endregion

    #region Validation

    [Fact]
    public void ValidateServerStep_No_Errors_When_Managed_With_Local_File()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.ManageServer = true;
        vm.UseLocalFile = true;
        vm.LocalModelPath = "/path/to/model.gguf";

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateServerStep_Error_When_Managed_With_No_Local_File()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.CurrentStep = WizardStepKind.ModelAndServer;
        vm.ManageServer = true;
        vm.UseLocalFile = true;
        vm.LocalModelPath = null;

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().ContainSingle();
        _ = errors[0].Should().Contain("Model file path");
    }

    [Fact]
    public void ValidateServerStep_Error_When_Managed_With_No_HfRepo()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.CurrentStep = WizardStepKind.ModelAndServer;
        vm.ManageServer = true;
        vm.UseLocalFile = false;
        vm.HfRepo = null;

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().ContainSingle();
        _ = errors[0].Should().Contain("HuggingFace repo");
    }

    [Fact]
    public void ValidateServerStep_Error_When_Unmanaged_With_No_ServerUrl()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.CurrentStep = WizardStepKind.ModelAndServer;
        vm.ManageServer = false;
        vm.ServerUrl = null;

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().ContainSingle();
        _ = errors[0].Should().Contain("Server URL");
    }

    [Fact]
    public void ValidateDatasetStep_No_Errors_With_Valid_File()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.CurrentStep = WizardStepKind.EvaluationDataset;
        vm.UseSingleFileDataSource = true;
        vm.DataFilePath = Path.GetTempFileName();

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateDatasetStep_Error_When_File_Does_Not_Exist()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.CurrentStep = WizardStepKind.EvaluationDataset;
        vm.UseSingleFileDataSource = true;
        vm.DataFilePath = "/nonexistent/file.json";

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().ContainSingle();
        _ = errors[0].Should().Contain("does not exist");
    }

    [Fact]
    public void ValidateDatasetStep_No_Errors_With_Valid_Directory()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.CurrentStep = WizardStepKind.EvaluationDataset;
        vm.UseSingleFileDataSource = false;
        vm.PromptDir = Path.GetTempPath();

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateScoringStep_Error_When_Judge_Enabled_With_No_Model()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.CurrentStep = WizardStepKind.Scoring;
        vm.EnableJudge = true;
        vm.JudgeManageServer = true;
        vm.JudgeUseLocalFile = true;
        vm.JudgeLocalModelPath = null;

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().ContainSingle();
        _ = errors[0].Should().Contain("Judge model");
    }

    #endregion

    #region BuildPartialConfig

    [Fact]
    public void BuildPartialConfig_Managed_Local_File_Creates_Correct_Config()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.ManageServer = true;
        vm.UseLocalFile = true;
        vm.LocalModelPath = "/path/to/model.gguf";
        vm.GpuLayerCount = 35;
        vm.ContextWindowTokens = 4096;

        // Act
        var config = vm.BuildPartialConfig();

        // Assert
        _ = config.Server.Should().NotBeNull();
        _ = config.Server!.Manage.Should().BeTrue();
        _ = config.Server.Model!.Kind.Should().Be(ModelSourceKind.LocalFile);
        _ = config.Server.Model.FilePath.Should().Be("/path/to/model.gguf");
        _ = config.LlamaSettings!.GpuLayerCount.Should().Be(35);
        _ = config.LlamaSettings.ContextWindowTokens.Should().Be(4096);
    }

    [Fact]
    public void BuildPartialConfig_Managed_HuggingFace_Creates_Correct_Config()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.ManageServer = true;
        vm.UseLocalFile = false;
        vm.HfRepo = "TheBloke/Mistral-7B-GGUF:Q4_K_M";
        vm.HfToken = "test-token";

        // Act
        var config = vm.BuildPartialConfig();

        // Assert - Note: WizardViewModel doesn't split the HfRepo, it passes it as-is
        _ = config.Server!.Model!.Kind.Should().Be(ModelSourceKind.HuggingFace);
        _ = config.Server.Model.HfRepo.Should().Be("TheBloke/Mistral-7B-GGUF:Q4_K_M");
        _ = config.Server.Model.HfToken.Should().Be("test-token");
    }

    [Fact]
    public void BuildPartialConfig_Unmanaged_Creates_Correct_Config()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.ManageServer = false;
        vm.ServerUrl = "http://localhost:8080";
        vm.ApiKey = "test-api-key";

        // Act
        var config = vm.BuildPartialConfig();

        // Assert
        _ = config.Server!.Manage.Should().BeFalse();
        _ = config.Server.BaseUrl.Should().Be("http://localhost:8080");
        _ = config.Server.ApiKey.Should().Be("test-api-key");
    }

    [Fact]
    public void BuildPartialConfig_Directory_DataSource_Creates_Correct_Config()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.UseSingleFileDataSource = false;
        vm.PromptDir = "/path/to/prompts";
        vm.ExpectedDir = "/path/to/expected";

        // Act
        var config = vm.BuildPartialConfig();

        // Assert
        _ = config.EvalSets![0].DataSource.Kind.Should().Be(DataSourceKind.SplitDirectories);
        _ = config.EvalSets[0].DataSource.PromptDirectory.Should().Be("/path/to/prompts");
        _ = config.EvalSets[0].DataSource.ExpectedDirectory.Should().Be("/path/to/expected");
    }

    [Fact]
    public void BuildPartialConfig_Judge_Enabled_Creates_Correct_Config()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.EnableJudge = true;
        vm.JudgeManageServer = true;
        vm.JudgeUseLocalFile = true;
        vm.JudgeLocalModelPath = "/path/to/judge.gguf";
        vm.JudgeTemplate = "pass-fail";
        vm.JudgeScoreMin = 0;
        vm.JudgeScoreMax = 1;

        // Act
        var config = vm.BuildPartialConfig();

        // Assert
        _ = config.Judge.Should().NotBeNull();
        _ = config.Judge!.Manage.Should().BeTrue();
        _ = config.Judge.ServerConfig!.Model!.Kind.Should().Be(ModelSourceKind.LocalFile);
        _ = config.Judge.ServerConfig.Model.FilePath.Should().Be("/path/to/judge.gguf");
        _ = config.Judge.JudgePromptTemplate.Should().Be("pass-fail");
        _ = config.Judge.ScoreMinValue.Should().Be(0);
        _ = config.Judge.ScoreMaxValue.Should().Be(1);
    }

    [Fact]
    public void BuildPartialConfig_All_LlamaSettings_Are_Included()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.ContextWindowTokens = 8192;
        vm.BatchSizeTokens = 512;
        vm.ParallelSlotCount = 4;
        vm.GpuLayerCount = 40;
        vm.EnableFlashAttention = true;
        vm.SamplingTemperature = 0.8;
        vm.TopP = 0.95;
        vm.TopK = 50;
        vm.ThreadCount = 8;

        // Act
        var config = vm.BuildPartialConfig();

        // Assert
        _ = config.LlamaSettings.Should().NotBeNull();
        _ = config.LlamaSettings!.ContextWindowTokens.Should().Be(8192);
        _ = config.LlamaSettings.BatchSizeTokens.Should().Be(512);
        _ = config.LlamaSettings.ParallelSlotCount.Should().Be(4);
        _ = config.LlamaSettings.GpuLayerCount.Should().Be(40);
        _ = config.LlamaSettings.EnableFlashAttention.Should().BeTrue();
        _ = config.LlamaSettings.SamplingTemperature.Should().BeApproximately(0.8, 1e-9);
        _ = config.LlamaSettings.TopP.Should().BeApproximately(0.95, 1e-9);
        _ = config.LlamaSettings.TopK.Should().Be(50);
        _ = config.LlamaSettings.ThreadCount.Should().Be(8);
    }

    #endregion

    #region SelectedPipelineIndex

    [Theory]
    [InlineData(0, "CasualQA")]
    [InlineData(1, "Translation")]
    [InlineData(2, "CSharpCoding")]
    public void SelectedPipelineIndex_Maps_Correctly(int index, string expectedName)
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);

        // Act
        vm.SelectedPipelineIndex = index;

        // Assert
        _ = vm.PipelineName.Should().Be(expectedName);
        _ = vm.SelectedPipelineIndex.Should().Be(index);
    }

    [Theory]
    [InlineData("CasualQA", 0)]
    [InlineData("Translation", 1)]
    [InlineData("CSharpCoding", 2)]
    public void SelectedPipelineIndex_Getter_Maps_Correctly(string name, int expectedIndex)
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.PipelineName = name;

        // Act & Assert
        _ = vm.SelectedPipelineIndex.Should().Be(expectedIndex);
    }

    #endregion

    #region SelectedJudgeTemplateIndex

    [Theory]
    [InlineData(0, "standard")]
    [InlineData(1, "pass-fail")]
    [InlineData(2, "json")]
    public void SelectedJudgeTemplateIndex_Maps_Correctly(int index, string expectedTemplate)
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);

        // Act
        vm.SelectedJudgeTemplateIndex = index;

        // Assert
        _ = vm.JudgeTemplate.Should().Be(expectedTemplate);
        _ = vm.SelectedJudgeTemplateIndex.Should().Be(index);
    }

    [Theory]
    [InlineData("standard", 0)]
    [InlineData("pass-fail", 1)]
    [InlineData("json", 2)]
    public void SelectedJudgeTemplateIndex_Getter_Maps_Correctly(string template, int expectedIndex)
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.JudgeTemplate = template;

        // Act & Assert
        _ = vm.SelectedJudgeTemplateIndex.Should().Be(expectedIndex);
    }

    #endregion

    #region UseSingleFileDataSource / UseDirectoryDataSource

    [Fact]
    public void UseSingleFileDataSource_Switch_To_Directory_Clears_FilePath()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.DataFilePath = "/path/to/file.json";

        // Act
        vm.UseSingleFileDataSource = false;

        // Assert
        _ = vm.UseDirectoryDataSource.Should().BeTrue();
        _ = vm.DataFilePath.Should().BeNull();
    }

    [Fact]
    public void UseDirectoryDataSource_Switch_To_File_Clears_Directories()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.UseSingleFileDataSource = false;
        vm.PromptDir = "/path/to/prompts";
        vm.ExpectedDir = "/path/to/expected";

        // Act
        vm.UseSingleFileDataSource = true;

        // Assert
        _ = vm.UseDirectoryDataSource.Should().BeFalse();
        _ = vm.PromptDir.Should().BeNull();
        _ = vm.ExpectedDir.Should().BeNull();
    }

    #endregion

    #region ResetToDefaults

    [Fact]
    public void ResetToDefaults_Resets_All_Properties()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        vm.ManageServer = false;
        vm.ServerUrl = "http://example.com";
        vm.GpuLayerCount = 50;
        vm.PipelineName = "Translation";
        vm.EnableJudge = true;
        vm.JudgeTemplate = "json";

        // Act
        vm.ResetToDefaults();

        // Assert
        _ = vm.CurrentStep.Should().Be(WizardStepKind.ContinueRun);
        _ = vm.ManageServer.Should().BeTrue();
        _ = vm.ServerUrl.Should().BeNull();
        _ = vm.GpuLayerCount.Should().BeNull();
        _ = vm.PipelineName.Should().Be("CasualQA");
        _ = vm.EnableJudge.Should().BeFalse();
        _ = vm.JudgeTemplate.Should().Be("standard");
    }

    #endregion

    #region Property Change Notifications

    [Fact]
    public void Property_Change_Notifies_CanGoForward()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker);
        var propertyChanged = new List<string>();
        vm.PropertyChanged += (_, e) => propertyChanged.Add(e.PropertyName!);

        // Act
        vm.LocalModelPath = "/path/to/model.gguf";

        // Assert
        _ = propertyChanged.Should().Contain(nameof(WizardViewModel.CanGoForward));
    }

    #endregion
}
