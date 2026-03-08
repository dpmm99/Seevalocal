using Seevalocal.Core.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Seevalocal.UI.ViewModels;

// ─── Settings stack ──────────────────────────────────────────────────────────

/// <summary>
/// Represents a single loaded settings file in the layered stack.
/// </summary>
public sealed class SettingsLayerViewModel(string filePath, int layerIndex, PartialConfig config) : INotifyPropertyChanged
{
    private bool _isEnabled = true;

    public string FilePath { get; } = filePath;
    public string DisplayName => Path.GetFileName(FilePath);
    public int LayerIndex { get; } = layerIndex;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public PartialConfig Config { get; } = config;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private bool SetField<T>(ref T f, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(f, v)) return false;
        f = v; OnPropertyChanged(n); return true;
    }
}
