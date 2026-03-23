using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.DataSources.Tests;

public class DirectoryDataSourceTests : IDisposable
{
    private readonly string _tempDir;

    public DirectoryDataSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _ = Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string CreatePromptDir(params (string name, string content)[] files)
    {
        var dir = Path.Combine(_tempDir, "prompts");
        _ = Directory.CreateDirectory(dir);
        foreach ((var name, var content) in files)
            File.WriteAllText(Path.Combine(dir, name), content);
        return dir;
    }

    private string CreateExpectedDir(params (string name, string content)[] files)
    {
        var dir = Path.Combine(_tempDir, "expected");
        _ = Directory.CreateDirectory(dir);
        foreach ((var name, var content) in files)
            File.WriteAllText(Path.Combine(dir, name), content);
        return dir;
    }

    [Fact]
    public async Task SingleDirectory_LoadsItems()
    {
        var promptDir = CreatePromptDir(
            ("001.txt", "Hello"),
            ("002.txt", "World")
        );

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var config = new DataSourceConfig
        {
            Kind = DataSourceKind.SplitDirectories,
            PromptDirectory = promptDir,
        };
        var ds = factory.Create("test", config).Value;
        var items = await TestHelpers.CollectAsync(ds);

        Assert.Equal(2, items.Count);
        Assert.Equal("Hello", items[0].UserPrompt);
        Assert.Equal("World", items[1].UserPrompt);
        Assert.Equal("001", items[0].Id);
        Assert.Equal("002", items[1].Id);
    }

    [Fact]
    public async Task SplitDirectories_MatchesByFileStem()
    {
        var promptDir = CreatePromptDir(
            ("001.txt", "Prompt A"),
            ("002.txt", "Prompt B")
        );
        var expectedDir = CreateExpectedDir(
            ("001.txt", "Expected A")
        // 002 intentionally missing
        );

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var config = new DataSourceConfig
        {
            Kind = DataSourceKind.SplitDirectories,
            PromptDirectory = promptDir,
            ExpectedDirectory = expectedDir,
        };
        var ds = factory.Create("test", config).Value;
        var items = await TestHelpers.CollectAsync(ds);

        Assert.Equal(2, items.Count);
        Assert.Equal("Expected A", items[0].ExpectedOutput);
        Assert.Null(items[1].ExpectedOutput);
    }

    [Fact]
    public async Task SystemPromptFile_AppliedToAll()
    {
        var promptDir = CreatePromptDir(("001.txt", "Q"));
        var sysFile = Path.Combine(_tempDir, "system.txt");
        File.WriteAllText(sysFile, "You are an assistant.");

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var config = new DataSourceConfig
        {
            Kind = DataSourceKind.SplitDirectories,
            PromptDirectory = promptDir,
            SystemPromptFilePath = sysFile,
        };
        var ds = factory.Create("test", config).Value;
        var items = await TestHelpers.CollectAsync(ds);

        _ = Assert.Single(items);
        Assert.Equal("You are an assistant.", items[0].SystemPrompt);
    }

    [Fact]
    public async Task FileExtensionFilter_OnlyMatchingFiles()
    {
        var promptDir = CreatePromptDir(
            ("a.txt", "txt"),
            ("b.md", "md"),
            ("c.txt", "txt2")
        );

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var config = new DataSourceConfig
        {
            Kind = DataSourceKind.SplitDirectories,
            PromptDirectory = promptDir,
            FileExtensionFilter = "*.txt",
        };
        var ds = factory.Create("test", config).Value;
        var items = await TestHelpers.CollectAsync(ds);

        Assert.Equal(2, items.Count);
        Assert.All(items, static i => Assert.True(i.UserPrompt is "txt" or "txt2"));
    }

    [Fact]
    public async Task GetCountAsync_ReturnsCorrectCount()
    {
        var promptDir = CreatePromptDir(("a.txt", "x"), ("b.txt", "y"), ("c.txt", "z"));

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var config = new DataSourceConfig
        {
            Kind = DataSourceKind.SplitDirectories,
            PromptDirectory = promptDir,
        };
        var ds = factory.Create("test", config).Value;
        var count = await ds.GetCountAsync(CancellationToken.None);

        Assert.Equal(3, count);
    }
}
