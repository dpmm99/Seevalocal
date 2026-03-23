namespace Seevalocal.UI.Services;

/// <summary>
/// Service for opening file picker dialogs.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Opens a file open dialog and returns the selected file path.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="filters">File filters (e.g., "Model Files|*.gguf|All Files|*.*").</param>
    /// <param name="initialDirectory">Initial directory to show.</param>
    /// <param name="dialogIdentifier">Optional identifier for the dialog type (e.g., "llama-server", "model-file", "output", "settings", "data-file"). Used by Windows to remember last used directory.</param>
    /// <returns>Selected file path, or null if cancelled.</returns>
    Task<string?> ShowOpenFileDialogAsync(string title, string? filters = null, string? initialDirectory = null, string? dialogIdentifier = null);

    /// <summary>
    /// Opens a folder picker dialog and returns the selected folder path.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="initialDirectory">Initial directory to show.</param>
    /// <param name="dialogIdentifier">Optional identifier for the dialog type. Used by Windows to remember last used directory.</param>
    /// <returns>Selected folder path, or null if cancelled.</returns>
    Task<string?> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null, string? dialogIdentifier = null);

    /// <summary>
    /// Opens a file save dialog and returns the selected file path.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="filters">File filters.</param>
    /// <param name="initialFileName">Initial file name.</param>
    /// <param name="dialogIdentifier">Optional identifier for the dialog type. Used by Windows to remember last used directory.</param>
    /// <returns>Selected file path, or null if cancelled.</returns>
    Task<string?> ShowSaveFileDialogAsync(string title, string? filters = null, string? initialFileName = null, string? dialogIdentifier = null);
}
