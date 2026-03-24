using Seevalocal.Core.Models;
using Seevalocal.UI.ViewModels;
using Xunit;
using System.Reflection;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Tests to verify that all wizard settings are properly preserved through the config resolution flow.
/// </summary>
public class WizardConfigPreservationTests
{
    private static HashSet<string> GetEditedFields(WizardViewModel vm)
    {
        var field = typeof(WizardViewModel).GetField("_editedFields", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (HashSet<string>)field.GetValue(vm)!;
    }

    [Fact]
    public void JudgeTemplate_IsIncludedInPartialConfig_WhenSet()
    {
        // Arrange
        var vm = new WizardViewModel();
        vm.EnableJudge = true;
        vm.JudgeManageServer = true;
        vm.JudgeTemplate = "standard";  // Default value, but should still be saved when judge is enabled
        vm.JudgeHfRepo = "test/repo";  // Need a model source for judge to be included
        
        // Act - Mark judge-related fields as edited (simulating user interaction)
        GetEditedFields(vm).Add(nameof(WizardViewModel.EnableJudge));
        GetEditedFields(vm).Add(nameof(WizardViewModel.JudgeHfRepo));
        
        var partial = vm.BuildPartialConfig();
        
        // Assert
        Assert.NotNull(partial.Judge);
        Assert.Equal("standard", partial.Judge.JudgePromptTemplate);
    }

    [Fact]
    public void ReasoningBudget_IsIncludedInPartialConfig_WhenSet()
    {
        // Arrange
        var vm = new WizardViewModel();
        vm.ReasoningBudget = 1024;
        
        // Act - Mark as edited
        GetEditedFields(vm).Add(nameof(WizardViewModel.ReasoningBudget));
        
        var partial = vm.BuildPartialConfig();
        
        // Assert
        Assert.NotNull(partial.LlamaSettings);
        Assert.Equal(1024, partial.LlamaSettings.ReasoningBudget);
    }

    [Fact]
    public void TranslationSystemPrompt_IsIncludedInPipelineOptions_WhenSet()
    {
        // Arrange
        var vm = new WizardViewModel();
        vm.PipelineName = "Translation";
        vm.TranslationSystemPrompt = "Custom translation prompt";
        
        // Act - Mark as edited
        GetEditedFields(vm).Add(nameof(WizardViewModel.TranslationSystemPrompt));
        
        var partial = vm.BuildPartialConfig();
        
        // Assert
        Assert.NotNull(partial.PipelineOptions);
        Assert.True(partial.PipelineOptions.ContainsKey("systemPrompt"));
        Assert.Equal("Custom translation prompt", partial.PipelineOptions["systemPrompt"]);
    }

    [Fact]
    public void AllLlamaServerSettings_AreIncluded_WhenEdited()
    {
        // Arrange
        var vm = new WizardViewModel();
        var editedFields = GetEditedFields(vm);
        
        // Set various settings
        vm.ContextWindowTokens = 8192;
        vm.BatchSizeTokens = 512;
        vm.ReasoningBudget = 2048;
        vm.ReasoningBudgetMessage = "Think carefully";
        vm.GpuLayerCount = 32;
        vm.EnableFlashAttention = true;
        
        // Mark all as edited
        editedFields.Add(nameof(WizardViewModel.ContextWindowTokens));
        editedFields.Add(nameof(WizardViewModel.BatchSizeTokens));
        editedFields.Add(nameof(WizardViewModel.ReasoningBudget));
        editedFields.Add(nameof(WizardViewModel.ReasoningBudgetMessage));
        editedFields.Add(nameof(WizardViewModel.GpuLayerCount));
        editedFields.Add(nameof(WizardViewModel.EnableFlashAttention));
        
        // Act
        var partial = vm.BuildPartialConfig();
        
        // Assert
        Assert.NotNull(partial.LlamaSettings);
        Assert.Equal(8192, partial.LlamaSettings.ContextWindowTokens);
        Assert.Equal(512, partial.LlamaSettings.BatchSizeTokens);
        Assert.Equal(2048, partial.LlamaSettings.ReasoningBudget);
        Assert.Equal("Think carefully", partial.LlamaSettings.ReasoningBudgetMessage);
        Assert.Equal(32, partial.LlamaSettings.GpuLayerCount);
        Assert.True(partial.LlamaSettings.EnableFlashAttention);
    }

    [Fact]
    public void JudgeLlamaServerSettings_AreIncluded_WhenEdited()
    {
        // Arrange
        var vm = new WizardViewModel();
        var editedFields = GetEditedFields(vm);
        
        vm.EnableJudge = true;
        vm.JudgeManageServer = true;
        vm.JudgeHfRepo = "test/repo";  // Need model source
        vm.JudgeContextWindowTokens = 4096;
        vm.JudgeReasoningBudget = 1024;
        vm.JudgeGpuLayerCount = 16;
        
        editedFields.Add(nameof(WizardViewModel.EnableJudge));
        editedFields.Add(nameof(WizardViewModel.JudgeHfRepo));
        editedFields.Add(nameof(WizardViewModel.JudgeContextWindowTokens));
        editedFields.Add(nameof(WizardViewModel.JudgeReasoningBudget));
        editedFields.Add(nameof(WizardViewModel.JudgeGpuLayerCount));
        
        // Act
        var partial = vm.BuildPartialConfig();
        
        // Assert
        Assert.NotNull(partial.Judge);
        Assert.NotNull(partial.Judge.ServerSettings);
        Assert.Equal(4096, partial.Judge.ServerSettings.ContextWindowTokens);
        Assert.Equal(1024, partial.Judge.ServerSettings.ReasoningBudget);
        Assert.Equal(16, partial.Judge.ServerSettings.GpuLayerCount);
    }

    [Fact]
    public void BuildPartialConfig_IncludesReasoningBudget_WhenEdited()
    {
        // Arrange
        var vm = new WizardViewModel();
        vm.ReasoningBudget = 512;
        GetEditedFields(vm).Add(nameof(WizardViewModel.ReasoningBudget));
        
        // Act
        var partial = vm.BuildPartialConfig();
        
        // Assert
        Assert.NotNull(partial.LlamaSettings);
        Assert.Equal(512, partial.LlamaSettings.ReasoningBudget);
    }

    [Fact]
    public void BuildPartialConfig_IncludesJudgeTemplate_WhenJudgeEnabled()
    {
        // Arrange
        var vm = new WizardViewModel();
        vm.EnableJudge = true;
        vm.JudgeManageServer = true;
        vm.JudgeTemplate = "standard";
        vm.JudgeHfRepo = "test/repo";  // Need model source
        GetEditedFields(vm).Add(nameof(WizardViewModel.EnableJudge));
        GetEditedFields(vm).Add(nameof(WizardViewModel.JudgeHfRepo));
        
        // Act
        var partial = vm.BuildPartialConfig();
        
        // Assert
        Assert.NotNull(partial.Judge);
        Assert.Equal("standard", partial.Judge.JudgePromptTemplate);
    }
}
