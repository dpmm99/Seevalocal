using System.Collections.Concurrent;
using System.Reflection;

namespace Seevalocal.Core.Models;

/// <summary>
/// Cached metadata for a llama-server setting property.
/// </summary>
public sealed record LlamaSettingMetadata
{
    public PropertyInfo Property { get; init; } = null!;
    public string SettingsKey { get; init; } = "";
    public string? CliFlag { get; init; }
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public LlamaSettingType SettingType { get; init; }
    public string? EnableFlag { get; init; }
    public string? DisableFlag { get; init; }
}

/// <summary>
/// Provides cached reflection metadata for llama-server settings.
/// Drives automatic CLI argument generation and settings UI registration.
/// </summary>
public static class LlamaSettingsMetadata
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<LlamaSettingMetadata>> _cache = new();

    /// <summary>
    /// Gets all llama-server setting metadata for a given settings type.
    /// </summary>
    public static IReadOnlyList<LlamaSettingMetadata> GetSettings(Type settingsType)
    {
        return _cache.GetOrAdd(settingsType, type =>
        {
            var metadata = new List<LlamaSettingMetadata>();
            var resolvedType = type.Name.StartsWith("Partial") ? Type.GetType(type.Name.Replace("Partial", "")) ?? Type.GetType(type.Name.Replace("Partial", "Resolved")) : null;
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = resolvedType?.GetCustomAttribute<LlamaSettingAttribute>() ?? prop.GetCustomAttribute<LlamaSettingAttribute>();
                if (attr is null) continue;

                metadata.Add(new LlamaSettingMetadata
                {
                    Property = prop,
                    SettingsKey = attr.SettingsKey,
                    CliFlag = attr.CliFlag,
                    DisplayName = attr.DisplayName,
                    Description = attr.Description,
                    SettingType = attr.SettingType,
                    EnableFlag = attr.EnableFlag,
                    DisableFlag = attr.DisableFlag
                });
            }
            return metadata;
        });
    }

    /// <summary>
    /// Gets all llama-server setting metadata for <see cref="LlamaServerSettings"/>.
    /// </summary>
    public static IReadOnlyList<LlamaSettingMetadata> LlamaServerSettings =>
        GetSettings(typeof(LlamaServerSettings));

    /// <summary>
    /// Gets all llama-server setting metadata for <see cref="PartialLlamaServerSettings"/>.
    /// </summary>
    public static IReadOnlyList<LlamaSettingMetadata> PartialLlamaServerSettings =>
        GetSettings(typeof(PartialLlamaServerSettings));

    /// <summary>
    /// Gets the value of a setting property from a settings instance.
    /// </summary>
    public static object? GetPropertyValue(LlamaSettingMetadata metadata, object settings)
    {
        return metadata.Property.GetValue(settings);
    }

    /// <summary>
    /// Sets the value of a setting property on a settings instance (for builder patterns).
    /// </summary>
    public static void SetPropertyValue(LlamaSettingMetadata metadata, object settings, object? value)
    {
        // For record types with init-only properties, we can't set via reflection after construction
        // This is mainly used for builder patterns that use mutable types
        var prop = metadata.Property;
        if (prop.CanWrite)
        {
            prop.SetValue(settings, value);
        }
    }

    /// <summary>
    /// Merges two LlamaServerSettings instances, with overlay taking precedence.
    /// Uses reflection driven by LlamaSettingAttribute metadata.
    /// </summary>
    /// <param name="overlay">The overlay settings (takes precedence).</param>
    /// <param name="baseSettings">The base settings (fallback values).</param>
    /// <returns>A new LlamaServerSettings with merged values.</returns>
    public static LlamaServerSettings Merge(LlamaServerSettings? overlay, LlamaServerSettings? baseSettings)
    {
        var result = new LlamaServerSettings();
        var props = typeof(LlamaServerSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite);

        foreach (var prop in props)
        {
            var overlayValue = prop.GetValue(overlay);
            var baseValue = prop.GetValue(baseSettings);

            object? value;
            if (overlayValue is IReadOnlyList<string> overlayList
                && baseValue is IReadOnlyList<string> baseList)
            {
                value = overlayList.Count > 0 ? overlayList : baseList;
            }
            else
            {
                value = overlayValue ?? baseValue;
            }

            prop.SetValue(result, value);
        }

        return result;
    }

    /// <summary>
    /// Merges two PartialLlamaServerSettings instances, with overlay taking precedence.
    /// Uses reflection driven by LlamaSettingAttribute metadata.
    /// </summary>
    public static PartialLlamaServerSettings Merge(PartialLlamaServerSettings? overlay, PartialLlamaServerSettings? baseSettings)
    {
        var result = new PartialLlamaServerSettings();
        var settingsType = typeof(PartialLlamaServerSettings);
        var props = settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            var overlayValue = prop.GetValue(overlay);
            var baseValue = prop.GetValue(baseSettings);

            // Use overlay value if present, otherwise use base value
            var value = overlayValue ?? baseValue;

            // For ExtraArgs, merge lists if both present
            if (prop.Name == "ExtraArgs" &&
                overlayValue is List<string> overlayList &&
                baseValue is List<string> baseList)
            {
                value = overlayList.Count > 0 ? overlayList : baseList;
            }

            if (prop.CanWrite && prop.SetMethod is not null)
            {
                prop.SetValue(result, value);
            }
        }

        return result;
    }

    /// <summary>
    /// Merges two PartialJudgeConfig instances, with overlay taking precedence.
    /// Uses reflection to avoid enumerating every field manually.
    /// </summary>
    public static PartialJudgeConfig Merge(PartialJudgeConfig? overlay, PartialJudgeConfig? baseConfig)
    {
        var result = new PartialJudgeConfig();
        var configType = typeof(PartialJudgeConfig);
        var props = configType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            // ServerConfig and ServerSettings need special handling
            if (prop.Name == nameof(PartialJudgeConfig.ServerConfig))
            {
                var overlayValue = overlay?.ServerConfig;
                var baseValue = baseConfig?.ServerConfig;
                var merged = MergeServerConfig(overlayValue, baseValue);
                prop.SetValue(result, merged);
            }
            else if (prop.Name == nameof(PartialJudgeConfig.ServerSettings))
            {
                var overlayValue = overlay?.ServerSettings;
                var baseValue = baseConfig?.ServerSettings;
                var merged = Merge(overlayValue, baseValue);
                prop.SetValue(result, merged);
            }
            else
            {
                var overlayValue = prop.GetValue(overlay);
                var baseValue = prop.GetValue(baseConfig);
                var value = overlayValue ?? baseValue;
                if (prop.CanWrite && prop.SetMethod is not null)
                {
                    prop.SetValue(result, value);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Merges two PartialServerConfig instances, with overlay taking precedence.
    /// Uses reflection to avoid enumerating every field manually.
    /// </summary>
    private static PartialServerConfig MergeServerConfig(PartialServerConfig? overlay, PartialServerConfig? baseConfig)
    {
        var result = new PartialServerConfig();
        var configType = typeof(PartialServerConfig);
        var props = configType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            var overlayValue = prop.GetValue(overlay);
            var baseValue = prop.GetValue(baseConfig);
            var value = overlayValue ?? baseValue;
            if (prop.CanWrite && prop.SetMethod is not null)
            {
                prop.SetValue(result, value);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a PartialJudgeConfig from a dictionary of field values using reflection.
    /// </summary>
    /// <param name="fieldGetter">Function to get a field value by key (e.g., "judge.template").</param>
    /// <param name="prefix">Field prefix (e.g., "judge").</param>
    /// <returns>A PartialJudgeConfig with values from the field getter.</returns>
    public static PartialJudgeConfig BuildPartialJudgeConfig(Func<string, string?> fieldGetter, string prefix = "judge")
    {
        var result = new PartialJudgeConfig();
        var configType = typeof(PartialJudgeConfig);
        var props = configType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            // ServerConfig and ServerSettings need special handling
            if (prop.Name == nameof(PartialJudgeConfig.ServerConfig))
            {
                var serverConfig = BuildPartialServerConfig(fieldGetter, prefix);
                prop.SetValue(result, serverConfig);
            }
            else if (prop.Name == nameof(PartialJudgeConfig.ServerSettings))
            {
                // ServerSettings is built separately via BuildLlamaSettingsFromFields
                continue;
            }
            else
            {
                var key = $"{prefix}.{ToSnakeCase(prop.Name)}";
                var value = fieldGetter(key);
                if (!string.IsNullOrEmpty(value))
                {
                    var converted = ConvertValue(value, prop.PropertyType);
                    if (converted != null && prop.CanWrite && prop.SetMethod is not null)
                    {
                        prop.SetValue(result, converted);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a PartialServerConfig from a dictionary of field values using reflection.
    /// </summary>
    private static PartialServerConfig BuildPartialServerConfig(Func<string, string?> fieldGetter, string prefix)
    {
        var result = new PartialServerConfig();
        var configType = typeof(PartialServerConfig);
        var props = configType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            if (prop.Name == nameof(PartialServerConfig.Model))
            {
                var filePath = fieldGetter($"{prefix}.modelFile");
                var hfRepo = fieldGetter($"{prefix}.hfRepo");
                if (!string.IsNullOrEmpty(filePath) || !string.IsNullOrEmpty(hfRepo))
                {
                    var model = new ModelSource
                    {
                        FilePath = string.IsNullOrEmpty(filePath) ? null : filePath,
                        HfRepo = string.IsNullOrEmpty(hfRepo) ? null : hfRepo
                    };
                    prop.SetValue(result, model);
                }
            }
            else
            {
                var key = $"{prefix}.{ToSnakeCase(prop.Name)}";
                var value = fieldGetter(key);
                if (!string.IsNullOrEmpty(value))
                {
                    var converted = ConvertValue(value, prop.PropertyType);
                    if (converted != null && prop.CanWrite && prop.SetMethod is not null)
                    {
                        prop.SetValue(result, converted);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Converts a string value to the target type.
    /// </summary>
    private static object? ConvertValue(string value, Type targetType)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(bool?) && bool.TryParse(value, out var b)) return b;
        if (targetType == typeof(int?) && int.TryParse(value, out var i)) return i;
        if (targetType == typeof(double?) && double.TryParse(value, out var d)) return d;
        if (targetType.IsEnum && Enum.TryParse(targetType, value, true, out var e)) return e;
        return null;
    }

    /// <summary>
    /// Converts a PascalCase property name to snake_case for field keys.
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        return string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLower(c) : char.ToLower(c).ToString()));
    }
}
