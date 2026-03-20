using FluentAssertions;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Unit tests for EvalGenConfig and related models.
/// </summary>
public class EvalGenModelsTests
{
    #region EvalGenConfig Tests

    [Fact]
    public void EvalGenConfig_DefaultValues_AreValid()
    {
        // Arrange & Act
        var config = new EvalGenConfig();

        // Assert
        config.Id.Should().NotBeNullOrEmpty();
        config.RunName.Should().BeEmpty();
        config.OutputDirectoryPath.Should().BeEmpty();
        config.TargetCategoryCount.Should().Be(10);
        config.TargetProblemsPerCategory.Should().Be(5);
        config.ContinueFromCheckpoint.Should().BeFalse();
    }

    [Fact]
    public void EvalGenConfig_WithCustomValues_StoresCorrectly()
    {
        // Arrange & Act
        var config = new EvalGenConfig
        {
            Id = "test-id-123",
            RunName = "Test Run",
            OutputDirectoryPath = "./test_output",
            TargetCategoryCount = 20,
            TargetProblemsPerCategory = 10,
            DomainPrompt = "Test domain",
            ContextPrompt = "Test context",
            SystemPrompt = "Test system prompt",
            ContinueFromCheckpoint = true,
            CheckpointDatabasePath = "./test.db"
        };

        // Assert
        config.Id.Should().Be("test-id-123");
        config.RunName.Should().Be("Test Run");
        config.OutputDirectoryPath.Should().Be("./test_output");
        config.TargetCategoryCount.Should().Be(20);
        config.TargetProblemsPerCategory.Should().Be(10);
        config.DomainPrompt.Should().Be("Test domain");
        config.ContextPrompt.Should().Be("Test context");
        config.SystemPrompt.Should().Be("Test system prompt");
        config.ContinueFromCheckpoint.Should().BeTrue();
        config.CheckpointDatabasePath.Should().Be("./test.db");
    }

    #endregion

    #region GeneratedCategory Tests

    [Fact]
    public void GeneratedCategory_DefaultValues_AreValid()
    {
        // Arrange & Act
        var category = new GeneratedCategory();

        // Assert - Id is empty string by default (not auto-generated)
        category.Id.Should().BeEmpty();
        category.Name.Should().BeEmpty();
        category.Problems.Should().BeEmpty();
    }

    [Fact]
    public void GeneratedCategory_WithValues_StoresCorrectly()
    {
        // Arrange
        var problems = new List<GeneratedProblem>
        {
            new GeneratedProblem { Id = "p1", OneLineStatement = "Problem 1" }
        };

        // Act
        var category = new GeneratedCategory
        {
            Id = "cat-123",
            Name = "Test Category",
            Problems = problems
        };

        // Assert
        category.Id.Should().Be("cat-123");
        category.Name.Should().Be("Test Category");
        category.Problems.Should().HaveCount(1);
        category.Problems[0].OneLineStatement.Should().Be("Problem 1");
    }

    #endregion

    #region GeneratedProblem Tests

    [Fact]
    public void GeneratedProblem_DefaultValues_AreValid()
    {
        // Arrange & Act
        var problem = new GeneratedProblem();

        // Assert - Id is empty string by default (not auto-generated)
        problem.Id.Should().BeEmpty();
        problem.CategoryId.Should().BeEmpty();
        problem.OneLineStatement.Should().BeEmpty();
        problem.FullPrompt.Should().BeNull();
        problem.ExpectedOutput.Should().BeNull();
        problem.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void GeneratedProblem_IsComplete_WhenFullPromptAndExpectedOutput()
    {
        // Act
        var incomplete = new GeneratedProblem
        {
            Id = "p1",
            OneLineStatement = "Test",
            FullPrompt = "Full prompt"
            // ExpectedOutput is null
        };

        var complete = new GeneratedProblem
        {
            Id = "p2",
            OneLineStatement = "Test",
            FullPrompt = "Full prompt",
            ExpectedOutput = "Expected output"
        };

        // Assert
        incomplete.IsComplete.Should().BeFalse();
        complete.IsComplete.Should().BeTrue();
    }

    #endregion

    #region EvalGenPhase Tests

    [Fact]
    public void EvalGenPhase_EnumValues_AreCorrect()
    {
        // Arrange & Act & Assert
        Enum.GetName(typeof(EvalGenPhase), 0).Should().Be("GeneratingCategories");
        Enum.GetName(typeof(EvalGenPhase), 1).Should().Be("GeneratingProblems");
        Enum.GetName(typeof(EvalGenPhase), 2).Should().Be("FleshingOutProblems");
        Enum.GetName(typeof(EvalGenPhase), 3).Should().Be("Completed");
    }

    #endregion

    #region EvalGenProgress Tests

    [Fact]
    public void EvalGenProgress_DefaultValues_AreValid()
    {
        // Arrange & Act
        var progress = new EvalGenProgress();

        // Assert
        progress.CurrentPhase.Should().Be(EvalGenPhase.GeneratingCategories);
        progress.CategoriesGenerated.Should().Be(0);
        progress.TargetCategories.Should().Be(0);
        progress.ProblemsGenerated.Should().Be(0);
        progress.TargetProblems.Should().Be(0);
        progress.ProblemsFleshedOut.Should().Be(0);
        progress.StatusMessage.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0, 0)] // All zeros = 0%
    [InlineData(10, 10, 50, 50, 50, 50, 100)] // All complete = 100%
    [InlineData(5, 10, 25, 50, 25, 50, 50)] // Half complete = 50%
    [InlineData(10, 10, 50, 50, 0, 50, 20)] // Categories+problems done (5%+15%=20%), no flesh-out
    public void EvalGenProgress_OverallProgressPercent_CalculatesCorrectly(
        int catGen, int catTarget, int probGen, int probTarget, int fleshed, int _fleshTarget,
        double expectedPercent)
    {
        // Arrange
        var progress = new EvalGenProgress
        {
            CategoriesGenerated = catGen,
            TargetCategories = catTarget,
            ProblemsGenerated = probGen,
            TargetProblems = probTarget,
            ProblemsFleshedOut = fleshed
        };

        // Act
        var actualPercent = progress.OverallProgressPercent;

        // Assert (allow small floating point tolerance)
        actualPercent.Should().BeApproximately(expectedPercent, 0.1);
    }

    [Fact]
    public void EvalGenProgress_WithValues_StoresCorrectly()
    {
        // Arrange & Act
        var progress = new EvalGenProgress
        {
            CurrentPhase = EvalGenPhase.GeneratingProblems,
            CategoriesGenerated = 5,
            TargetCategories = 10,
            ProblemsGenerated = 20,
            TargetProblems = 50,
            ProblemsFleshedOut = 10,
            StatusMessage = "Generating problems..."
        };

        // Assert
        progress.CurrentPhase.Should().Be(EvalGenPhase.GeneratingProblems);
        progress.CategoriesGenerated.Should().Be(5);
        progress.TargetCategories.Should().Be(10);
        progress.ProblemsGenerated.Should().Be(20);
        progress.TargetProblems.Should().Be(50);
        progress.ProblemsFleshedOut.Should().Be(10);
        progress.StatusMessage.Should().Be("Generating problems...");
    }

    #endregion
}
