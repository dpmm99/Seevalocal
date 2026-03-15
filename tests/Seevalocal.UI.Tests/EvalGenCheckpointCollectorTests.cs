using FluentAssertions;
using Seevalocal.Core.Models;
using Seevalocal.UI.Services;
using Xunit;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Unit tests for EvalGenCheckpointCollector SQLite persistence.
/// </summary>
public class EvalGenCheckpointCollectorTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly string _tempDir;

    public EvalGenCheckpointCollectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eval_gen_collector_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
    }

    public async Task InitializeAsync()
    {
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    private async Task DisposeCollectorAsync(EvalGenCheckpointCollector collector)
    {
        await collector.DisposeAsync();
    }

    #region Initialization Tests

    [Fact]
    public async Task InitializeAsync_CreatesDatabaseTables()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);

        // Act
        await collector.InitializeAsync(CancellationToken.None);

        // Assert - should not throw and database should exist
        File.Exists(_dbPath).Should().BeTrue();
        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task InitializeAsync_MultipleCalls_DoesNotFail()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);

        // Act & Assert - multiple initializations should not fail
        await collector.InitializeAsync(CancellationToken.None);
        await collector.InitializeAsync(CancellationToken.None);
        await collector.InitializeAsync(CancellationToken.None);
        
        await DisposeCollectorAsync(collector);
    }

    #endregion

    #region StartupParameters Tests

    [Fact]
    public async Task SaveStartupParametersAsync_SavesConfig()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        var config = new EvalGenConfig
        {
            Id = "test-id-123",
            RunName = "Test Run",
            OutputDirectoryPath = "./test_output",
            TargetCategoryCount = 15,
            TargetProblemsPerCategory = 8,
            DomainPrompt = "Test domain prompt",
            ContextPrompt = "Test context",
            SystemPrompt = "Test system"
        };
        var judgeConfig = new JudgeConfig
        {
            BaseUrl = "http://localhost:8081",
            Manage = false
        };

        // Act
        await collector.SaveStartupParametersAsync(config, judgeConfig, CancellationToken.None);

        // Assert
        var loaded = await collector.LoadStartupParametersAsync(CancellationToken.None);
        loaded.Should().NotBeNull();
        loaded!.Value.Config.RunName.Should().Be("Test Run");
        loaded.Value.Config.OutputDirectoryPath.Should().Be("./test_output");
        loaded.Value.Config.TargetCategoryCount.Should().Be(15);
        loaded.Value.Config.TargetProblemsPerCategory.Should().Be(8);
        loaded.Value.Config.DomainPrompt.Should().Be("Test domain prompt");
        loaded.Value.Config.ContextPrompt.Should().Be("Test context");
        loaded.Value.Config.SystemPrompt.Should().Be("Test system");
        loaded.Value.Config.ContinueFromCheckpoint.Should().BeTrue();
        loaded.Value.Config.CheckpointDatabasePath.Should().Be(_dbPath);

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task LoadStartupParametersAsync_NoData_ReturnsNull()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);

        // Act
        var loaded = await collector.LoadStartupParametersAsync(CancellationToken.None);

        // Assert
        loaded.Should().BeNull();

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task SaveStartupParametersAsync_OverwritesPrevious()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        var config1 = new EvalGenConfig
        {
            RunName = "First",
            TargetCategoryCount = 5,
            TargetProblemsPerCategory = 3,
            OutputDirectoryPath = "./output1"
        };
        var config2 = new EvalGenConfig
        {
            RunName = "Second",
            TargetCategoryCount = 10,
            TargetProblemsPerCategory = 5,
            OutputDirectoryPath = "./output2"
        };
        var judgeConfig = new JudgeConfig { Manage = false };

        // Act
        await collector.SaveStartupParametersAsync(config1, judgeConfig, CancellationToken.None);
        await collector.SaveStartupParametersAsync(config2, judgeConfig, CancellationToken.None);

        // Assert
        var loaded = await collector.LoadStartupParametersAsync(CancellationToken.None);
        loaded!.Value.Config.RunName.Should().Be("Second");
        loaded.Value.Config.TargetCategoryCount.Should().Be(10);

        await DisposeCollectorAsync(collector);
    }

    #endregion

    #region Category Tests

    [Fact]
    public async Task SaveCategoryAsync_SavesCategory()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        var category = new GeneratedCategory
        {
            Id = "cat-123",
            Name = "Test Category"
        };

        // Act
        await collector.SaveCategoryAsync(category, CancellationToken.None);

        // Assert
        var categories = await collector.LoadCategoriesAsync(CancellationToken.None);
        categories.Should().ContainSingle();
        categories[0].Id.Should().Be("cat-123");
        categories[0].Name.Should().Be("Test Category");

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task SaveCategoryAsync_MultipleCategories_SavesAll()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        var categories = new List<GeneratedCategory>
        {
            new GeneratedCategory { Id = "cat-1", Name = "Category One" },
            new GeneratedCategory { Id = "cat-2", Name = "Category Two" },
            new GeneratedCategory { Id = "cat-3", Name = "Category Three" }
        };

        // Act
        foreach (var cat in categories)
        {
            await collector.SaveCategoryAsync(cat, CancellationToken.None);
        }

        // Assert
        var loaded = await collector.LoadCategoriesAsync(CancellationToken.None);
        loaded.Should().HaveCount(3);
        loaded.Select(c => c.Name).Should().Contain("Category One");
        loaded.Select(c => c.Name).Should().Contain("Category Two");
        loaded.Select(c => c.Name).Should().Contain("Category Three");

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task SaveCategoryAsync_DuplicateId_Overwrites()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        var cat1 = new GeneratedCategory { Id = "cat-1", Name = "Original Name" };
        var cat2 = new GeneratedCategory { Id = "cat-1", Name = "Updated Name" };

        // Act
        await collector.SaveCategoryAsync(cat1, CancellationToken.None);
        await collector.SaveCategoryAsync(cat2, CancellationToken.None);

        // Assert
        var loaded = await collector.LoadCategoriesAsync(CancellationToken.None);
        loaded.Should().ContainSingle();
        loaded[0].Name.Should().Be("Updated Name");

        await DisposeCollectorAsync(collector);
    }

    #endregion

    #region Problem Tests

    [Fact]
    public async Task SaveProblemAsync_SavesIncompleteProblem()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        // First save the parent category
        await collector.SaveCategoryAsync(new GeneratedCategory { Id = "cat-1", Name = "Test Category" }, CancellationToken.None);
        
        var problem = new GeneratedProblem
        {
            Id = "prob-123",
            CategoryId = "cat-1",
            OneLineStatement = "Test problem statement"
        };

        // Act
        await collector.SaveProblemAsync(problem, CancellationToken.None);

        // Assert
        var problems = await collector.LoadProblemsAsync(CancellationToken.None);
        problems.Should().ContainSingle();
        problems[0].Id.Should().Be("prob-123");
        problems[0].CategoryId.Should().Be("cat-1");
        problems[0].OneLineStatement.Should().Be("Test problem statement");
        problems[0].IsComplete.Should().BeFalse();

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task SaveProblemAsync_SavesCompleteProblem()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        // First save the parent category
        await collector.SaveCategoryAsync(new GeneratedCategory { Id = "cat-1", Name = "Test Category" }, CancellationToken.None);
        
        var problem = new GeneratedProblem
        {
            Id = "prob-123",
            CategoryId = "cat-1",
            OneLineStatement = "Test problem",
            FullPrompt = "Full prompt content",
            ExpectedOutput = "Expected output content"
        };

        // Act
        await collector.SaveProblemAsync(problem, CancellationToken.None);

        // Assert
        var problems = await collector.LoadProblemsAsync(CancellationToken.None);
        problems.Should().ContainSingle();
        problems[0].IsComplete.Should().BeTrue();
        problems[0].FullPrompt.Should().Be("Full prompt content");
        problems[0].ExpectedOutput.Should().Be("Expected output content");

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task SaveProblemAsync_MultipleProblems_SavesAll()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        // First save the parent categories
        await collector.SaveCategoryAsync(new GeneratedCategory { Id = "cat-1", Name = "Category 1" }, CancellationToken.None);
        await collector.SaveCategoryAsync(new GeneratedCategory { Id = "cat-2", Name = "Category 2" }, CancellationToken.None);
        
        var problems = new List<GeneratedProblem>
        {
            new GeneratedProblem { Id = "p1", CategoryId = "cat-1", OneLineStatement = "Problem 1" },
            new GeneratedProblem { Id = "p2", CategoryId = "cat-1", OneLineStatement = "Problem 2" },
            new GeneratedProblem { Id = "p3", CategoryId = "cat-2", OneLineStatement = "Problem 3" }
        };

        // Act
        foreach (var problem in problems)
        {
            await collector.SaveProblemAsync(problem, CancellationToken.None);
        }

        // Assert
        var loaded = await collector.LoadProblemsAsync(CancellationToken.None);
        loaded.Should().HaveCount(3);

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task SaveProblemAsync_UpdateProblem_Overwrites()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        // First save the parent category
        await collector.SaveCategoryAsync(new GeneratedCategory { Id = "cat-1", Name = "Test Category" }, CancellationToken.None);
        
        var incomplete = new GeneratedProblem
        {
            Id = "prob-1",
            CategoryId = "cat-1",
            OneLineStatement = "Test problem"
        };
        var complete = new GeneratedProblem
        {
            Id = "prob-1",
            CategoryId = "cat-1",
            OneLineStatement = "Test problem",
            FullPrompt = "Full prompt",
            ExpectedOutput = "Expected output"
        };

        // Act
        await collector.SaveProblemAsync(incomplete, CancellationToken.None);
        await collector.SaveProblemAsync(complete, CancellationToken.None);

        // Assert
        var problems = await collector.LoadProblemsAsync(CancellationToken.None);
        problems.Should().ContainSingle();
        problems[0].IsComplete.Should().BeTrue();
        problems[0].FullPrompt.Should().Be("Full prompt");

        await DisposeCollectorAsync(collector);
    }

    #endregion

    #region Checkpoint Tests

    [Fact]
    public async Task SaveCheckpointAsync_SavesCustomState()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);

        // Act
        await collector.SaveCheckpointAsync("current_phase", "GeneratingProblems", CancellationToken.None);
        await collector.SaveCheckpointAsync("iteration_count", "5", CancellationToken.None);

        // Assert
        var phase = await collector.LoadCheckpointAsync("current_phase", CancellationToken.None);
        var iteration = await collector.LoadCheckpointAsync("iteration_count", CancellationToken.None);

        phase.Should().Be("GeneratingProblems");
        iteration.Should().Be("5");

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task LoadCheckpointAsync_NonExistentKey_ReturnsNull()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);

        // Act
        var value = await collector.LoadCheckpointAsync("non_existent", CancellationToken.None);

        // Assert
        value.Should().BeNull();

        await DisposeCollectorAsync(collector);
    }

    #endregion

    #region Get Counts Tests

    [Fact]
    public async Task GetCompletedProblemCountAsync_NoProblems_ReturnsZero()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);

        // Act
        var count = await collector.GetCompletedProblemCountAsync(CancellationToken.None);

        // Assert
        count.Should().Be(0);

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task GetCompletedProblemCountAsync_WithProblems_ReturnsCorrectCount()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        // First save the parent category
        await collector.SaveCategoryAsync(new GeneratedCategory { Id = "c1", Name = "Test Category" }, CancellationToken.None);
        
        var problems = new List<GeneratedProblem>
        {
            new GeneratedProblem { Id = "p1", CategoryId = "c1", OneLineStatement = "P1", FullPrompt = "F1", ExpectedOutput = "E1" },
            new GeneratedProblem { Id = "p2", CategoryId = "c1", OneLineStatement = "P2" }, // Incomplete
            new GeneratedProblem { Id = "p3", CategoryId = "c1", OneLineStatement = "P3", FullPrompt = "F3", ExpectedOutput = "E3" }
        };

        // Act
        foreach (var p in problems)
        {
            await collector.SaveProblemAsync(p, CancellationToken.None);
        }

        var count = await collector.GetCompletedProblemCountAsync(CancellationToken.None);

        // Assert
        count.Should().Be(2); // Only p1 and p3 are complete

        await DisposeCollectorAsync(collector);
    }

    [Fact]
    public async Task GetProblemCountsByCategoryAsync_ReturnsCorrectCounts()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        // First save the parent categories
        await collector.SaveCategoryAsync(new GeneratedCategory { Id = "cat-1", Name = "Category 1" }, CancellationToken.None);
        await collector.SaveCategoryAsync(new GeneratedCategory { Id = "cat-2", Name = "Category 2" }, CancellationToken.None);
        
        var problems = new List<GeneratedProblem>
        {
            new GeneratedProblem { Id = "p1", CategoryId = "cat-1", OneLineStatement = "P1" },
            new GeneratedProblem { Id = "p2", CategoryId = "cat-1", OneLineStatement = "P2" },
            new GeneratedProblem { Id = "p3", CategoryId = "cat-2", OneLineStatement = "P3" }
        };

        // Act
        foreach (var p in problems)
        {
            await collector.SaveProblemAsync(p, CancellationToken.None);
        }

        var counts = await collector.GetProblemCountsByCategoryAsync(CancellationToken.None);

        // Assert
        counts.Should().ContainKey("cat-1").WhoseValue.Should().Be(2);
        counts.Should().ContainKey("cat-2").WhoseValue.Should().Be(1);

        await DisposeCollectorAsync(collector);
    }

    #endregion

    #region Async Disposal Tests

    [Fact]
    public async Task DisposeAsync_ClosesDatabaseConnection()
    {
        // Arrange
        var collector = new EvalGenCheckpointCollector(_dbPath);
        await collector.SaveCategoryAsync(new GeneratedCategory { Id = "c1", Name = "Test" }, CancellationToken.None);

        // Act
        await collector.DisposeAsync();

        // Assert - should be able to create new collector and access data
        var collector2 = new EvalGenCheckpointCollector(_dbPath);
        var categories = await collector2.LoadCategoriesAsync(CancellationToken.None);
        categories.Should().ContainSingle();
        
        await DisposeCollectorAsync(collector2);
    }

    #endregion
}
