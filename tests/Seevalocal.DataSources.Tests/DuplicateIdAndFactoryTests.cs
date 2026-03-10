using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.DataSources.Tests;

public class DuplicateIdTests : IDisposable
{
    private readonly string _tempDir;

    public DuplicateIdTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _ = Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task DuplicateIds_ThrowsInvalidDataException()
    {
        var path = Path.Combine(_tempDir, "dup.json");
        File.WriteAllText(path, """
            [
              { "id": "dup", "prompt": "A" },
              { "id": "dup", "prompt": "B" }
            ]
            """);

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.JsonFile,
            DataFilePath = path,
        }).Value;

        _ = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await TestHelpers.CollectAsync(ds));
    }
}

public class DataSourceFactoryTests : IDisposable
{
    private readonly string _tempDir;

    public DataSourceFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _ = Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Create_JsonFile_Succeeds()
    {
        var path = Path.Combine(_tempDir, "data.json");
        File.WriteAllText(path, "[]");
        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var result = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.JsonFile,
            DataFilePath = path,
        });
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_MissingFile_ReturnsFailure()
    {
        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var result = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.JsonFile,
            DataFilePath = "/nonexistent/path.json",
        });
        Assert.True(result.IsFailed);
    }

    [Fact]
    public void Create_InlineList_CorrectImplementation()
    {
        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var result = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.InlineList,
            InlineItems = [new EvalItemDto { Id = "q1", UserPrompt = "Hello?" }]
        });
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_Directory_MissingPath_ReturnsFailure()
    {
        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var result = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.Directory,
            PromptDirectoryPath = "/nonexistent",
        });
        Assert.True(result.IsFailed);
    }

    [Theory]
    [InlineData(DataSourceKind.JsonFile)]
    [InlineData(DataSourceKind.JsonlFile)]
    [InlineData(DataSourceKind.YamlFile)]
    [InlineData(DataSourceKind.CsvFile)]
    public void Create_FileKinds_WithValidFile_ReturnsSuccess(DataSourceKind kind)
    {
        var ext = kind switch
        {
            DataSourceKind.JsonFile => ".json",
            DataSourceKind.JsonlFile => ".jsonl",
            DataSourceKind.YamlFile => ".yaml",
            DataSourceKind.CsvFile => ".csv",
            _ => ".dat"
        };
        var path = Path.Combine(_tempDir, $"data{ext}");
        File.WriteAllText(path, "");

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var result = factory.Create("test", new DataSourceConfig
        {
            Kind = kind,
            DataFilePath = path,
        });
        Assert.True(result.IsSuccess);
    }
}
