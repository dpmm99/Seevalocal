using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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
}
