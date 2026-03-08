using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core.Pipeline;
using Xunit;

namespace Seevalocal.Core.Tests;

public class InMemoryResultCollectorTests
{
    private static EvalResult MakeResult(string id, bool succeeded = true) =>
        new()
        {
            EvalItemId = id,
            EvalSetId = "set1",
            Succeeded = succeeded,
            StartedAt = DateTimeOffset.UtcNow,
            DurationSeconds = 0.5
        };

    [Fact]
    public async Task CollectAsync_SingleResult_Stored()
    {
        var collector = new InMemoryResultCollector(NullLogger<InMemoryResultCollector>.Instance);

        await collector.CollectAsync(MakeResult("item-1"), CancellationToken.None);

        var results = collector.GetResults();
        _ = Assert.Single(results);
        Assert.Equal("item-1", results[0].EvalItemId);
    }

    [Fact]
    public async Task CollectAsync_MultipleResults_AllStored()
    {
        var collector = new InMemoryResultCollector(NullLogger<InMemoryResultCollector>.Instance);

        for (var i = 1; i <= 5; i++)
            await collector.CollectAsync(MakeResult($"item-{i}"), CancellationToken.None);

        Assert.Equal(5, collector.GetResults().Count);
    }

    [Fact]
    public async Task CollectAsync_ConcurrentCalls_AllStored()
    {
        var collector = new InMemoryResultCollector(NullLogger<InMemoryResultCollector>.Instance);

        var tasks = Enumerable.Range(1, 50)
            .Select(i => collector.CollectAsync(MakeResult($"item-{i}"), CancellationToken.None));

        await Task.WhenAll(tasks);

        Assert.Equal(50, collector.GetResults().Count);
    }

    [Fact]
    public async Task FinalizeAsync_DoesNotThrow()
    {
        var collector = new InMemoryResultCollector(NullLogger<InMemoryResultCollector>.Instance);
        await collector.FinalizeAsync(CancellationToken.None); // should complete without error
    }

    [Fact]
    public async Task GetResults_ReturnedBeforeFinalize_ReflectsCurrentState()
    {
        var collector = new InMemoryResultCollector(NullLogger<InMemoryResultCollector>.Instance);
        await collector.CollectAsync(MakeResult("item-1"), CancellationToken.None);

        var snapshot = collector.GetResults();
        _ = Assert.Single(snapshot);
    }
}
