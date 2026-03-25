namespace Seevalocal.Core.Models;

/// <summary>
/// Types of llama-server settings for CLI argument generation.
/// </summary>
public enum LlamaSettingType
{
    Int,
    Double,
    Bool,
    String,
    BoolLong,  // Boolean with --enable-foo / --no-enable-foo style
}

/// <summary>
/// Marks a property on <see cref="LlamaServerSettings"/> or <see cref="PartialLlamaServerSettings"/>
/// as a llama-server configuration option.
/// Drives automatic CLI argument generation and settings UI registration.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class LlamaSettingAttribute : Attribute
{
    /// <summary>
    /// The CLI flag name (e.g., "--repeat-last-n", "-c", "-ngl").
    /// Null for settings that don't produce CLI args (e.g., ExtraArgs).
    /// </summary>
    public string? CliFlag { get; init; }

    /// <summary>
    /// The settings key suffix for JSON/settings file binding (e.g., "repeatLastNTokens").
    /// Combined with prefix ("llama." or "judge.") to form full key.
    /// </summary>
    public string SettingsKey { get; init; } = "";

    /// <summary>
    /// Display name for UI (e.g., "Repeat Last N", "GPU Layers").
    /// </summary>
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// Description for UI tooltips.
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// The type of this setting for CLI generation.
    /// </summary>
    public LlamaSettingType SettingType { get; init; }

    /// <summary>
    /// For BoolLong type: the --enable-foo flag.
    /// </summary>
    public string? EnableFlag { get; init; }

    /// <summary>
    /// For BoolLong type: the --no-foo flag.
    /// </summary>
    public string? DisableFlag { get; init; }

    public LlamaSettingAttribute(LlamaSettingType settingType, string settingsKey, string displayName, string cliFlag, string description)
    {
        CliFlag = cliFlag;
        SettingsKey = settingsKey;
        DisplayName = displayName;
        Description = description;
        SettingType = settingType;
    }

    public LlamaSettingAttribute(LlamaSettingType settingType, string settingsKey, string displayName, string description)
    {
        SettingsKey = settingsKey;
        DisplayName = displayName;
        Description = description;
        SettingType = settingType;
    }

    /// <summary>
    /// Constructor for BoolLong type settings (with enable/disable flags).
    /// LlamaSettingType parameter is first to make this constructor unique and avoid overload ambiguity.
    /// </summary>
    public LlamaSettingAttribute(LlamaSettingType settingType, string settingsKey, string displayName, string enableFlag, string disableFlag, string description)
    {
        EnableFlag = enableFlag;
        DisableFlag = disableFlag;
        SettingsKey = settingsKey;
        DisplayName = displayName;
        Description = description;
        SettingType = settingType;
        // Note: CliFlag is left null for BoolLong since we use EnableFlag/DisableFlag instead
    }
}
