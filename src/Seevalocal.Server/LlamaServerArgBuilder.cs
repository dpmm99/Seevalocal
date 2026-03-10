using Seevalocal.Core.Models;

namespace Seevalocal.Server;

/// <summary>
/// Converts <see cref="LlamaServerSettings"/> and <see cref="ServerConfig"/> into a
/// <c>string[]</c> suitable for <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>.
/// Only non-null settings are emitted. Null means "use llama-server default."
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
        var host = settings.Host ?? serverConfig.Host;
        args.Add("--host");
        args.Add(host ?? "127.0.0.1");

        var port = settings.Port ?? serverConfig.Port;
        args.Add("--port");
        args.Add(port?.ToString() ?? "8080");

        var apiKey = settings.ApiKey ?? serverConfig.ApiKey;
        if (apiKey is not null)
        {
            args.Add("--api-key");
            args.Add(apiKey);
        }

        // ── Context / batching ───────────────────────────────────────────────
        AppendInt(args, settings.ContextWindowTokens, "-c");
        AppendInt(args, settings.BatchSizeTokens, "-b");
        AppendInt(args, settings.UbatchSizeTokens, "-ub");
        AppendInt(args, settings.ParallelSlotCount, "-np");
        AppendInt(args, settings.GpuLayerCount, "-ngl");
        AppendInt(args, settings.ThreadCount, "-t");

        // ── Boolean feature flags ────────────────────────────────────────────
        AppendBool(args, settings.EnableFlashAttention, "-fa");
        AppendBoolLong(args, settings.EnableContinuousBatching, "--cont-batching", "--no-cont-batching");
        AppendBoolLong(args, settings.EnableCachePrompt, "--cache-prompt", "--no-cache-prompt");
        AppendBoolLong(args, settings.EnableContextShift, "--context-shift", "--no-context-shift");
        AppendBoolLong(args, settings.EnableJinja, "--jinja", "--no-jinja");

        // ── Sampling ─────────────────────────────────────────────────────────
        AppendDouble(args, settings.SamplingTemperature, "--temp");
        AppendDouble(args, settings.TopP, "--top-p");
        AppendInt(args, settings.TopK, "--top-k");
        AppendDouble(args, settings.MinP, "--min-p");
        AppendDouble(args, settings.RepeatPenalty, "--repeat-penalty");
        AppendInt(args, settings.Seed, "--seed");

        // ── KV cache ─────────────────────────────────────────────────────────
        AppendString(args, settings.KvCacheTypeK, "-ctk");
        AppendString(args, settings.KvCacheTypeV, "-ctv");

        // ── Misc ─────────────────────────────────────────────────────────────
        AppendInt(args, settings.LogVerbosity, "-lv");
        AppendString(args, settings.ChatTemplate, "--chat-template");
        AppendString(args, settings.ReasoningFormat, "--reasoning-format");

        // ── Extra args (verbatim) ─────────────────────────────────────────────
        if (serverConfig.ExtraArgs.Count > 0)
            args.AddRange(serverConfig.ExtraArgs);

        return [.. args];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AppendInt(List<string> args, int? value, string flag)
    {
        if (value is null) return;
        args.Add(flag);
        args.Add(value.Value.ToString());
    }

    private static void AppendDouble(List<string> args, double? value, string flag)
    {
        if (value is null) return;
        args.Add(flag);
        // Use invariant culture so we never emit a comma as decimal separator.
        args.Add(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AppendString(List<string> args, string? value, string flag)
    {
        if (string.IsNullOrEmpty(value)) return;
        args.Add(flag);
        args.Add(value);
    }

    /// <summary>
    /// Emits e.g. <c>-fa on</c> / <c>-fa off</c>.
    /// </summary>
    private static void AppendBool(List<string> args, bool? value, string flag)
    {
        if (value is null) return;
        args.Add(flag);
        args.Add(value.Value ? "on" : "off");
    }

    /// <summary>
    /// Emits e.g. <c>--cont-batching</c> or <c>--no-cont-batching</c>.
    /// </summary>
    private static void AppendBoolLong(List<string> args, bool? value, string enableFlag, string disableFlag)
    {
        if (value is null) return;
        args.Add(value.Value ? enableFlag : disableFlag);
    }
}
