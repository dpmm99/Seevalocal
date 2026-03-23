using System.Collections.Concurrent;
using System.Text.Json;

namespace Seevalocal.UI.Services;

/// <summary>
/// Persists last-used directories for file/folder dialogs by identifier.
/// On Windows, this supplements the OS's built-in dialog memory for cases where that's not sufficient.
/// </summary>
public interface IDialogDirectoryService
{
    /// <summary>
    /// Gets the last-used directory for a dialog identifier.
    /// </summary>
    /// <param name="dialogIdentifier">Dialog identifier (e.g., "llama-server", "model-file", "output", "data-file", "settings").</param>
    /// <returns>Last-used directory path, or null if none saved.</returns>
    string? GetLastDirectory(string? dialogIdentifier);

    /// <summary>
    /// Saves the last-used directory for a dialog identifier.
    /// </summary>
    /// <param name="dialogIdentifier">Dialog identifier.</param>
    /// <param name="directoryPath">Directory path to save.</param>
    void SaveLastDirectory(string? dialogIdentifier, string? directoryPath);
}

/// <summary>
/// In-memory implementation of IDialogDirectoryService.
/// Persists directories to a JSON file in the user's app data folder.
/// </summary>
public sealed class DialogDirectoryService : IDialogDirectoryService
{
    private readonly ConcurrentDictionary<string, string> _directories = new();
    private readonly string _storagePath;
    private readonly object _lock = new();

    public DialogDirectoryService()
    {
        // Store in user's local app data folder
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Seevalocal");

        Directory.CreateDirectory(appDataPath);
        _storagePath = Path.Combine(appDataPath, "dialog_directories.json");

        LoadFromDisk();
    }

    public string? GetLastDirectory(string? dialogIdentifier)
    {
        if (string.IsNullOrEmpty(dialogIdentifier))
            return null;

        return _directories.TryGetValue(dialogIdentifier, out var path) ? path : null;
    }

    public void SaveLastDirectory(string? dialogIdentifier, string? directoryPath)
    {
        if (string.IsNullOrEmpty(dialogIdentifier) || string.IsNullOrEmpty(directoryPath))
            return;

        _directories[dialogIdentifier] = directoryPath;
        SaveToDisk();
    }

    private void LoadFromDisk()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                var json = File.ReadAllText(_storagePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        _directories[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore load errors - start fresh
        }
    }

    private void SaveToDisk()
    {
        try
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(_directories, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_storagePath, json);
            }
        }
        catch (Exception)
        {
            // Ignore save errors - in-memory cache still works
        }
    }
}
