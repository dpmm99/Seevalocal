using Avalonia.Threading;
using System.Collections.ObjectModel;

namespace Seevalocal.UI.Services;

/// <summary>
/// Implementation of toast notification service.
/// </summary>
public sealed class ToastService : IToastService
{
    /// <summary>
    /// Collection of active toasts. Bind to this to display toasts in the UI.
    /// </summary>
    public ObservableCollection<ToastMessage> Toasts { get; } = [];

    /// <summary>
    /// Shows a toast notification with the specified message.
    /// </summary>
    public void Show(string message, int duration = 3000)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ToastMessage(message, ToastKind.Info, duration, this);
            Toasts.Add(toast);
            toast.StartTimer();
        });
    }

    /// <summary>
    /// Shows an error toast notification.
    /// </summary>
    public void ShowError(string message, int duration = 5000)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ToastMessage(message, ToastKind.Error, duration, this);
            Toasts.Add(toast);
            toast.StartTimer();
        });
    }

    /// <summary>
    /// Shows a success toast notification.
    /// </summary>
    public void ShowSuccess(string message, int duration = 3000)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ToastMessage(message, ToastKind.Success, duration, this);
            Toasts.Add(toast);
            toast.StartTimer();
        });
    }

    /// <summary>
    /// Dismisses a toast notification.
    /// </summary>
    internal void Dismiss(ToastMessage toast)
    {
        Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
    }
}

/// <summary>
/// Represents a toast message.
/// </summary>
public sealed class ToastMessage(string message, ToastKind kind, int duration, ToastService service)
{
    private Timer? _timer;
    private bool _isClosing;
    private bool _isDisposed;

    public string Message { get; } = message;
    public ToastKind Kind { get; } = kind;
    public int Duration { get; } = duration;
    public bool IsVisible { get; private set; } = true;

    internal void StartTimer()
    {
        _timer = new Timer(_ =>
        {
            if (!_isClosing && !_isDisposed)
            {
                _isClosing = true;
                IsVisible = false;
                // Notify the service to remove this toast after fade out
                Task.Delay(200).ContinueWith(_ =>
                {
                    if (!_isDisposed)
                    {
                        service.Dismiss(this);
                    }
                }, TaskScheduler.Default);
            }
        }, null, Duration, Timeout.Infinite);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _timer?.Dispose();
        }
    }
}

/// <summary>
/// The kind of toast notification.
/// </summary>
public enum ToastKind
{
    Info,
    Success,
    Error
}
