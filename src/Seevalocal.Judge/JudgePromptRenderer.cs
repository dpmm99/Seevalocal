using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Seevalocal.Judge;

/// <summary>
/// Substitutes {prompt}, {expectedOutput}, {actualOutput}, and {metadata.KEY} placeholders
/// in a judge prompt template. Unknown {metadata.KEY} placeholders are replaced with an empty
/// string and a warning is logged.
/// </summary>
public sealed partial class JudgePromptRenderer(ILogger<JudgePromptRenderer> logger)
{
    private static readonly Regex MetadataPlaceholder =
        MetadataPlaceholderRegex();

    private readonly ILogger<JudgePromptRenderer> _logger = logger;

    /// <summary>
    /// Renders the template by substituting all known placeholders.
    /// Thread-safe; may be called concurrently.
    /// </summary>
    public string Render(
        string template,
        string userPrompt,
        string expectedOutput,
        string actualOutput,
        IReadOnlyDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(userPrompt);
        ArgumentNullException.ThrowIfNull(expectedOutput);
        ArgumentNullException.ThrowIfNull(actualOutput);
        ArgumentNullException.ThrowIfNull(metadata);

        var result = template
            .Replace("{prompt}", userPrompt, StringComparison.Ordinal)
            .Replace("{expectedOutput}", expectedOutput, StringComparison.Ordinal)
            .Replace("{actualOutput}", actualOutput, StringComparison.Ordinal);

        return MetadataPlaceholder.Replace(result, match =>
        {
            var key = match.Groups["key"].Value;
            if (metadata.TryGetValue(key, out var value))
                return value;

            _logger.LogWarning(
                "[JudgePromptRenderer] Unknown metadata placeholder {{metadata.{Key}}} — substituting empty string",
                key);
            return string.Empty;
        });
    }

    [GeneratedRegex(@"\{metadata\.(?<key>[^}]+)\}", RegexOptions.Compiled)]
    private static partial Regex MetadataPlaceholderRegex();
}
