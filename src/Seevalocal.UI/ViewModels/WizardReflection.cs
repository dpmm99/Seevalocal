using Seevalocal.Core.Models;

namespace Seevalocal.UI.ViewModels;

/// <summary>
/// Reflection helpers for wizard config operations.
///
/// Most of the heavy lifting has moved into <see cref="WizardState.ApplyResolvedConfig"/>.
/// This class retains only the parts that are called from outside the view-model pair
/// (e.g. from <c>MainWindow</c>) or that provide generic utility.
/// </summary>
public static class WizardReflection
{
    /// <summary>
    /// Syncs config values into a wizard view model, skipping fields the user has already edited.
    /// Delegates to <see cref="WizardState.ApplyResolvedConfig"/> via the view model's
    /// <c>SyncDefaultsFromSettings</c> method so that all logic stays in one place.
    /// </summary>
    public static void SyncConfigToFields(WizardViewModel viewModel, ResolvedConfig config, HashSet<string> editedFields)
        => WizardState.ApplyResolvedConfig(viewModel.State, config, editedFields, onlyUnedited: true);

    /// <summary>
    /// Populates a wizard view model from a checkpoint config, marking all loaded fields as edited.
    /// </summary>
    public static void PopulateFromCheckpointConfig(WizardViewModel viewModel, ResolvedConfig config, HashSet<string> editedFields)
        => WizardState.ApplyResolvedConfig(viewModel.State, config, editedFields, onlyUnedited: false);

    /// <summary>
    /// Gets a config value using a dot-separated property path (e.g. "LlamaServer.ContextWindowTokens").
    /// </summary>
    public static object? GetConfigValue(object config, string path)
    {
        object? current = config;
        foreach (var part in path.Split('.'))
        {
            if (current == null) return null;
            current = current.GetType().GetProperty(part)?.GetValue(current);
        }
        return current;
    }

    /// <summary>
    /// Sets a config value using a dot-separated property path.
    /// </summary>
    public static void SetConfigValue(object config, string path, object? value)
    {
        if (value == null) return;
        var parts = path.Split('.');
        object? current = config;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            current = current?.GetType().GetProperty(parts[i])?.GetValue(current);
            if (current == null) return;
        }

        var finalProp = current?.GetType().GetProperty(parts[^1]);
        if (finalProp is not { CanWrite: true }) return;

        var targetType = finalProp.PropertyType;
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            finalProp.SetValue(current, value.GetType() == underlying ? value : Convert.ChangeType(value, underlying));
        }
        catch { /* incompatible type — silently skip */ }
    }
}