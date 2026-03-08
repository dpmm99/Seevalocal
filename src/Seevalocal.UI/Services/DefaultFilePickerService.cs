using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Controls.ApplicationLifetimes;

namespace Seevalocal.UI.Services;

/// <summary>
/// Avalonia implementation of file picker service.
/// </summary>
public sealed class DefaultFilePickerService(TopLevel? topLevel = null) : IFilePickerService
{
    private readonly TopLevel? _topLevel = topLevel;

    public async Task<string?> ShowOpenFileDialogAsync(string title, string? filters = null, string? initialDirectory = null)
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

        var files = await filePicker.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null)
    {
        var topLevel = _topLevel ?? TopLevel.GetTopLevel(App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        if (topLevel == null) return null;

        var folderPicker = topLevel.StorageProvider;
        
        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        var folders = await folderPicker.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> ShowSaveFileDialogAsync(string title, string? filters = null, string? initialFileName = null)
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

        var file = await filePicker.SaveFilePickerAsync(options);
        return file?.Path.LocalPath;
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
