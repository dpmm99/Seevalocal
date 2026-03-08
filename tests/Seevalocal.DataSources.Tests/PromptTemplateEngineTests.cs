using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.DataSources.Tests;

public class PromptTemplateEngineTests
{
    private static EvalItem MakeItem(string id = "001", string prompt = "Hello", string? expected = null,
        Dictionary<string, string>? meta = null)
        => new()
        {
            Id = id,
            UserPrompt = prompt,
            ExpectedOutput = expected,
            Metadata = meta ?? [],
        };

    [Fact]
    public void NoTemplates_ReturnsUnchanged()
    {
        var engine = new PromptTemplateEngine();
        var item = MakeItem();
        var config = new DataSourceConfig();
        var result = PromptTemplateEngine.Apply(item, config);
        Assert.Same(item, result); // record equality but reference-same if no change
    }

    [Fact]
    public void PromptTemplate_SubstitutesPrompt()
    {
        var engine = new PromptTemplateEngine();
        var item = MakeItem(prompt: "bonjour");
        var config = new DataSourceConfig
        {
            PromptTemplate = "Translate: {prompt}\nTranslation:"
        };
        var result = PromptTemplateEngine.Apply(item, config);
        Assert.Equal("Translate: bonjour\nTranslation:", result.UserPrompt);
    }

    [Fact]
    public void PromptTemplate_SavesOriginalToMetadata()
    {
        var engine = new PromptTemplateEngine();
        var item = MakeItem(prompt: "original");
        var config = new DataSourceConfig { PromptTemplate = "Wrapped: {prompt}" };
        var result = PromptTemplateEngine.Apply(item, config);
        Assert.Equal("original", result.Metadata["originalPrompt"]);
    }

    [Fact]
    public void PromptTemplate_SubstitutesIdAndExpected()
    {
        var engine = new PromptTemplateEngine();
        var item = MakeItem(id: "item-42", prompt: "Q", expected: "A");
        var config = new DataSourceConfig
        {
            PromptTemplate = "[{id}] {prompt} / {expected}"
        };
        var result = PromptTemplateEngine.Apply(item, config);
        Assert.Equal("[item-42] Q / A", result.UserPrompt);
    }

    [Fact]
    public void PromptTemplate_SubstitutesMetadataVariables()
    {
        var engine = new PromptTemplateEngine();
        var item = MakeItem(prompt: "text", meta: new Dictionary<string, string> { ["lang"] = "fr" });
        var config = new DataSourceConfig
        {
            PromptTemplate = "Translate to {meta.lang}: {prompt}"
        };
        var result = PromptTemplateEngine.Apply(item, config);
        Assert.Equal("Translate to fr: text", result.UserPrompt);
    }

    [Fact]
    public void PromptTemplate_MissingPlaceholder_Throws()
    {
        var engine = new PromptTemplateEngine();
        var item = MakeItem();
        var config = new DataSourceConfig { PromptTemplate = "No placeholder here" };
        _ = Assert.Throws<ArgumentException>(() => PromptTemplateEngine.Apply(item, config));
    }

    [Fact]
    public void SystemPromptTemplate_Applied()
    {
        var engine = new PromptTemplateEngine();
        var item = MakeItem();
        var config = new DataSourceConfig
        {
            SystemPromptTemplate = "You are a {meta.role} assistant.",
            PromptTemplate = "{prompt}",
        };
        // We need meta for this to work meaningfully
        var itemWithMeta = item with { Metadata = new Dictionary<string, string> { ["role"] = "French" } };
        var result = PromptTemplateEngine.Apply(itemWithMeta, config);
        Assert.Equal("You are a French assistant.", result.SystemPrompt);
    }
}
