using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core.Pipeline.Stages;
using Xunit;

namespace Seevalocal.Core.Pipeline.Tests;

public class FileWriterStageTests : IDisposable
{
    private readonly string _tempDir;

    public FileWriterStageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"seevalocal-tests-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string TempPath(string filename) => Path.Combine(_tempDir, filename);

    private static EvalStageContext MakeCtxWithResponse(string? response, string itemId = "item-000001") =>
        TestHelpers.MakeContext(
            item: TestHelpers.MakeItem(id: itemId),
            stageOutputs: response is not null
                ? new Dictionary<string, object?> { ["PromptStage.response"] = response }
                : []);

    // ── Basic write ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_WritesContentToFile()
    {
        var outputPath = TempPath("{id}.txt");
        var stage = new FileWriterStage(NullLogger<FileWriterStage>.Instance)
        {
            OutputFilePathTemplate = outputPath,
            StripMarkdownCodeFences = false
        };

        var ctx = MakeCtxWithResponse("Hello, world!");
        var result = await stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);

        var writtenPath = result.Outputs[$"{stage.StageName}.writtenFilePath"] as string;
        Assert.NotNull(writtenPath);
        Assert.True(File.Exists(writtenPath));
        Assert.Equal("Hello, world!", await File.ReadAllTextAsync(writtenPath));
    }

    // ── Path template substitution ────────────────────────────────────────────

    [Fact]
    public async Task Execute_PathTemplate_ItemIdSubstituted()
    {
        var outputPath = TempPath("{id}.cs");
        var stage = new FileWriterStage(NullLogger<FileWriterStage>.Instance)
        {
            OutputFilePathTemplate = outputPath,
            StripMarkdownCodeFences = false
        };

        var ctx = MakeCtxWithResponse("code", itemId: "item-000042");
        var result = await stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
        var writtenPath = result.Outputs[$"{stage.StageName}.writtenFilePath"] as string;
        Assert.Contains("item-000042", writtenPath);
    }

    // ── Markdown fence stripping ──────────────────────────────────────────────

    [Fact]
    public async Task Execute_StripMarkdownCodeFences_RemovesFence()
    {
        var outputPath = TempPath("{id}.cs");
        var stage = new FileWriterStage(NullLogger<FileWriterStage>.Instance)
        {
            OutputFilePathTemplate = outputPath,
            StripMarkdownCodeFences = true
        };

        var responseWithFence = "```csharp\nConsole.WriteLine(\"Hello\");\n```";
        var ctx = MakeCtxWithResponse(responseWithFence);
        var result = await stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
        var writtenPath = result.Outputs[$"{stage.StageName}.writtenFilePath"] as string;
        var content = await File.ReadAllTextAsync(writtenPath!);

        Assert.DoesNotContain("```", content);
        Assert.Contains("Console.WriteLine", content);
    }

    [Fact]
    public async Task Execute_StripMarkdownCodeFences_NoFence_ContentUnchanged()
    {
        var outputPath = TempPath("{id}.txt");
        var stage = new FileWriterStage(NullLogger<FileWriterStage>.Instance)
        {
            OutputFilePathTemplate = outputPath,
            StripMarkdownCodeFences = true
        };

        var ctx = MakeCtxWithResponse("plain content");
        var result = await stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
        var writtenPath = result.Outputs[$"{stage.StageName}.writtenFilePath"] as string;
        var content = await File.ReadAllTextAsync(writtenPath!);
        Assert.Equal("plain content", content);
    }

    // ── Missing input ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_MissingStageOutput_ReturnsFailure()
    {
        var outputPath = TempPath("{id}.txt");
        var stage = new FileWriterStage(NullLogger<FileWriterStage>.Instance)
        {
            OutputFilePathTemplate = outputPath
        };

        // No PromptStage.response in StageOutputs
        var ctx = MakeCtxWithResponse(response: null);
        var result = await stage.ExecuteAsync(ctx);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureReason);
    }

    // ── Directory creation ────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_CreatesIntermediateDirectories()
    {
        var nestedPath = TempPath(Path.Combine("nested", "deep", "{id}.txt"));
        var stage = new FileWriterStage(NullLogger<FileWriterStage>.Instance)
        {
            OutputFilePathTemplate = nestedPath,
            StripMarkdownCodeFences = false
        };

        var ctx = MakeCtxWithResponse("content");
        var result = await stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
        var writtenPath = result.Outputs[$"{stage.StageName}.writtenFilePath"] as string;
        Assert.True(File.Exists(writtenPath));
    }
}
