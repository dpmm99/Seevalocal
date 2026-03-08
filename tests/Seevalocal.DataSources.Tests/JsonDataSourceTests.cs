using Xunit;

namespace Seevalocal.DataSources.Tests;

public class JsonDataSourceTests : IDisposable
{
    private readonly string _tempDir;

    public JsonDataSourceTests()
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
    public async Task JsonArray_LoadsItems()
    {
        var path = WriteFile("data.json", """
            [
              { "id": "001", "prompt": "Hello", "expected": "World" },
              { "id": "002", "prompt": "Foo",   "expected": "Bar" }
            ]
            """);

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.JsonFile,
            DataFilePath = path,
        }).Value;
        var items = await TestHelpers.CollectAsync(ds);

        Assert.Equal(2, items.Count);
        Assert.Equal("001", items[0].Id);
        Assert.Equal("Hello", items[0].UserPrompt);
        Assert.Equal("World", items[0].ExpectedOutput);
    }

    [Fact]
    public async Task JsonArray_MissingId_AutoGenerates()
    {
        var path = WriteFile("data.json", """
            [
              { "prompt": "No id here" }
            ]
            """);

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("mysource", new DataSourceConfig
        {
            Kind = DataSourceKind.JsonFile,
            DataFilePath = path,
        }).Value;
        var items = await TestHelpers.CollectAsync(ds);

        _ = Assert.Single(items);
        Assert.Equal("mysource-000000", items[0].Id);
    }

    [Fact]
    public async Task Jsonl_LoadsItems()
    {
        var path = WriteFile("data.jsonl", """
            {"id":"001","prompt":"A","expected":"B"}
            {"id":"002","prompt":"C","expected":"D"}
            """);

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.JsonlFile,
            DataFilePath = path,
        }).Value;
        var items = await TestHelpers.CollectAsync(ds);

        Assert.Equal(2, items.Count);
        Assert.Equal("A", items[0].UserPrompt);
    }

    [Fact]
    public async Task JsonArray_CustomFieldMapping()
    {
        var path = WriteFile("data.json", """
            [{"q":"What?","ans":"That.","cat":"science"}]
            """);

        var factory = new DataSourceFactory(TestHelpers.NullLoggerFactory);
        var ds = factory.Create("test", new DataSourceConfig
        {
            Kind = DataSourceKind.JsonFile,
            DataFilePath = path,
            FieldMapping = new FieldMapping
            {
                UserPromptField = "q",
                ExpectedOutputField = "ans",
                MetadataFields = ["cat"],
            }
        }).Value;
        var items = await TestHelpers.CollectAsync(ds);

        _ = Assert.Single(items);
        Assert.Equal("What?", items[0].UserPrompt);
        Assert.Equal("That.", items[0].ExpectedOutput);
        Assert.Equal("science", items[0].Metadata["cat"]);
    }
}
