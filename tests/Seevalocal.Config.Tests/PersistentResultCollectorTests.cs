using FluentAssertions;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Xunit;

namespace Seevalocal.Config.Tests;

/// <summary>
/// Tests for PersistentResultCollector to ensure no deadlocks and proper checkpoint functionality.
/// </summary>
public sealed class PersistentResultCollectorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ResolvedConfig _testConfig;

    public PersistentResultCollectorTests()
    {
        // Use a unique temp file for each test
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_checkpoint_{Guid.NewGuid()}.db");

        _testConfig = new ResolvedConfig
        {
            Run = new RunMeta
            {
                RunName = "test-run",
                OutputDirectoryPath = "./results",
            },
            Server = new ServerConfig
            {
                Manage = true,
                BaseUrl = "http://127.0.0.1:8080",
            },
            LlamaServer = new LlamaServerSettings
            {
                ContextWindowTokens = 8192,
            },
        };
    }

    [Fact]
    public async Task SaveStartupParametersAsync_DoesNotDeadlock()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act & Assert - should complete within 2 seconds (deadlock would timeout)
        var saveTask = collector.SaveStartupParametersAsync(_testConfig, cts.Token);
        var completedTask = await Task.WhenAny(saveTask, Task.Delay(TimeSpan.FromSeconds(2)));

        completedTask.Should().Be(saveTask,
            "SaveStartupParametersAsync should not deadlock when calling nested SaveMetadataAsync");
    }

    [Fact]
    public async Task SaveStartupParametersAsync_SavesConfigAndTimestamp()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await collector.SaveStartupParametersAsync(_testConfig, cts.Token);

        // Assert
        var loadedConfig = await collector.LoadStartupParametersAsync(cts.Token);
        loadedConfig.Should().NotBeNull();
        loadedConfig!.Run.RunName.Should().Be("test-run");
        loadedConfig.Run.OutputDirectoryPath.Should().Be("./results");
        loadedConfig.Server.BaseUrl.Should().Be("http://127.0.0.1:8080");
        loadedConfig.LlamaServer.ContextWindowTokens.Should().Be(8192);
    }

    [Fact]
    public async Task SaveStageOutputAsync_SavesAndRetrievesStageOutput()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const string evalItemId = "item-1";
        const string stageName = "PromptStage";
        const string outputKey = "PromptStage.response";  // Production uses prefixed keys
        const string outputValue = "test response content";

        // First create an EvalResult record (required by foreign key)
        var result = new EvalResult
        {
            EvalItemId = evalItemId,
            Succeeded = true,
            Metrics = [],
            AllStageOutputs = new Dictionary<string, object?>(),
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.0,
        };
        await collector.CollectAsync(result, cts.Token);

        // Act
        await collector.SaveStageOutputAsync(evalItemId, stageName, outputKey, outputValue, cts.Token);

        // Assert
        var outputs = await collector.GetStageOutputsAsync(evalItemId, cts.Token);
        outputs.Should().ContainKey(outputKey);  // Key is already prefixed
        outputs[outputKey].Should().Be(outputValue);
    }

    [Fact]
    public async Task SaveStageOutputAsync_SavesComplexObjects()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const string evalItemId = "item-1";
        const string stageName = "JudgeStage";
        const string outputKey = "JudgeStage.score";  // Production uses prefixed keys
        var outputValue = new { score = 8.5, rationale = "Good response" };

        // First create an EvalResult record (required by foreign key)
        var result = new EvalResult
        {
            EvalItemId = evalItemId,
            Succeeded = true,
            Metrics = [],
            AllStageOutputs = new Dictionary<string, object?>(),
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.0,
        };
        await collector.CollectAsync(result, cts.Token);

        // Act
        await collector.SaveStageOutputAsync(evalItemId, stageName, outputKey, outputValue, cts.Token);

        // Assert
        var outputs = await collector.GetStageOutputsAsync(evalItemId, cts.Token);
        outputs.Should().ContainKey(outputKey);  // Key is already prefixed
    }

    [Fact]
    public async Task GetLastCompletedStageAsync_ReturnsLastStage()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const string evalItemId = "item-1";

        // Act - simulate partial progress
        await collector.SavePartialProgressAsync(evalItemId, "PromptStage", cts.Token);

        // Assert
        var lastStage = await collector.GetLastCompletedStageAsync(evalItemId, cts.Token);
        lastStage.Should().Be("PromptStage");
    }

    [Fact]
    public async Task SavePartialProgressAsync_UpdatesLastCompletedStage()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const string evalItemId = "item-1";

        // Act - simulate progressing through stages
        await collector.SavePartialProgressAsync(evalItemId, "PromptStage", cts.Token);
        await collector.SavePartialProgressAsync(evalItemId, "JudgeStage", cts.Token);

        // Assert
        var lastStage = await collector.GetLastCompletedStageAsync(evalItemId, cts.Token);
        lastStage.Should().Be("JudgeStage");
    }

    [Fact]
    public async Task CollectAsync_SetsLastCompletedStageToComplete()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = new EvalResult
        {
            EvalItemId = "item-1",
            Succeeded = true,
            Metrics = [],
            AllStageOutputs = new Dictionary<string, object?>(),
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.5,
        };

        // Act
        await collector.CollectAsync(result, cts.Token);

        // Assert
        var lastStage = await collector.GetLastCompletedStageAsync(result.EvalItemId, cts.Token);
        lastStage.Should().Be("Complete");
    }

    [Fact]
    public async Task CollectJudgeResultAsync_SetsLastCompletedStageToJudgeStage()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // First collect primary result
        var primaryResult = new EvalResult
        {
            EvalItemId = "item-1",
            Succeeded = true,
            Metrics = [],
            AllStageOutputs = new Dictionary<string, object?>(),
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.5,
        };
        await collector.CollectAsync(primaryResult, cts.Token);

        // Then collect judge result
        var judgeResult = new EvalResult
        {
            EvalItemId = "item-1",
            Succeeded = true,
            Metrics = [],
            AllStageOutputs = new Dictionary<string, object?>(),
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 2.0,
        };

        // Act
        await collector.CollectJudgeResultAsync(judgeResult, cts.Token);

        // Assert
        var lastStage = await collector.GetLastCompletedStageAsync(judgeResult.EvalItemId, cts.Token);
        lastStage.Should().Be("JudgeStage");
    }

    [Fact]
    public async Task MultipleConcurrentWrites_DoesNotDeadlock()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tasks = new List<Task>();

        // Act - fire off multiple concurrent writes
        for (int i = 0; i < 10; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                await collector.SaveStageOutputAsync(
                    $"item-{index}",
                    $"Stage-{index}",
                    "key",
                    $"value-{index}",
                    cts.Token);
            }));
        }

        // Assert - should all complete without deadlock
        var timeoutTask = Task.WhenAll(tasks);
        var completedTask = await Task.WhenAny(timeoutTask, Task.Delay(TimeSpan.FromSeconds(5)));

        completedTask.Should().Be(timeoutTask,
            "Concurrent writes should not cause deadlock and should complete within 5 seconds");
    }

    [Fact]
    public async Task FinalizeAsync_SetsFinalizedCheckpoint()
    {
        // Arrange - use a fresh database for this test
        var freshDbPath = Path.Combine(Path.GetTempPath(), $"test_checkpoint_final_{Guid.NewGuid()}.db");
        try
        {
            await using var collector = new PersistentResultCollector(freshDbPath);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Act
            await collector.FinalizeAsync(cts.Token);

            // Assert
            var finalized = await collector.LoadCheckpointAsync("finalized", cts.Token);
            finalized.Should().Be("true");
        }
        finally
        {
            // Clean up
            try { if (File.Exists(freshDbPath)) File.Delete(freshDbPath); } catch { }
        }
    }

    [Fact]
    public async Task GetResultsForPhaseAsync_LoadsStageOutputsFromRelationalTable()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const string evalItemId = "item-1";

        // Create a result with empty AllStageOutputs (simulating what CollectAsync does now)
        var result = new EvalResult
        {
            EvalItemId = evalItemId,
            Succeeded = true,
            FailureReason = null,
            Metrics = [],
            AllStageOutputs = new Dictionary<string, object?>(),  // Empty - stage outputs are in relational table
            RawLlmResponse = "test response",
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.5,
        };
        await collector.CollectAsync(result, cts.Token);

        // Save stage outputs to the relational table (using prefixed keys like production)
        await collector.SaveStageOutputAsync(evalItemId, "PromptStage", "PromptStage.userPrompt", "What is 2+2?", cts.Token);
        await collector.SaveStageOutputAsync(evalItemId, "PromptStage", "PromptStage.response", "2+2=4", cts.Token);
        await collector.SaveStageOutputAsync(evalItemId, "PromptStage", "PromptStage.expectedOutput", "4", cts.Token);
        await collector.SaveStageOutputAsync(evalItemId, "JudgeStage", "JudgeStage.score", 9.5, cts.Token);
        await collector.SaveStageOutputAsync(evalItemId, "JudgeStage", "JudgeStage.rationale", "Good answer", cts.Token);

        // Act
        var results = await collector.GetResultsForPhaseAsync("primary", cts.Token);

        // Assert
        results.Should().HaveCount(1);
        var loadedResult = results[0];
        loadedResult.EvalItemId.Should().Be(evalItemId);
        loadedResult.RawLlmResponse.Should().Be("test response");

        // Stage outputs should be loaded from the relational table
        loadedResult.AllStageOutputs.Should().ContainKey("PromptStage.userPrompt");
        loadedResult.AllStageOutputs.Should().ContainKey("PromptStage.response");
        loadedResult.AllStageOutputs.Should().ContainKey("PromptStage.expectedOutput");
        loadedResult.AllStageOutputs.Should().ContainKey("JudgeStage.score");
        loadedResult.AllStageOutputs.Should().ContainKey("JudgeStage.rationale");

        loadedResult.AllStageOutputs["PromptStage.userPrompt"].Should().Be("What is 2+2?");
        loadedResult.AllStageOutputs["PromptStage.response"].Should().Be("2+2=4");
        loadedResult.AllStageOutputs["PromptStage.expectedOutput"].Should().Be(4);  // Deserialized as int
        loadedResult.AllStageOutputs["JudgeStage.score"].Should().Be(9.5);
        loadedResult.AllStageOutputs["JudgeStage.rationale"].Should().Be("Good answer");
    }

    [Fact]
    public async Task GetResultsForPhaseAsync_LoadsMultipleItemsWithStageOutputs()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Create multiple results
        for (int i = 1; i <= 3; i++)
        {
            var result = new EvalResult
            {
                EvalItemId = $"item-{i}",
                Succeeded = true,
                Metrics = [],
                AllStageOutputs = new Dictionary<string, object?>(),
                StartedAt = DateTimeOffset.UtcNow,
                DurationSeconds = 1.0,
            };
            await collector.CollectAsync(result, cts.Token);

            // Save different stage outputs for each item (using prefixed keys like production)
            await collector.SaveStageOutputAsync($"item-{i}", "PromptStage", $"PromptStage.response", $"response-{i}", cts.Token);
            await collector.SaveStageOutputAsync($"item-{i}", "PromptStage", $"PromptStage.itemNumber", i, cts.Token);
        }

        // Act
        var results = await collector.GetResultsForPhaseAsync("primary", cts.Token);

        // Assert
        results.Should().HaveCount(3);

        for (int i = 1; i <= 3; i++)
        {
            var loadedResult = results.First(r => r.EvalItemId == $"item-{i}");
            loadedResult.AllStageOutputs.Should().ContainKey("PromptStage.response");
            loadedResult.AllStageOutputs.Should().ContainKey("PromptStage.itemNumber");
            loadedResult.AllStageOutputs["PromptStage.response"].Should().Be($"response-{i}");
            loadedResult.AllStageOutputs["PromptStage.itemNumber"].Should().Be(i);  // Deserialized as int
        }
    }

    [Fact]
    public async Task SaveServerBinaryPathAsync_SavesAndLoadsBinaryPath()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const string serverType = "primary";
        const string binaryPath = "C:\\cache\\llama-server\\b8184\\win\\llama-server.exe";

        // Act
        await collector.SaveServerBinaryPathAsync(serverType, binaryPath, cts.Token);

        // Assert
        var loadedPath = await collector.LoadServerBinaryPathAsync(serverType, cts.Token);
        loadedPath.Should().Be(binaryPath);
    }

    [Fact]
    public async Task SaveServerBinaryPathAsync_SavesSeparatePathsForPrimaryAndJudge()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const string primaryPath = "C:\\cache\\llama-server\\b8184\\win\\llama-server.exe";
        const string judgePath = "C:\\cache\\llama-server\\b8184\\win\\llama-server-judge.exe";

        // Act
        await collector.SaveServerBinaryPathAsync("primary", primaryPath, cts.Token);
        await collector.SaveServerBinaryPathAsync("judge", judgePath, cts.Token);

        // Assert
        var loadedPrimaryPath = await collector.LoadServerBinaryPathAsync("primary", cts.Token);
        var loadedJudgePath = await collector.LoadServerBinaryPathAsync("judge", cts.Token);

        loadedPrimaryPath.Should().Be(primaryPath);
        loadedJudgePath.Should().Be(judgePath);
        loadedPrimaryPath.Should().NotBe(loadedJudgePath);
    }

    [Fact]
    public async Task StageOutputsTable_HandlesNullValues()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const string evalItemId = "item-1";

        var result = new EvalResult
        {
            EvalItemId = evalItemId,
            Succeeded = true,
            Metrics = [],
            AllStageOutputs = new Dictionary<string, object?>(),
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.0,
        };
        await collector.CollectAsync(result, cts.Token);

        // Act - save null value (using prefixed key like production)
        await collector.SaveStageOutputAsync(evalItemId, "TestStage", "TestStage.nullableKey", null, cts.Token);

        // Assert
        var outputs = await collector.GetStageOutputsAsync(evalItemId, cts.Token);
        outputs.Should().ContainKey("TestStage.nullableKey");
        outputs["TestStage.nullableKey"].Should().BeNull();
    }

    [Fact]
    public async Task StageOutputsTable_HandlesComplexJsonObjects()
    {
        // Arrange
        await using var collector = new PersistentResultCollector(_dbPath);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const string evalItemId = "item-1";

        var result = new EvalResult
        {
            EvalItemId = evalItemId,
            Succeeded = true,
            Metrics = [],
            AllStageOutputs = new Dictionary<string, object?>(),
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 1.0,
        };
        await collector.CollectAsync(result, cts.Token);

        var complexObject = new { name = "test", values = new[] { 1, 2, 3 }, nested = new { key = "value" } };

        // Act (using prefixed key like production)
        await collector.SaveStageOutputAsync(evalItemId, "TestStage", "TestStage.complexKey", complexObject, cts.Token);

        // Assert
        var outputs = await collector.GetStageOutputsAsync(evalItemId, cts.Token);
        outputs.Should().ContainKey("TestStage.complexKey");

        // The value should be deserialized as JSON
        var loadedValue = outputs["TestStage.complexKey"];
        loadedValue.Should().NotBeNull();
    }

    public void Dispose()
    {
        // Clean up temp file
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
