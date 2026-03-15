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
    private readonly IToastService _toastService;

    public WizardViewModelTests()
    {
        _filePicker = Substitute.For<IFilePickerService>();
        _toastService = Substitute.For<IToastService>();
    }

    #region Initialization

    [Fact]
    public void Constructor_Sets_Default_Values()
    {
        // Arrange & Act
        var vm = new WizardViewModel(_filePicker, _toastService);

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
        var vm = new WizardViewModel(_filePicker, _toastService);

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
        var vm = new WizardViewModel(_filePicker, _toastService);

        // Act & Assert
        _ = vm.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void CanGoForward_Is_True_When_Step_Is_Valid()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService);

        // Act & Assert
        _ = vm.CanGoForward.Should().BeTrue();
    }

    [Fact]
    public async Task GoBack_Moves_To_Previous_StepAsync()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService);
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
        var vm = new WizardViewModel(_filePicker, _toastService);

        // Act
        await vm.GoForwardAsync();

        // Assert - First step after ContinueRun is now PipelineSelection
        _ = vm.CurrentStep.Should().Be(WizardStepKind.PipelineSelection);
        _ = vm.CanGoBack.Should().BeTrue();
    }

    [Fact]
    public void GoBack_Does_Nothing_At_First_Step()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService);

        // Act
        vm.GoBack();

        // Assert
        _ = vm.CurrentStep.Should().Be(WizardStepKind.ContinueRun);
    }

    [Fact]
    public async Task GoForward_AutoSelects_ShellTarget_On_Output_StepAsync()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Set up valid state for each step
        vm.ManageServer = true;
        vm.UseLocalFile = true;
        vm.LocalModelPath = "test.gguf";

        // Act - navigate through steps
        await vm.GoForwardAsync(); // PipelineSelection (no validation)
        await vm.GoForwardAsync(); // ModelAndServer (valid)
        await vm.GoForwardAsync(); // PerformanceSettings (no validation)
        await vm.GoForwardAsync(); // EvaluationDataset (need valid data source)
        vm.UseSingleFileDataSource = true;
        vm.DataFilePath = Path.GetTempFileName();
        await vm.GoForwardAsync(); // FieldMapping (no validation)
        await vm.GoForwardAsync(); // PipelineConfiguration (no validation)
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
        var vm = new WizardViewModel(_filePicker, _toastService);
        await vm.GoForwardAsync(); // Move to PipelineSelection
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
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            ManageServer = true,
            UseLocalFile = true,
            LocalModelPath = "/path/to/model.gguf"
        };

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateServerStep_Error_When_Managed_With_No_Local_File()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            CurrentStep = WizardStepKind.ModelAndServer,
            ManageServer = true,
            UseLocalFile = true,
            LocalModelPath = null
        };

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
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            CurrentStep = WizardStepKind.ModelAndServer,
            ManageServer = true,
            UseLocalFile = false,
            HfRepo = null
        };

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
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            CurrentStep = WizardStepKind.ModelAndServer,
            ManageServer = false,
            ServerUrl = null
        };

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
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            CurrentStep = WizardStepKind.EvaluationDataset,
            UseSingleFileDataSource = true,
            DataFilePath = Path.GetTempFileName()
        };

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateDatasetStep_Error_When_File_Does_Not_Exist()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            CurrentStep = WizardStepKind.EvaluationDataset,
            UseSingleFileDataSource = true,
            DataFilePath = "/nonexistent/file.json"
        };

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
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            CurrentStep = WizardStepKind.EvaluationDataset,
            UseSingleFileDataSource = false,
            PromptDir = Path.GetTempPath()
        };

        // Act
        var errors = vm.ValidateCurrentStep();

        // Assert
        _ = errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateScoringStep_Error_When_Judge_Enabled_With_No_Model()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            CurrentStep = WizardStepKind.Scoring,
            EnableJudge = true,
            JudgeManageServer = true,
            JudgeUseLocalFile = true,
            JudgeLocalModelPath = null
        };

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
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Set properties to mark them as edited (change from defaults first)
        vm.ManageServer = false;
        vm.ManageServer = true;  // Now set to true to mark as edited
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
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Set properties to mark them as edited (change from defaults first)
        vm.ManageServer = false;
        vm.ManageServer = true;  // Now set to true to mark as edited
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
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Set properties to mark them as edited
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
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Set properties to mark them as edited
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
    public void BuildPartialConfig_Jsonl_DataSource_Creates_Correct_Config()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Set properties to mark them as edited
        vm.UseSingleFileDataSource = true;
        vm.DataFilePath = "/path/to/data.jsonl";

        // Act
        var config = vm.BuildPartialConfig();

        // Assert
        _ = config.EvalSets![0].DataSource.Kind.Should().Be(DataSourceKind.SingleFile);
        _ = config.EvalSets[0].DataSource.FilePath.Should().Be("/path/to/data.jsonl");
    }

    [Fact]
    public void BuildPartialConfig_Judge_Enabled_Creates_Correct_Config()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Set properties to mark them as edited
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
        _ = config.Judge!.Enable.Should().BeTrue();
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
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Set properties to mark them as edited
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

    [Fact]
    public void BuildPartialConfig_Unedited_Fields_Are_Null()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Don't set any properties - leave them all unedited
        // Note: Host defaults to "127.0.0.1" and Port to 8080, so Server will have those default values

        // Act
        var config = vm.BuildPartialConfig();

        // Assert - unedited llama settings and judge should be null
        // Server will have default Host/Port values
        _ = config.LlamaSettings.Should().BeNull();
        _ = config.Judge.Should().BeNull();
        // DataSource should still be created with defaults
        _ = config.EvalSets.Should().NotBeNull();
        
        // Server config exists but has null Manage and Model (not configured)
        _ = config.Server.Should().NotBeNull();
        _ = config.Server.Manage.Should().BeNull();
        _ = config.Server.Model.Should().BeNull();
    }

    [Fact]
    public void BuildPartialConfig_Only_Edited_Fields_Are_Included()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService);
        // Only edit a subset of properties
        vm.GpuLayerCount = 40;
        vm.EnableFlashAttention = true;

        // Act
        var config = vm.BuildPartialConfig();

        // Assert - only edited fields should have values
        _ = config.LlamaSettings.Should().NotBeNull();
        _ = config.LlamaSettings!.GpuLayerCount.Should().Be(40);
        _ = config.LlamaSettings.EnableFlashAttention.Should().BeTrue();
        // Unedited fields should be null
        _ = config.LlamaSettings.ContextWindowTokens.Should().BeNull();
        _ = config.LlamaSettings.BatchSizeTokens.Should().BeNull();
        _ = config.LlamaSettings.ThreadCount.Should().BeNull();
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
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            // Act
            SelectedPipelineIndex = index
        };

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
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            PipelineName = name
        };

        // Act & Assert
        _ = vm.SelectedPipelineIndex.Should().Be(expectedIndex);
    }

    #endregion

    #region SelectedJudgeTemplateIndex

    [Theory]
    [InlineData(0, "casual-q-a-judge-template")]
    [InlineData(1, "code-quality-judge-template")]
    [InlineData(2, "pass-fail")]
    [InlineData(3, "standard")]
    [InlineData(4, "structured-json")]
    [InlineData(5, "translation-judge-template")]
    public void SelectedJudgeTemplateIndex_Maps_Correctly(int index, string expectedTemplate)
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            // Act
            SelectedJudgeTemplateIndex = index
        };

        // Assert
        _ = vm.JudgeTemplate.Should().Be(expectedTemplate);
        _ = vm.SelectedJudgeTemplateIndex.Should().Be(index);
    }

    [Theory]
    [InlineData("casual-q-a-judge-template", 0)]
    [InlineData("code-quality-judge-template", 1)]
    [InlineData("pass-fail", 2)]
    [InlineData("standard", 3)]
    [InlineData("structured-json", 4)]
    [InlineData("translation-judge-template", 5)]
    public void SelectedJudgeTemplateIndex_Getter_Maps_Correctly(string template, int expectedIndex)
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            JudgeTemplate = template
        };

        // Act & Assert
        _ = vm.SelectedJudgeTemplateIndex.Should().Be(expectedIndex);
    }

    #endregion

    #region UseSingleFileDataSource / UseDirectoryDataSource

    [Fact]
    public void UseSingleFileDataSource_Switch_To_Directory_Does_Not_Clear_FilePath()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            DataFilePath = "/path/to/file.json",

            // Act
            UseSingleFileDataSource = false
        };

        // Assert
        _ = vm.UseDirectoryDataSource.Should().BeTrue();
        // Path fields are preserved when switching modes
        _ = vm.DataFilePath.Should().Be("/path/to/file.json");
    }

    [Fact]
    public void UseDirectoryDataSource_Switch_To_File_Does_Not_Clear_Directories()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            UseSingleFileDataSource = false,
            PromptDir = "/path/to/prompts",
            ExpectedDir = "/path/to/expected"
        };

        // Act
        vm.UseSingleFileDataSource = true;

        // Assert
        _ = vm.UseDirectoryDataSource.Should().BeFalse();
        // Path fields are preserved when switching modes
        _ = vm.PromptDir.Should().Be("/path/to/prompts");
        _ = vm.ExpectedDir.Should().Be("/path/to/expected");
    }

    #endregion

    #region ResetToDefaults

    [Fact]
    public void ResetToDefaults_Resets_All_Properties()
    {
        // Arrange
        var vm = new WizardViewModel(_filePicker, _toastService)
        {
            ManageServer = false,
            ServerUrl = "http://example.com",
            GpuLayerCount = 50,
            PipelineName = "Translation",
            EnableJudge = true,
            JudgeTemplate = "json"
        };

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
        var vm = new WizardViewModel(_filePicker, _toastService);
        var propertyChanged = new List<string>();
        vm.PropertyChanged += (_, e) => propertyChanged.Add(e.PropertyName!);

        // Act
        vm.LocalModelPath = "/path/to/model.gguf";

        // Assert
        _ = propertyChanged.Should().Contain(nameof(WizardViewModel.CanGoForward));
    }

    #endregion
}
