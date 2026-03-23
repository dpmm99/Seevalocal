using Seevalocal.Core;
using Seevalocal.Core.Models;
using System.Collections;
using System.Reflection;

namespace Seevalocal.Config.Merging;

/// <summary>
/// <para>
/// Merges an ordered list of <see cref="PartialConfig"/> settings files plus CLI
/// overrides into a single <see cref="ResolvedConfig"/>.
/// </para>
/// <para>
/// Merge rule (right-fold null-coalescing, per 03-config.md §4.1):
///   For each leaf field:
///     1. If CLI overrides has a non-null value → use it.
///     2. Else walk settings files from last to first; use first non-null.
///     3. If still null → field is unset; use defaults.
/// </para>
/// <para>
/// Flat scalar/list properties are handled automatically via reflection.
/// Complex sub-object properties (e.g. <see cref="ServerConfig"/>,
/// <see cref="LlamaServerSettings"/> nested inside <see cref="JudgeConfig"/>)
/// are handled by a per-<typeparamref name="TResult"/> special-handler registry
/// registered in <see cref="SpecialHandlers{TResult}"/>.
/// </para>
/// </summary>
public sealed class ConfigurationMerger
{
    /// <summary>
    /// Merges settings files in order; later files override earlier ones.
    /// CLI overrides are applied last (highest priority).
    /// </summary>
    public ResolvedConfig Merge(
        IReadOnlyList<PartialConfig> settingsFiles,
        PartialConfig? cliOverrides = null)
    {
        // Build a combined list ordered lowest → highest priority
        // (index 0 = first file, last = CLI)
        var all = new List<PartialConfig>(settingsFiles);
        if (cliOverrides is not null)
            all.Add(cliOverrides);

        return new ResolvedConfig
        {
            Run = MergeSection<RunMeta, PartialRunMeta>(all, c => c.Run),
            Server = MergeSection<ServerConfig, PartialServerConfig>(all, c => c.Server),
            LlamaServer = MergeSection<LlamaServerSettings, PartialLlamaServerSettings>(all, c => c.LlamaSettings),
            Judge = MergeJudgeConfig(all),
            DataSource = MergeSection<DataSourceConfig, PartialDataSourceConfig>(all, c => c.DataSource),
            PipelineOptions = MergePipelineOptions(all),
        };
    }

    // -----------------------------------------------------------------------
    // JudgeConfig
    // -----------------------------------------------------------------------

    private static JudgeConfig? MergeJudgeConfig(IReadOnlyList<PartialConfig> all)
    {
        if (!all.Any(p => p.Judge is not null)) return null;

        // Reflection handles all flat/list properties on JudgeConfig/PartialJudgeConfig.
        // Special handlers supply the two complex sub-objects.
        return MergeSection<JudgeConfig, PartialJudgeConfig>(all, c => c.Judge);
    }

    // -----------------------------------------------------------------------
    // PipelineOptions — last-wins union across all configs
    // -----------------------------------------------------------------------

    private static Dictionary<string, object?> MergePipelineOptions(IReadOnlyList<PartialConfig> all)
    {
        // Walk lowest → highest priority; higher-priority keys overwrite lower ones.
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var config in all)
        {
            if (config.PipelineOptions is null) continue;
            foreach (var (key, value) in config.PipelineOptions)
                result[key] = value;
        }
        return result;
    }

    // -----------------------------------------------------------------------
    // Special-handler registry
    // -----------------------------------------------------------------------

    /// <summary>
    /// Holds named handlers for properties on <typeparamref name="TResult"/> that
    /// require custom merge logic (i.e. they are complex objects, not flat scalars).
    /// Any property whose name appears here is skipped during the reflection pass
    /// and instead resolved by the registered factory.
    /// </summary>
    private static class SpecialHandlers<TResult>
    {
        // Dictionary<propertyName, Func<all, value>>
        internal static readonly Dictionary<
            string,
            Func<IReadOnlyList<PartialConfig>, object?>> Registry = BuildRegistry();

        private static Dictionary<string, Func<IReadOnlyList<PartialConfig>, object?>> BuildRegistry()
            => typeof(TResult) switch
            {
                // ----- ResolvedConfig ----------------------------------------
                // (flat scalar/list properties are handled by reflection;
                //  PipelineOptions is handled separately in Merge())
                var t when t == typeof(ResolvedConfig) => new(StringComparer.Ordinal)
                {
                    // No special handlers needed here: Run, Server, LlamaServer,
                    // Judge, DataSource, and PipelineOptions are all assigned
                    // explicitly in Merge(). If MergeSection were ever used
                    // directly for ResolvedConfig these would be needed.
                },

                // ----- JudgeConfig -------------------------------------------
                var t when t == typeof(JudgeConfig) => new(StringComparer.Ordinal)
                {
                    [nameof(JudgeConfig.ServerConfig)] = all =>
                        MergeSection<ServerConfig, PartialServerConfig>(
                            all, c => c.Judge?.ServerConfig),

                    [nameof(JudgeConfig.ServerSettings)] = all =>
                    {
                        return !all.Any(p => p.Judge?.ServerSettings is not null)
                            ? null
                            : (object)MergeSection<LlamaServerSettings, PartialLlamaServerSettings>(
                            all, c => c.Judge?.ServerSettings);
                    },
                },

                _ => new(StringComparer.Ordinal),
            };
    }

    /// <summary>
    /// Merges any config record by reflection.  For each property on
    /// <typeparamref name="TResult"/>:
    /// <list type="number">
    ///   <item>If a special handler is registered for that property name, delegate to it.</item>
    ///   <item>Otherwise walk configs last-to-first for the first usable value.</item>
    ///   <item>If none found, apply <see cref="MergeDefaultAttribute"/> if present.</item>
    ///   <item>Otherwise leave as the type default (null / 0 / false).</item>
    /// </list>
    /// </summary>
    private static TResult MergeSection<TResult, TSource>(
        IReadOnlyList<PartialConfig> all,
        Func<PartialConfig, TSource?> sectionSelector)
        where TResult : new()
        where TSource : class
    {
        var destProps = GetProps(typeof(TResult));
        var sourceProps = GetProps(typeof(TSource));
        var handlers = SpecialHandlers<TResult>.Registry;
        var result = new TResult();

        foreach (var (name, destProp) in destProps)
        {
            // --- special handler? -------------------------------------------
            if (handlers.TryGetValue(name, out var handler))
            {
                destProp.SetValue(result, handler(all));
                continue;
            }

            // --- reflection pass --------------------------------------------
            if (!sourceProps.TryGetValue(name, out var srcProp))
            {
                ApplyDefault(result, destProp);
                continue;
            }

            var found = false;
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var section = sectionSelector(all[i]);
                if (section is null) continue;

                var value = srcProp.GetValue(section);
                if (!IsUsableValue(value)) continue;

                destProp.SetValue(result, value);
                found = true;
                break;
            }

            if (!found)
                ApplyDefault(result, destProp);
        }

        return result;
    }

    /// <summary>
    /// Returns true when a merged value should be considered "set" and stop the search.
    /// </summary>
    private static bool IsUsableValue(object? value)
    {
        return value is not null && (value is string s ? !string.IsNullOrWhiteSpace(s) : value is not ICollection { Count: 0 });
    }

    private static void ApplyDefault<TResult>(TResult result, PropertyInfo prop)
    {
        var attr = prop.GetCustomAttribute<MergeDefaultAttribute>();
        if (attr is null) return;

        // Attribute constructor only accepts compile-time constants, so enums
        // arrive as their underlying int — convert back to the property's type.
        var value = attr.Value is null
            ? null
            : Convert.ChangeType(attr.Value, prop.PropertyType);

        prop.SetValue(result, value);
    }

    // Keyed cache shared across all TResult/TSource combinations.
    private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> PropCache = [];

    private static Dictionary<string, PropertyInfo> GetProps(Type type)
    {
        lock (PropCache)
        {
            if (!PropCache.TryGetValue(type, out var props))
                PropCache[type] = props = type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .ToDictionary(p => p.Name, StringComparer.Ordinal);
            return props;
        }
    }
}