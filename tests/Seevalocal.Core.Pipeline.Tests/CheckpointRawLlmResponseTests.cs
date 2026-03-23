using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace Seevalocal.Core.Pipeline.Tests;

/// <summary>
/// Tests to verify RawLlmResponse is preserved through checkpoint resumption and judge phase.
/// </summary>
public class CheckpointRawLlmResponseTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"checkpoint_test_{Guid.NewGuid()}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch { }
    }

    [Fact]
    public async Task CollectAsync_Saves_RawLlmResponse()
    {
        // Arrange
        var collector = new PersistentResultCollector(_dbPath);
        var result = new EvalResult
        {
            EvalItemId = "test-item-1",
            Succeeded = true,
            RawLlmResponse = "This is the original LLM response from primary phase",
            AllStageOutputs = new Dictionary<string, object?>
            {
                ["PromptStage.response"] = "The answer is 42",
                ["PromptStage.userPrompt"] = "What is 2+2?"
            },
            Metrics = [],
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.5
        };

        // Act
        await collector.CollectAsync(result, default);

        // Assert - read directly from database
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT RawLlmResponse FROM EvalResults WHERE EvalItemId = @id";
        cmd.Parameters.AddWithValue("@id", "test-item-1");
        var rawResponse = await cmd.ExecuteScalarAsync();

        output.WriteLine($"RawLlmResponse from DB after CollectAsync: '{rawResponse}'");
        Assert.NotNull(rawResponse);
        Assert.Equal("This is the original LLM response from primary phase", rawResponse);
    }

    [Fact]
    public async Task CollectJudgeResultAsync_Preserves_RawLlmResponse()
    {
        // Arrange - First save primary phase result
        var collector = new PersistentResultCollector(_dbPath);
        var primaryResult = new EvalResult
        {
            EvalItemId = "test-item-1",
            Succeeded = true,
            RawLlmResponse = "Original LLM response from primary phase",
            AllStageOutputs = new Dictionary<string, object?>
            {
                ["PromptStage.response"] = "The answer is 42",
                ["PromptStage.userPrompt"] = "What is 2+2?"
            },
            Metrics = [],
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.5
        };

        await collector.CollectAsync(primaryResult, default);
        output.WriteLine("Primary phase saved");

        // Verify primary phase saved correctly
        var rawResponseBefore = await GetRawLlmResponse("test-item-1");
        output.WriteLine($"RawLlmResponse before judge: '{rawResponseBefore}'");
        Assert.Equal("Original LLM response from primary phase", rawResponseBefore);

        // Act - Now save judge phase result (with NULL RawLlmResponse, as judge results typically have)
        var judgeResult = new EvalResult
        {
            EvalItemId = "test-item-1",
            Succeeded = true,
            RawLlmResponse = "abc",
            AllStageOutputs = new Dictionary<string, object?>
            {
                ["JudgeStage.rationale"] = "The answer is correct",
                ["JudgeStage.score"] = 10.0
            },
            Metrics =
            [
                new MetricValue { Name = "judgeScore", Value = new MetricScalar.DoubleMetric(10.0) }
            ],
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 2.0
        };

        await collector.CollectJudgeResultAsync(judgeResult, default);
        output.WriteLine("Judge phase saved");

        // Assert - RawLlmResponse should still have original value
        var rawResponseAfter = await GetRawLlmResponse("test-item-1");
        output.WriteLine($"RawLlmResponse after judge: '{rawResponseAfter}'");

        Assert.NotNull(rawResponseAfter);
        Assert.Equal("Original LLM response from primary phase", rawResponseAfter);

        // Also verify phase was updated to judge
        var phase = await GetPhase("test-item-1");
        Assert.Equal("judge", phase);
    }

    [Fact]
    public async Task CollectJudgeResultAsync_WhenPrimaryRawLlmResponseWasNull_RemainsNull()
    {
        // This test documents the current behavior: if primary phase didn't save RawLlmResponse,
        // judge phase can't preserve it

        // Arrange - First save primary phase result WITH NULL RawLlmResponse
        var collector = new PersistentResultCollector(_dbPath);
        var primaryResult = new EvalResult
        {
            EvalItemId = "test-item-1",
            Succeeded = true,
            RawLlmResponse = null,  // Primary phase somehow has NULL
            AllStageOutputs = new Dictionary<string, object?>
            {
                ["PromptStage.response"] = "The answer is 42"
            },
            Metrics = [],
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.5
        };

        await collector.CollectAsync(primaryResult, default);
        output.WriteLine("Primary phase saved with NULL RawLlmResponse");

        // Verify primary phase saved correctly (with NULL)
        var rawResponseBefore = await GetRawLlmResponse("test-item-1");
        output.WriteLine($"RawLlmResponse before judge: '{rawResponseBefore}'");
        Assert.Null(rawResponseBefore);

        // Act - Now save judge phase result
        var judgeResult = new EvalResult
        {
            EvalItemId = "test-item-1",
            Succeeded = true,
            RawLlmResponse = null,
            AllStageOutputs = new Dictionary<string, object?>
            {
                ["JudgeStage.rationale"] = "The answer is correct"
            },
            Metrics = [],
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 2.0
        };

        await collector.CollectJudgeResultAsync(judgeResult, default);

        // Assert - RawLlmResponse remains NULL (can't preserve what wasn't there)
        var rawResponseAfter = await GetRawLlmResponse("test-item-1");
        output.WriteLine($"RawLlmResponse after judge: '{rawResponseAfter}'");
        Assert.Null(rawResponseAfter);
    }

    // Helper methods
    private async Task<string?> GetRawLlmResponse(string evalItemId)
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT RawLlmResponse FROM EvalResults WHERE EvalItemId = @id";
        cmd.Parameters.AddWithValue("@id", evalItemId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    private async Task<string?> GetPhase(string evalItemId)
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Phase FROM EvalResults WHERE EvalItemId = @id";
        cmd.Parameters.AddWithValue("@id", evalItemId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }
}

/// <summary>
/// Test logger that writes to ITestOutputHelper.
/// </summary>
public class TestLogger<T>(ITestOutputHelper output) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
    }
}
