using Seevalocal.Core.Models;
using System.ComponentModel;

namespace Seevalocal.UI.ViewModels;

public interface IWizardViewModel : INotifyPropertyChanged
{
    // Navigation / commands
    public System.Windows.Input.ICommand GoBackCommand { get; }
    public System.Windows.Input.ICommand GoForwardCommand { get; }
    public System.Windows.Input.ICommand ExportScriptCommand { get; }

    // Browse / test commands
    public System.Windows.Input.ICommand BrowseLocalModelCommand { get; }
    public System.Windows.Input.ICommand BrowseDataFileCommand { get; }
    public System.Windows.Input.ICommand BrowsePromptDirCommand { get; }
    public System.Windows.Input.ICommand BrowseExpectedDirCommand { get; }
    public System.Windows.Input.ICommand BrowseOutputDirCommand { get; }
    public System.Windows.Input.ICommand TestConnectionCommand { get; }

    // State (only the members MainWindowViewModel / App need)
    public ShellTarget? ShellTarget { get; }
    public string? RunName { get; }
    public string? OutputDirectoryPath { get; }

    // Build partial config
    public PartialConfig BuildPartialConfig();

    // Reset to defaults
    public void ResetToDefaults();

    // Callbacks set by MainWindow/App
    public Action? OnExportScript { get; set; }
    public Func<Task>? OnStartRun { get; set; }
    public Action<string>? OnShowNotification { get; set; }
}