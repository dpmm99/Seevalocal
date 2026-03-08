using Seevalocal.Core.Models;

namespace Seevalocal.DataSources;

/// <summary>
/// Applies prompt and system-prompt templates to an EvalItem.
/// Available placeholders: {prompt}, {expected}, {id}, {meta.key}
/// </summary>
public sealed class PromptTemplateEngine
{
    /// <summary>
    /// Applies configured templates to an EvalItem, returning a new item with substituted fields.
    /// If PromptTemplate is set, the original UserPrompt is saved to Metadata["originalPrompt"].
    /// Throws <see cref="ArgumentException"/> if PromptTemplate is set but does not contain {prompt}.
    /// </summary>
    public static EvalItem Apply(EvalItem item, DataSourceConfig config)
    {
        var promptTemplate = config.PromptTemplate;
        var sysTemplate = config.SystemPromptTemplate;

        if (promptTemplate is null && sysTemplate is null)
            return item;

        if (promptTemplate?.Contains("{prompt}") == false)
            throw new ArgumentException(
                $"PromptTemplate must contain the {{prompt}} placeholder. Value: {promptTemplate}");

        var newUserPrompt = item.UserPrompt;
        var newMetadata = item.Metadata;

        if (promptTemplate is not null)
        {
            newUserPrompt = ApplyTemplate(promptTemplate, item);
            // Save original prompt
            var mutableMeta = new Dictionary<string, string>(item.Metadata)
            {
                ["originalPrompt"] = item.UserPrompt
            };
            newMetadata = mutableMeta;
        }

        var newSystemPrompt = item.SystemPrompt;
        if (sysTemplate is not null)
            newSystemPrompt = ApplyTemplate(sysTemplate, item);

        return item with
        {
            UserPrompt = newUserPrompt,
            SystemPrompt = newSystemPrompt,
            Metadata = newMetadata,
        };
    }

    private static string ApplyTemplate(string template, EvalItem item)
    {
        var result = template
            .Replace("{prompt}", item.UserPrompt)
            .Replace("{expected}", item.ExpectedOutput ?? "")
            .Replace("{id}", item.Id);

        foreach ((var key, var value) in item.Metadata)
            result = result.Replace($"{{meta.{key}}}", value);

        return result;
    }
}
