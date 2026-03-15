using FluentAssertions;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Pipeline;
using Seevalocal.UI.Services;
using Seevalocal.UI.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Integration test that loads a checkpoint database and validates the wizard can proceed through all steps.
/// </summary>
public class CheckpointWizardIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _checkpointDbPath = @"C:\DePro\CodeProjects\CSharp\Seevalocal\src\Seevalocal.UI\bin\Debug\net10.0\results\.txt2sqltestprog_checkpoint.db";
    //Yes, I asked for it to use this hard-coded file. It's a temporary test; we'll figure out a better way later. :)

    public CheckpointWizardIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CheckpointLoad_WizardValidates_ThroughAllSteps()
    {
        // Arrange
        var filePicker = new TestFilePickerService();
        var toastService = new TestToastService();
        var logger = new TestLogger(_output);

        var wizard = new WizardViewModel(filePicker, toastService, logger);

        // Load checkpoint config
        var collector = new PersistentResultCollector(_checkpointDbPath);
        var checkpointConfig = await collector.LoadStartupParametersAsync(default);

        _output.WriteLine($"Loaded checkpoint config: {checkpointConfig != null}");
        if (checkpointConfig != null)
        {
            _output.WriteLine($"  Server.Manage: {checkpointConfig.Server.Manage}");
            _output.WriteLine($"  Server.Model: {checkpointConfig.Server.Model?.Kind}");
            _output.WriteLine($"  Judge.Enable: {checkpointConfig.Judge?.Enable}");
            _output.WriteLine($"  Judge.Manage: {checkpointConfig.Judge?.Manage}");
            _output.WriteLine($"  Judge.Model: {checkpointConfig.Judge?.ServerConfig?.Model?.Kind}");
        }

        // Act - populate wizard from checkpoint
        if (checkpointConfig != null)
        {
            // Use reflection to call the private PopulateFromCheckpointConfig method
            var method = typeof(WizardViewModel).GetMethod("PopulateFromCheckpointConfig",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(wizard, [checkpointConfig]);
        }

        // Assert - validate each step
        _output.WriteLine("\n=== Validating Wizard Steps ===");

        // Step 1: Continue Run
        wizard.CurrentStep = WizardStepKind.ContinueRun;
        var continueRunErrors = wizard.ValidateCurrentStep();
        _output.WriteLine($"Continue Run errors: {continueRunErrors.Count}");
        foreach (var error in continueRunErrors) _output.WriteLine($"  - {error}");
        continueRunErrors.Should().BeEmpty("Continue Run step should validate");

        // Step 2: Model and Server
        wizard.CurrentStep = WizardStepKind.ModelAndServer;
        var serverErrors = wizard.ValidateCurrentStep();
        _output.WriteLine($"\nModel and Server errors: {serverErrors.Count}");
        foreach (var error in serverErrors) _output.WriteLine($"  - {error}");
        serverErrors.Should().BeEmpty("Model and Server step should validate after checkpoint load");

        // Step 3: Dataset
        wizard.CurrentStep = WizardStepKind.EvaluationDataset;
        var datasetErrors = wizard.ValidateCurrentStep();
        _output.WriteLine($"\nDataset errors: {datasetErrors.Count}");
        foreach (var error in datasetErrors) _output.WriteLine($"  - {error}");
        datasetErrors.Should().BeEmpty("Dataset step should validate after checkpoint load");

        // Step 4: Scoring
        wizard.CurrentStep = WizardStepKind.Scoring;
        var scoringErrors = wizard.ValidateCurrentStep();
        _output.WriteLine($"\nScoring errors: {scoringErrors.Count}");
        foreach (var error in scoringErrors) _output.WriteLine($"  - {error}");
        scoringErrors.Should().BeEmpty("Scoring step should validate after checkpoint load");

        // Step 5: Review (no validation, just build config)
        wizard.CurrentStep = WizardStepKind.ReviewAndRun;

        // Build config and validate with ConfigValidator
        var partialConfig = wizard.BuildPartialConfig();
        _output.WriteLine($"\n=== Built PartialConfig ===");
        _output.WriteLine($"Server: {partialConfig.Server != null}");
        _output.WriteLine($"Server.Manage: {partialConfig.Server?.Manage}");
        _output.WriteLine($"Server.Model: {partialConfig.Server?.Model?.Kind.ToString() ?? "null"}");
        _output.WriteLine($"Server.Model.FilePath: {partialConfig.Server?.Model?.FilePath ?? "null"}");
        _output.WriteLine($"Judge: {partialConfig.Judge != null}");
        _output.WriteLine($"Judge.Enable: {partialConfig.Judge?.Enable}");
        _output.WriteLine($"Judge.ServerConfig.Manage: {partialConfig.Judge?.ServerConfig?.Manage}");
        _output.WriteLine($"Judge.Model: {partialConfig.Judge?.ServerConfig?.Model?.Kind.ToString() ?? "null"}");
        _output.WriteLine($"Judge.Model.FilePath: {partialConfig.Judge?.ServerConfig?.Model?.FilePath ?? "null"}");

        // The key assertion: config should have model info if Manage is true
        if (partialConfig.Server?.Manage == true)
        {
            partialConfig.Server.Model.Should().NotBeNull("server.manage is true but model is null");
        }

        if (partialConfig.Judge?.ServerConfig?.Manage == true)
        {
            partialConfig.Judge.ServerConfig.Model.Should().NotBeNull("judge.manage is true but model is null");
        }

        // Note: CheckpointDatabasePath may not be set if the original run didn't specify one
        // (it's generated from run name by default)
    }

    [Fact]
    public async Task CheckpointDatabase_ContainsCompletedItems()
    {
        // Arrange
        var collector = new PersistentResultCollector(_checkpointDbPath);

        // First, let's discover what eval set IDs exist in the database
        var allEvalSetIds = await GetAllEvalSetIdsAsync(collector);
        _output.WriteLine($"\n=== Eval Set IDs in Database ===");
        foreach (var id in allEvalSetIds)
        {
            _output.WriteLine($"  {id}");
        }

        // Act - get completed item IDs for each eval set
        foreach (var evalSetId in allEvalSetIds)
        {
            var primaryCompletedIds = await collector.GetCompletedItemIdsAsync(evalSetId, "primary", default);
            var judgeCompletedIds = await collector.GetCompletedItemIdsAsync(evalSetId, "judge", default);

            // Assert
            _output.WriteLine($"\n=== Checkpoint Database Contents for '{evalSetId}' ===");
            _output.WriteLine($"Primary phase completed items: {primaryCompletedIds.Count}");
            _output.WriteLine($"Judge phase completed items: {judgeCompletedIds.Count}");

            foreach (var id in primaryCompletedIds.Take(5))
            {
                _output.WriteLine($"  Primary completed: {id}");
            }
            if (primaryCompletedIds.Count > 5)
            {
                _output.WriteLine($"  ... and {primaryCompletedIds.Count - 5} more");
            }

            // At least one eval set should have completed items
            if (primaryCompletedIds.Count > 0)
            {
                return; // Test passes
            }
        }

        // If we get here, no eval sets had completed items
        throw new Xunit.Sdk.XunitException("No eval sets in checkpoint database have completed items");
    }

    private async Task<List<string>> GetAllEvalSetIdsAsync(PersistentResultCollector collector)
    {
        var evalSetIds = new List<string>();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_checkpointDbPath}");
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT EvalSetId FROM EvalResults";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            evalSetIds.Add(reader.GetString(0));
        }

        return evalSetIds;
    }
}

/// <summary>
/// Test implementation of IFilePickerService that returns predefined paths.
/// </summary>
public class TestFilePickerService : IFilePickerService
{
    public Task<string?> ShowOpenFileDialogAsync(string title, string? filters = null, string? initialDirectory = null)
        => Task.FromResult<string?>(null);

    public Task<string?> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null)
        => Task.FromResult<string?>(null);

    public Task<string?> ShowSaveFileDialogAsync(string title, string? filters = null, string? initialFileName = null)
        => Task.FromResult<string?>(null);
}

/// <summary>
/// Test implementation of IToastService.
/// </summary>
public class TestToastService : IToastService
{
    public void Show(string message, int duration = 3000) { }
    public void ShowError(string message, int duration = 5000) { }
    public void ShowSuccess(string message, int duration = 3000) { }
}

/// <summary>
/// Test logger that writes to ITestOutputHelper.
/// </summary>
public class TestLogger : ILogger<WizardViewModel>
{
    private readonly ITestOutputHelper _output;

    public TestLogger(ITestOutputHelper output)
    {
        _output = output;
    }

    public IDisposable? BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
    }
}
