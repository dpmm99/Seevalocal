using Xunit;

namespace Seevalocal.DataSources.Tests;

public class CsvDataSourceTests : IDisposable
{
    private readonly string _tempDir;

    public CsvDataSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _ = Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Csv_HeaderMapping_DefaultFields()
    {
        var path = WriteFile("data.csv", """
            id,prompt,expected
            001,Hello,World
            002,Foo,Bar
            """);

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.CsvFile,
            DataFilePath = path,
        }).Value;
        var items = await TestHelpers.CollectAsync(ds);

        Assert.Equal(2, items.Count);
        Assert.Equal("001", items[0].Id);
        Assert.Equal("Hello", items[0].UserPrompt);
        Assert.Equal("World", items[0].ExpectedOutput);
    }

    [Fact]
    public async Task Csv_CustomFieldMapping()
    {
        var path = WriteFile("data.csv", """
            question,answer,category
            "What is 2+2?","4","math"
            """);

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.CsvFile,
            DataFilePath = path,
            FieldMapping = new FieldMapping
            {
                UserPromptField = "question",
                ExpectedOutputField = "answer",
                MetadataFields = ["category"],
            }
        }).Value;
        var items = await TestHelpers.CollectAsync(ds);

        _ = Assert.Single(items);
        Assert.Equal("What is 2+2?", items[0].UserPrompt);
        Assert.Equal("4", items[0].ExpectedOutput);
        Assert.Equal("math", items[0].Metadata["category"]);
    }

    [Fact]
    public async Task Csv_UnicodeContent()
    {
        var path = WriteFile("data.csv", "id,prompt,expected\n001,\"Café\",\"Café\"");

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.CsvFile,
            DataFilePath = path,
        }).Value;
        var items = await TestHelpers.CollectAsync(ds);

        _ = Assert.Single(items);
        Assert.Equal("Café", items[0].UserPrompt);
    }

    [Fact]
    public async Task Csv_MissingPromptColumn_ThrowsOnEnumeration()
    {
        var path = WriteFile("data.csv", "id,answer\n001,x");

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.CsvFile,
            DataFilePath = path,
        }).Value;

        _ = await Assert.ThrowsAsync<InvalidDataException>(async () => await TestHelpers.CollectAsync(ds));
    }
}
