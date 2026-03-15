using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Seevalocal.Core.Models;
using Seevalocal.UI.Services;
using Seevalocal.UI.ViewModels;
using Xunit;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Unit tests for EvalGenRunViewModel.
/// </summary>
public class EvalGenRunViewModelTests : IAsyncLifetime
{
    private readonly IEvalGenService _evalGenService;
    private readonly ILogger<EvalGenRunViewModel> _logger;
    private readonly EvalGenRunViewModel _viewModel;

    public EvalGenRunViewModelTests()
    {
        _evalGenService = Substitute.For<IEvalGenService>();
        _logger = NullLogger<EvalGenRunViewModel>.Instance;
        _viewModel = new EvalGenRunViewModel(_evalGenService, _logger);
    }

    public async Task InitializeAsync()
    {
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _viewModel.DisposeAsync();
    }

    #region Initial State Tests

    [Fact]
    public void Constructor_InitialState_IsCorrect()
    {
        // Assert
        _viewModel.IsRunning.Should().BeFalse();
        _viewModel.IsPaused.Should().BeFalse();
        _viewModel.IsCompleted.Should().BeFalse();
        _viewModel.IsCancelled.Should().BeFalse();
        _viewModel.ProgressPercent.Should().Be(0);
        _viewModel.StatusLine.Should().Be("Ready");
        _viewModel.Error.Should().BeNull();
    }

    [Fact]
    public void Constructor_Commands_AreNotNull()
    {
        // Assert
        _viewModel.PauseCommand.Should().NotBeNull();
        _viewModel.CancelCommand.Should().NotBeNull();
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_ValidConfig_SetsRunningState()
    {
        // Arrange
        var config = new EvalGenConfig
        {
            RunName = "TestRun",
            OutputDirectoryPath = "./test",
            TargetCategoryCount = 5,
            TargetProblemsPerCategory = 3
        };

        var run = CreateMockRun(config);
        _evalGenService.GenerateAsync(config, Arg.Any<JudgeConfig?>(), Arg.Any<CancellationToken>())
            .Returns(run);

        // Act
        await _viewModel.StartAsync(config, null, CancellationToken.None);

        // Assert - run should be started
        _viewModel.RunName.Should().Be("TestRun");
        _viewModel.TargetCategories.Should().Be(5);
        _viewModel.TargetProblems.Should().Be(15);
    }

    #endregion

    #region Pause/Resume Tests

    [Fact]
    public void PauseCommand_WhenRunning_CanExecute()
    {
        // Arrange - simulate running state
        SetViewModelRunning();

        // Assert
        _viewModel.PauseCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void PauseCommand_WhenNotRunning_CannotExecute()
    {
        // Assert
        _viewModel.PauseCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CancelCommand_WhenRunning_CanExecute()
    {
        // Arrange
        SetViewModelRunning();

        // Assert
        _viewModel.CancelCommand.CanExecute(null).Should().BeTrue();
    }

    #endregion

    #region Progress Update Tests

    [Fact]
    public void RefreshProgress_WithNullRun_DoesNotThrow()
    {
        // Arrange & Act - RefreshProgress with null run should not throw
        var action = () => _viewModel.RefreshProgress();

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region ContinueFromCheckpoint Tests

    [Fact]
    public async Task ContinueFromCheckpointAsync_NoCheckpoint_SetsError()
    {
        // Arrange
        var tempDb = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.db");

        // Act
        await _viewModel.ContinueFromCheckpointAsync(tempDb, CancellationToken.None);

        // Assert
        _viewModel.Error.Should().Contain("No checkpoint found");
    }

    #endregion

    #region Property Change Tests

    [Fact]
    public void PropertyChanged_FiresForProperties()
    {
        // Arrange
        var propertyChanged = new List<string>();
        _viewModel.PropertyChanged += (_, e) => propertyChanged.Add(e.PropertyName!);

        // Act - trigger a property change by setting a property directly
        typeof(EvalGenRunViewModel).GetProperty(nameof(EvalGenRunViewModel.StatusLine))!
            .SetValue(_viewModel, "Test status");

        // Assert - should have received property change notification
        propertyChanged.Should().Contain(nameof(EvalGenRunViewModel.StatusLine));
    }

    #endregion

    private EvalGenRun CreateMockRun(EvalGenConfig config)
    {
        var run = new EvalGenRun(config, async _ =>
        {
            await Task.Delay(10);
        });
        run.Start();
        return run;
    }

    private void SetViewModelRunning()
    {
        // Use reflection to set IsRunning for testing command CanExecute
        typeof(EvalGenRunViewModel).GetProperty(nameof(EvalGenRunViewModel.IsRunning))!
            .SetValue(_viewModel, true);
    }
}
