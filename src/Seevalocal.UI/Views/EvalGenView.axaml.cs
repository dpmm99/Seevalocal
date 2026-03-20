using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Seevalocal.Core.Models;
using Seevalocal.UI.ViewModels;
using System.ComponentModel;

namespace Seevalocal.UI.Views;

public partial class EvalGenView : UserControl
{
    public EvalGenView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private EvalGenViewModel? _viewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel?.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as EvalGenViewModel;
        _viewModel?.PropertyChanged += OnViewModelPropertyChanged;
    }

    // This absurdity was required to get the prompt textboxes to update when the user clears them and then switches focus to another field.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null) return;

        switch (e.PropertyName)
        {
            case nameof(EvalGenViewModel.Phase1Prompt):
                Dispatcher.UIThread.Post(() => Phase1PromptBox.Text = _viewModel.Phase1Prompt);
                break;
            case nameof(EvalGenViewModel.Phase2Prompt):
                Dispatcher.UIThread.Post(() => Phase2PromptBox.Text = _viewModel.Phase2Prompt);
                break;
            case nameof(EvalGenViewModel.Phase3Prompt):
                Dispatcher.UIThread.Post(() => Phase3PromptBox.Text = _viewModel.Phase3Prompt);
                break;
        }
    }

    // Context menu click handlers - use Click instead of Command to avoid scrolling issues
    private void CopyCategoryNameMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GeneratedCategoryViewModel category)
        {
            CopyToClipboard(category.Name);
        }
    }

    private void CopyProblemMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GeneratedProblem problem)
        {
            CopyToClipboard(problem.OneLineStatement);
        }
    }

    private void CopyPromptMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GeneratedProblem problem && !string.IsNullOrEmpty(problem.FullPrompt))
        {
            CopyToClipboard(problem.FullPrompt);
        }
    }

    private void CopyExpectedOutputMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is GeneratedProblem problem && !string.IsNullOrEmpty(problem.ExpectedOutput))
        {
            CopyToClipboard(problem.ExpectedOutput);
        }
    }

    private void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Clipboard?.SetTextAsync(text);
            }
        }
        catch
        {
            // Silently fail - clipboard access may not be available
        }
    }
}
