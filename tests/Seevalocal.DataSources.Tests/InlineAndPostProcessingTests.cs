using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.DataSources.Tests;

public class InlineDataSourceTests
{
    [Fact]
    public async Task InlineList_LoadsItems()
    {
        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var result = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.InlineList,
            InlineItems =
            [
                new EvalItemDto { Id = "q1", UserPrompt = "What is 2+2?", ExpectedOutput = "4" },
                new EvalItemDto { Id = "q2", UserPrompt = "Name a color." },
            ]
        });
        Assert.True(result.IsSuccess);
        var items = await TestHelpers.CollectAsync(result.Value);

        Assert.Equal(2, items.Count);
        Assert.Equal("q1", items[0].Id);
        Assert.Equal("What is 2+2?", items[0].UserPrompt);
        Assert.Equal("4", items[0].ExpectedOutput);
        Assert.Null(items[1].ExpectedOutput);
    }

    [Fact]
    public async Task InlineList_AutoId_WhenMissing()
    {
        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var result = factory.Create("mysrc", new DataSourceConfig
        {
            Kind = DataSourceKind.InlineList,
            InlineItems = [new EvalItemDto { UserPrompt = "Hello" }]
        });
        var items = await TestHelpers.CollectAsync(result.Value);

        Assert.Equal("mysrc-000000", items[0].Id);
    }

    [Fact]
    public async Task InlineList_DefaultSystemPrompt_Applied()
    {
        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var result = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.InlineList,
            DefaultSystemPrompt = "Be helpful.",
            InlineItems = [new EvalItemDto { UserPrompt = "Hi" }]
        });
        var items = await TestHelpers.CollectAsync(result.Value);

        Assert.Equal("Be helpful.", items[0].SystemPrompt);
    }
}

public class PostProcessingTests : IDisposable
{
    private readonly string _tempDir;

    public PostProcessingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _ = Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task MaxItemCount_LimitsResults()
    {
        var path = Path.Combine(_tempDir, "data.json");
        File.WriteAllText(path, """[{"id":"1","prompt":"a"},{"id":"2","prompt":"b"},{"id":"3","prompt":"c"}]""");

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.JsonFile,
            DataFilePath = path,
            MaxItemCount = 2,
        }).Value;
        var items = await TestHelpers.CollectAsync(ds);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task ShuffleRandomSeed_ProducesConsistentOrder()
    {
        var path = Path.Combine(_tempDir, "data.json");
        File.WriteAllText(path, """[{"id":"1","prompt":"a"},{"id":"2","prompt":"b"},{"id":"3","prompt":"c"}]""");

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var config = new DataSourceConfig
        {
            Kind = DataSourceKind.JsonFile,
            DataFilePath = path,
            ShuffleRandomSeed = 42,
        };
        var ds1 = factory.Create("test", config).Value;
        var ds2 = factory.Create("test", config).Value;

        var items1 = await TestHelpers.CollectAsync(ds1);
        var items2 = await TestHelpers.CollectAsync(ds2);

        Assert.Equal(items1.Select(static i => i.Id), items2.Select(static i => i.Id));
    }

    [Fact]
    public async Task ShuffleRandomSeed_OrderDiffersFromOriginal()
    {
        // With 10 items and seed 0, order should differ from sequential
        var items = Enumerable.Range(0, 10)
            .Select(static i => $"{{\"id\":\"{i}\",\"prompt\":\"p{i}\"}}")
            .ToArray();
        var path = Path.Combine(_tempDir, "big.json");
        File.WriteAllText(path, $"[{string.Join(",", items)}]");

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.JsonFile,
            DataFilePath = path,
            ShuffleRandomSeed = 0,
        }).Value;
        var result = await TestHelpers.CollectAsync(ds);
        var ids = result.Select(static i => i.Id).ToArray();
        var sequential = Enumerable.Range(0, 10).Select(static i => i.ToString()).ToArray();

        Assert.False(ids.SequenceEqual(sequential), "Shuffled order should differ from original");
    }
}
