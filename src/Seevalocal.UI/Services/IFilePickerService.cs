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
    /// <returns>Selected file path, or null if cancelled.</returns>
    Task<string?> ShowOpenFileDialogAsync(string title, string? filters = null, string? initialDirectory = null);

    /// <summary>
    /// Opens a folder picker dialog and returns the selected folder path.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="initialDirectory">Initial directory to show.</param>
    /// <returns>Selected folder path, or null if cancelled.</returns>
    Task<string?> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null);

    /// <summary>
    /// Opens a file save dialog and returns the selected file path.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="filters">File filters.</param>
    /// <param name="initialFileName">Initial file name.</param>
    /// <returns>Selected file path, or null if cancelled.</returns>
    Task<string?> ShowSaveFileDialogAsync(string title, string? filters = null, string? initialFileName = null);
}
