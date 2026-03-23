using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Seevalocal.UI.Services;

/// <summary>
/// Avalonia implementation of file picker service.
/// </summary>
public sealed class DefaultFilePickerService(TopLevel? topLevel = null, IDialogDirectoryService? directoryService = null) : IFilePickerService
{
    private readonly TopLevel? _topLevel = topLevel;
    private readonly IDialogDirectoryService? _directoryService = directoryService;

    public async Task<string?> ShowOpenFileDialogAsync(string title, string? filters = null, string? initialDirectory = null, string? dialogIdentifier = null)
    {
        var topLevel = _topLevel ?? TopLevel.GetTopLevel(App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (topLevel == null) return null;

        var filePicker = topLevel.StorageProvider;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (!string.IsNullOrEmpty(filters))
        {
            options.FileTypeFilter = ParseFilters(filters);
        }

        // Load last-used directory for this dialog identifier
        var startLocation = await GetStartLocationAsync(filePicker, initialDirectory, dialogIdentifier);
        if (startLocation != null)
        {
            options.SuggestedStartLocation = startLocation;
        }

        var files = await filePicker.OpenFilePickerAsync(options);
        var result = files.Count > 0 ? files[0].Path.LocalPath : null;

        // Save the directory for this dialog identifier
        if (!string.IsNullOrEmpty(result))
        {
            var directory = Path.GetDirectoryName(result);
            _directoryService?.SaveLastDirectory(dialogIdentifier, directory);
        }

        return result;
    }

    public async Task<string?> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null, string? dialogIdentifier = null)
    {
        var topLevel = _topLevel ?? TopLevel.GetTopLevel(App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (topLevel == null) return null;

        var folderPicker = topLevel.StorageProvider;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        // Load last-used directory for this dialog identifier
        var startLocation = await GetStartLocationAsync(folderPicker, initialDirectory, dialogIdentifier);
        if (startLocation != null)
        {
            options.SuggestedStartLocation = startLocation;
        }

        var folders = await folderPicker.OpenFolderPickerAsync(options);
        var result = folders.Count > 0 ? folders[0].Path.LocalPath : null;

        // Save the directory for this dialog identifier
        _directoryService?.SaveLastDirectory(dialogIdentifier, result);

        return result;
    }

    public async Task<string?> ShowSaveFileDialogAsync(string title, string? filters = null, string? initialFileName = null, string? dialogIdentifier = null)
    {
        var topLevel = _topLevel ?? TopLevel.GetTopLevel(App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (topLevel == null) return null;

        var filePicker = topLevel.StorageProvider;

        var options = new FilePickerSaveOptions
        {
            Title = title
        };

        if (!string.IsNullOrEmpty(initialFileName))
        {
            options.SuggestedFileName = initialFileName;
        }

        if (!string.IsNullOrEmpty(filters))
        {
            options.FileTypeChoices = ParseFilters(filters);
        }

        // Load last-used directory for this dialog identifier
        var startLocation = await GetStartLocationAsync(filePicker, null, dialogIdentifier);
        if (startLocation != null)
        {
            options.SuggestedStartLocation = startLocation;
        }

        var file = await filePicker.SaveFilePickerAsync(options);
        var result = file?.Path.LocalPath;

        // Save the directory for this dialog identifier
        if (!string.IsNullOrEmpty(result))
        {
            var directory = Path.GetDirectoryName(result);
            _directoryService?.SaveLastDirectory(dialogIdentifier, directory);
        }

        return result;
    }

    private async Task<IStorageFolder?> GetStartLocationAsync(IStorageProvider storageProvider, string? initialDirectory, string? dialogIdentifier)
    {
        // Priority: 1. Explicit initialDirectory, 2. Saved directory for identifier, 3. null (use default)
        if (!string.IsNullOrEmpty(initialDirectory))
        {
            return await storageProvider.TryGetFolderFromPathAsync(initialDirectory);
        }

        var savedDirectory = _directoryService?.GetLastDirectory(dialogIdentifier);
        if (!string.IsNullOrEmpty(savedDirectory))
        {
            return await storageProvider.TryGetFolderFromPathAsync(savedDirectory);
        }

        return null;
    }

    private static List<FilePickerFileType> ParseFilters(string filters)
    {
        var result = new List<FilePickerFileType>();
        var parts = filters.Split('|');

        for (int i = 0; i < parts.Length; i += 2)
        {
            if (i + 1 < parts.Length)
            {
                var name = parts[i];
                var patterns = parts[i + 1].Split(';');
                result.Add(new FilePickerFileType(name)
                {
                    Patterns = patterns
                });
            }
        }

        if (result.Count == 0)
        {
            result.Add(FilePickerFileTypes.All);
        }

        return result;
    }
}
