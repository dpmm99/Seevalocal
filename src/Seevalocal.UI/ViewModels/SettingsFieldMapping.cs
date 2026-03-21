using Seevalocal.Core.Models;
using System.Reflection;

namespace Seevalocal.UI.ViewModels;

/// <summary>
/// Reflection-based helper for mapping SettingsViewModel field keys to WizardViewModel properties
/// and PartialConfig fields. Eliminates duplication by using a single source of truth for mappings.
/// </summary>
public static class SettingsFieldMapping
{
    /// <summary>
    /// Maps a SettingsViewModel field key (e.g., "llama.contextWindowTokens") to a WizardViewModel property name.
    /// Uses conventions to minimize explicit mappings:
    /// - "llama.*" → direct property name (e.g., "ContextWindowTokens")
    /// - "judge.*" → "Judge" + property name (e.g., "JudgeContextWindowTokens")
    /// - "server.*" → explicit mapping
    /// - "run.*" → explicit mapping
    /// - "output.*" → explicit mapping
    /// - "dataSource.*" → explicit mapping
    /// </summary>
    private static readonly Dictionary<string, string> s_keyToPropertyMap = new()
    {
        // Server settings
        { "server.manage", nameof(WizardViewModel.ManageServer) },
        { "server.executablePath", nameof(WizardViewModel.LlamaServerExecutablePath) },
        { "server.host", nameof(WizardViewModel.Host) },
        { "server.port", nameof(WizardViewModel.Port) },
        { "server.apiKey", nameof(WizardViewModel.ApiKey) },
        { "server.baseUrl", nameof(WizardViewModel.ServerUrl) },

        // Judge server settings (non-llama)
        { "judge.manage", nameof(WizardViewModel.JudgeManageServer) },
        { "judge.executablePath", nameof(WizardViewModel.JudgeExecutablePath) },
        { "judge.host", nameof(WizardViewModel.Host) }, // Judge uses same host field //TODO: no, it doesn't. There is no "host" or "port" field in the UI; it's just "URL".
        { "judge.port", nameof(WizardViewModel.Port) }, // Judge uses same port field
        { "judge.apiKey", nameof(WizardViewModel.JudgeApiKey) },
        { "judge.baseUrl", nameof(WizardViewModel.JudgeServerUrl) },
        { "judge.modelFile", nameof(WizardViewModel.JudgeLocalModelPath) },
        { "judge.hfRepo", nameof(WizardViewModel.JudgeHfRepo) },
        { "judge.template", nameof(WizardViewModel.JudgeTemplate) },

        // Run settings
        { "run.name", nameof(WizardViewModel.RunName) },
        { "run.outputDirectoryPath", nameof(WizardViewModel.OutputDir) },
        { "run.exportShellTarget", nameof(WizardViewModel.ShellTarget) },
        { "run.continueOnEvalFailure", nameof(WizardViewModel.ContinueOnEvalFailure) },
        { "run.maxConcurrentEvals", nameof(WizardViewModel.MaxConcurrentEvals) },

        // Data source settings
        { "dataSource.kind", nameof(WizardViewModel.UseSingleFileDataSource) }, // Special handling needed
        { "dataSource.filePath", nameof(WizardViewModel.DataFilePath) },
        { "dataSource.promptDirectory", nameof(WizardViewModel.PromptDir) },
        { "dataSource.expectedDirectory", nameof(WizardViewModel.ExpectedDir) },

        // Output settings
        { "output.writePerEvalJson", nameof(WizardViewModel.WritePerEvalJson) },
        { "output.writeSummaryJson", nameof(WizardViewModel.WriteSummaryJson) },
        { "output.writeSummaryCsv", nameof(WizardViewModel.WriteSummaryCsv) },
        { "output.writeParquet", nameof(WizardViewModel.WriteResultsParquet) },
        { "output.includeRawResponse", nameof(WizardViewModel.IncludeRawLlmResponse) },
    };

    /// <summary>
    /// Gets the WizardViewModel property name for a given SettingsViewModel field key.
    /// Returns null if no mapping exists.
    /// </summary>
    public static string? GetPropertyNameForKey(string fieldKey)
    {
        if (s_keyToPropertyMap.TryGetValue(fieldKey, out var propertyName))
            return propertyName;

        // Handle llama.* prefix → direct property name
        if (fieldKey.StartsWith("llama.", StringComparison.OrdinalIgnoreCase))
        {
            var propName = fieldKey["llama.".Length..];
            // Convert camelCase to PascalCase
            if (propName.Length > 0)
                propName = char.ToUpperInvariant(propName[0]) + propName[1..];
            return propName;
        }

        // Handle judge.* prefix → Judge + property name
        if (fieldKey.StartsWith("judge.", StringComparison.OrdinalIgnoreCase))
        {
            var propName = fieldKey["judge.".Length..];
            // Convert camelCase to PascalCase and prefix with Judge
            if (propName.Length > 0)
                propName = "Judge" + char.ToUpperInvariant(propName[0]) + propName[1..];
            return propName;
        }

        return null;
    }

    /// <summary>
    /// Gets the PropertyInfo for a given SettingsViewModel field key from WizardViewModel.
    /// Returns null if no mapping exists.
    /// </summary>
    private static readonly Dictionary<string, PropertyInfo> s_propertyCache = [];
    public static PropertyInfo? GetPropertyForKey(string fieldKey)
    {
        if (s_propertyCache.TryGetValue(fieldKey, out var prop))
            return prop;

        var propertyName = GetPropertyNameForKey(fieldKey);
        if (propertyName == null) return null;

        var propInfo = typeof(WizardViewModel).GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        if (propInfo != null)
            s_propertyCache[fieldKey] = propInfo;

        return propInfo;
    }

    /// <summary>
    /// Converts a string value to the appropriate type for a given property.
    /// Handles int, double, bool, nullable types, and ShellTarget enum.
    /// </summary>
    public static object? ConvertValue(string? stringValue, Type targetType)
    {
        if (string.IsNullOrEmpty(stringValue))
            return GetDefaultValueForType(targetType);

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(bool))
            return stringValue.ToLowerInvariant() == "true";

        if (underlyingType == typeof(int))
            return int.TryParse(stringValue, out var intVal) ? intVal : GetDefaultValueForType(targetType);

        if (underlyingType == typeof(double))
            return double.TryParse(stringValue, out var doubleVal) ? doubleVal : GetDefaultValueForType(targetType);

        if (underlyingType == typeof(string))
            return stringValue;

        if (underlyingType == typeof(ShellTarget))
        {
            return stringValue.ToLowerInvariant() switch
            {
                "bash" => ShellTarget.Bash,
                "powershell" => ShellTarget.PowerShell,
                _ => GetDefaultValueForType(targetType)
            };
        }

        // For unknown types, try Convert.ChangeType
        try
        {
            return Convert.ChangeType(stringValue, underlyingType);
        }
        catch
        {
            return GetDefaultValueForType(targetType);
        }
    }

    /// <summary>
    /// Gets the default value for a type (null for nullable types, default for value types).
    /// </summary>
    private static object? GetDefaultValueForType(Type targetType)
    {
        if (Nullable.GetUnderlyingType(targetType) != null)
            return null;

        return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
    }

    /// <summary>
    /// Applies a field value from SettingsViewModel to WizardViewModel using reflection.
    /// </summary>
    public static void ApplyFieldToWizard(SettingsFieldViewModel field, WizardViewModel wizardVm)
    {
        var propInfo = GetPropertyForKey(field.Key);
        if (propInfo == null || !propInfo.CanWrite)
            return;

        var materializedValue = field.MaterializedValue;

        // Special handling for dataSource.kind (maps to UseSingleFileDataSource bool)
        if (field.Key == "dataSource.kind")
        {
            var isSingleFile = materializedValue?.ToLowerInvariant() switch
            {
                "singlefile" => true,
                "jsonlfile" => true,
                "splitdirectories" => false,
                "directory" => false,
                _ => true // Default
            };
            propInfo.SetValue(wizardVm, isSingleFile);
            return;
        }

        // Special handling for "Unspecified" values (treat as null)
        if (materializedValue == "Unspecified")
            materializedValue = null;

        var propType = propInfo.PropertyType;
        var convertedValue = ConvertValue(materializedValue, propType);
        propInfo.SetValue(wizardVm, convertedValue);
    }

    /// <summary>
    /// Builds a PartialLlamaServerSettings from SettingsViewModel fields using reflection.
    /// </summary>
    public static PartialLlamaServerSettings BuildLlamaServerSettings(
        IEnumerable<SettingsFieldViewModel> fields,
        string prefix, // "llama" or "judge"
        Func<SettingsFieldViewModel, string?> valueSelector)
    {
        var settings = new PartialLlamaServerSettings();
        var settingsType = typeof(PartialLlamaServerSettings);

        foreach (var field in fields)
        {
            if (!field.Key.StartsWith($"{prefix}.", StringComparison.OrdinalIgnoreCase))
                continue;

            var propName = field.Key[$"{prefix}.".Length..];
            // Convert camelCase to PascalCase
            if (propName.Length > 0)
                propName = char.ToUpperInvariant(propName[0]) + propName[1..];

            var propInfo = settingsType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (propInfo == null || !propInfo.CanWrite)
                continue;

            var stringValue = valueSelector(field);
            if (string.IsNullOrEmpty(stringValue) || stringValue == "Unspecified")
                continue;

            var propType = propInfo.PropertyType;
            var convertedValue = ConvertValue(stringValue, propType);

            // Handle ExtraArgs specially (space-separated string → list)
            if (propName == nameof(PartialLlamaServerSettings.ExtraArgs) && convertedValue is string ea)
            {
                var args = ea.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (args.Count > 0)
                    propInfo.SetValue(settings, args);
                continue;
            }

            propInfo.SetValue(settings, convertedValue);
        }

        return settings;
    }

    /// <summary>
    /// Helper to get field value from SettingsViewModel - empty strings are treated as null.
    /// </summary>
    public static string? GetFieldValue(IEnumerable<SettingsFieldViewModel> fields, string key, Func<SettingsFieldViewModel, string?> valueSelector)
    {
        var val = valueSelector(fields.FirstOrDefault(f => f.Key == key)!);
        return string.IsNullOrEmpty(val) || val == "Unspecified" ? null : val;
    }

    /// <summary>
    /// Parses a string value to bool, handling nulls and invalid values.
    /// </summary>
    public static bool? ParseBool(string? value) => value?.ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null
    };

    /// <summary>
    /// Parses a string value to ShellTarget, handling nulls and invalid values.
    /// </summary>
    public static ShellTarget? ParseShellTarget(string? value) => value?.ToLowerInvariant() switch
    {
        "bash" => ShellTarget.Bash,
        "powershell" => ShellTarget.PowerShell,
        _ => null
    };

    /// <summary>
    /// Parses a string value to DataSourceKind, handling nulls and invalid values.
    /// </summary>
    public static DataSourceKind? ParseDataSourceKind(string? value) => value?.ToLowerInvariant() switch
    {
        "singlefile" => DataSourceKind.SingleFile,
        "jsonlfile" => DataSourceKind.JsonlFile,
        "splitdirectories" => DataSourceKind.SplitDirectories,
        "directory" => DataSourceKind.Directory,
        _ => null
    };
}
