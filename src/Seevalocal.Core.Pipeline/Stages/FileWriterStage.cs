using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Seevalocal.Core.Pipeline.Stages;

/// <summary>
/// Writes a stage output value (typically the LLM response) to a file.
/// Useful in coding pipelines to persist generated source code.
/// Thread-safe: each call writes to a distinct path via the item ID template.
/// </summary>
public sealed partial class FileWriterStage(ILogger<FileWriterStage> logger) : IEvalStage
{
    private readonly ILogger<FileWriterStage> _logger = logger;

    public string StageName { get; init; } = "FileWriterStage";

    /// <summary>Key in StageOutputs to read content from.</summary>
    public string InputStageOutputKey { get; init; } = "PromptStage.response";

    /// <summary>
    /// File path template. {id} is replaced with EvalItem.Id.
    /// Example: "./generated/{id}.cs"
    /// </summary>
    public string OutputFilePathTemplate { get; init; } = "./generated/{id}.txt";

    /// <summary>
    /// When true, strip leading/trailing markdown code fences (```lang ... ```)
    /// before writing.
    /// </summary>
    public bool StripMarkdownCodeFences { get; init; } = true;

    public async Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        var item = context.Item;
        var ct = context.CancellationToken;

        if (!context.StageOutputs.TryGetValue(InputStageOutputKey, out var rawContent)
            || rawContent is not string content)
        {
            _logger.LogWarning("[{StageName}] Stage output key '{Key}' not found or not a string for item {EvalItemId}",
                StageName, InputStageOutputKey, item.Id);
            return StageResult.Failure(
                $"[{StageName}] Stage output '{InputStageOutputKey}' not available or not a string");
        }

        if (StripMarkdownCodeFences)
            content = StripFences(content);

        var resolvedPath = Path.GetFullPath(
            OutputFilePathTemplate.Replace("{id}", item.Id, StringComparison.OrdinalIgnoreCase));

        var directory = Path.GetDirectoryName(resolvedPath);
        if (directory is not null && !Directory.Exists(directory))
            _ = Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllTextAsync(resolvedPath, content, ct);
            _logger.LogDebug("[{StageName}] Wrote {ByteCount} bytes to {FilePath} for item {EvalItemId}",
                StageName, content.Length, resolvedPath, item.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[{StageName}] Failed to write file {FilePath} for item {EvalItemId}",
                StageName, resolvedPath, item.Id);
            return StageResult.Failure($"[{StageName}] Failed to write file '{resolvedPath}': {ex.Message}");
        }

        return StageResult.Success(
            new Dictionary<string, object?> { [$"{StageName}.writtenFilePath"] = resolvedPath },
            []);
    }

    // Matches: optional leading whitespace, ```, optional language identifier, newline,
    //          captured content,
    //          ```, optional trailing whitespace
    private static readonly Regex _codeFenceRegex = CodeFenceRegex();

    private static string StripFences(string content)
    {
        var trimmed = content.Trim();
        var match = _codeFenceRegex.Match(trimmed);
        return match.Success ? match.Groups[1].Value : trimmed;
    }

    /// <summary>
    /// Strips markdown code fences from content.
    /// Public static method for testing.
    /// </summary>
    public static string StripMarkdownFences(string content) => StripFences(content);
    [GeneratedRegex(@"^```[^\r\n]*\r?\n([\s\S]*?)\r?\n```\s*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CodeFenceRegex();
}
