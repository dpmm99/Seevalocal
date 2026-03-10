using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Linq;

namespace Seevalocal.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void CopyTextToClipboard(object? sender, RoutedEventArgs e)
    {
        string? text = null;

        if (sender is MenuItem menuItem)
        {
            // Navigate up to the ContextMenu, then get its PlacementTarget
            var contextMenu = menuItem.GetVisualAncestors().OfType<ContextMenu>().FirstOrDefault();
            if (contextMenu?.PlacementTarget is TextBlock textBlock)
            {
                text = textBlock.Text;
            }
        }

        if (!string.IsNullOrEmpty(text))
        {
            CopyToClipboard(text);
        }
    }

    private static void CopyToClipboard(string text)
    {
        TopLevel.GetTopLevel(App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null)?
            .Clipboard?.SetTextAsync(text);
    }
}
