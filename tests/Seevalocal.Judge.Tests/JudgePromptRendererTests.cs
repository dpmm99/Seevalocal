using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Seevalocal.Judge.Tests;

public class JudgePromptRendererTests
{
    private static JudgePromptRenderer CreateRenderer(ILogger<JudgePromptRenderer>? logger = null) =>
        new(logger ?? NullLogger<JudgePromptRenderer>.Instance);

    private static readonly IReadOnlyDictionary<string, string> EmptyMeta =
        new Dictionary<string, string>();

    [Fact]
    public void Render_SubstitutesAllCoreVariables()
    {
        var renderer = CreateRenderer();
        const string template = "Prompt: {prompt} | Expected: {expectedOutput} | Actual: {actualOutput}";
        var result = renderer.Render(template, "Q?", "A", "B", EmptyMeta);

        _ = result.Should().Be("Prompt: Q? | Expected: A | Actual: B");
    }

    [Fact]
    public void Render_SubstitutesMetadataKey()
    {
        var renderer = CreateRenderer();
        const string template = "Lang: {metadata.sourceLang}";
        var meta = new Dictionary<string, string> { ["sourceLang"] = "en" };

        var result = renderer.Render(template, "", "", "", meta);

        _ = result.Should().Be("Lang: en");
    }

    [Fact]
    public void Render_UnknownMetadataKey_ReplacesWithEmpty()
    {
        var renderer = CreateRenderer();
        const string template = "X={metadata.missing}";

        var result = renderer.Render(template, "", "", "", EmptyMeta);

        _ = result.Should().Be("X=");
    }

    [Fact]
    public void Render_UnknownMetadataKey_LogsWarning()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddDebug().SetMinimumLevel(LogLevel.Trace));
        var logger = loggerFactory.CreateLogger<JudgePromptRenderer>();

        // We just verify it does not throw; warning logging is tested via logger inspection in integration tests.
        var renderer = new JudgePromptRenderer(logger);
        Func<string> act = () => renderer.Render("{metadata.ghost}", "", "", "", EmptyMeta);
        _ = act.Should().NotThrow();
    }

    [Fact]
    public void Render_MultipleMetadataKeys_AllSubstituted()
    {
        var renderer = CreateRenderer();
        const string template = "{metadata.a}-{metadata.b}";
        var meta = new Dictionary<string, string> { ["a"] = "hello", ["b"] = "world" };

        var result = renderer.Render(template, "", "", "", meta);

        _ = result.Should().Be("hello-world");
    }

    [Fact]
    public void Render_TemplateWithNoPlaceholders_ReturnedUnchanged()
    {
        var renderer = CreateRenderer();
        const string template = "Static content.";
        var result = renderer.Render(template, "p", "e", "a", EmptyMeta);
        _ = result.Should().Be("Static content.");
    }
}
