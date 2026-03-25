using Seevalocal.Core.Models;
using System.Globalization;

namespace Seevalocal.Server;

/// <summary>
/// Converts <see cref="LlamaServerSettings"/> and <see cref="ServerConfig"/> into a
/// <c>string[]</c> suitable for <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>.
/// Only non-null settings are emitted. Null means "use llama-server default."
/// Driven entirely by <see cref="LlamaSettingAttribute"/> metadata via reflection.
/// </summary>
public sealed class LlamaServerArgBuilder
{
    /// <summary>
    /// Builds the full argument list for launching llama-server.
    /// </summary>
    public static string[] Build(LlamaServerSettings settings, ServerConfig serverConfig)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(serverConfig);

        List<string> args = [];

        // ── Model ────────────────────────────────────────────────────────────
        if (serverConfig.Model is { } model)
        {
            if (model.Kind == ModelSourceKind.LocalFile && model.FilePath is not null)
            {
                args.Add("-m");
                args.Add(model.FilePath);
            }
            else if (model.Kind == ModelSourceKind.HuggingFace)
            {
                if (model.HfRepo is not null)
                {
                    args.Add("--hf-repo");
                    args.Add(model.HfRepo);
                }
                if (model.HfQuant is not null)
                {
                    args.Add("--hf-file");
                    args.Add(model.HfQuant);
                }
                if (model.HfToken is not null)
                {
                    args.Add("--hf-token");
                    args.Add(model.HfToken);
                }
            }
        }

        // ── Network ──────────────────────────────────────────────────────────
        // Network settings come from serverConfig only (not from settings)
        var url = new Uri(serverConfig.BaseUrl ?? "http://127.0.0.1:8080");
        if (url != null)
        {
            args.Add("--host");
            args.Add(url.Host);
            args.Add("--port");
            args.Add(url.Port.ToString());
        }

        var apiKey = serverConfig.ApiKey;
        if (apiKey is not null)
        {
            args.Add("--api-key");
            args.Add(apiKey);
        }

        // ── Llama-server settings (driven by attributes) ─────────────────────
        var metadata = LlamaSettingsMetadata.LlamaServerSettings;
        foreach (var m in metadata)
        {
            var value = LlamaSettingsMetadata.GetPropertyValue(m, settings);
            if (value is null) continue;

            // Skip ExtraArgs here - handled separately below
            if (m.SettingsKey == "extraArgs") continue;

            AppendSetting(args, m, value);
        }

        // ── Extra args (verbatim) ─────────────────────────────────────────────
        if (settings.ExtraArgs.Count > 0)
            args.AddRange(settings.ExtraArgs);

        return [.. args];
    }

    /// <summary>
    /// Appends CLI arguments for a single setting based on its metadata.
    /// </summary>
    private static void AppendSetting(List<string> args, LlamaSettingMetadata metadata, object value)
    {
        switch (metadata.SettingType)
        {
            case LlamaSettingType.Int:
                if (value is int i && metadata.CliFlag is not null)
                {
                    args.Add(metadata.CliFlag);
                    args.Add(i.ToString());
                }
                break;

            case LlamaSettingType.Double:
                if (value is double d && metadata.CliFlag is not null)
                {
                    args.Add(metadata.CliFlag);
                    args.Add(d.ToString(CultureInfo.InvariantCulture));
                }
                break;

            case LlamaSettingType.String:
                if (value is string s && !string.IsNullOrEmpty(s) && metadata.CliFlag is not null)
                {
                    args.Add(metadata.CliFlag);
                    args.Add(s);
                }
                break;

            case LlamaSettingType.Bool:
                if (value is bool b && metadata.CliFlag is not null)
                {
                    args.Add(metadata.CliFlag);
                    args.Add(b ? "on" : "off");
                }
                break;

            case LlamaSettingType.BoolLong:
                if (value is bool bl && metadata.EnableFlag is not null && metadata.DisableFlag is not null)
                {
                    args.Add(bl ? metadata.EnableFlag : metadata.DisableFlag);
                }
                break;
        }
    }
}
