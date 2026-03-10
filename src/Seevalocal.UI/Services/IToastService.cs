namespace Seevalocal.UI.Services;

/// <summary>
/// Service for displaying toast notifications to the user.
/// </summary>
public interface IToastService
{
    /// <summary>
    /// Shows a toast notification with the specified message.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="duration">Optional duration in milliseconds. Defaults to 3000ms.</param>
    void Show(string message, int duration = 3000);

    /// <summary>
    /// Shows an error toast notification.
    /// </summary>
    /// <param name="message">The error message to display.</param>
    /// <param name="duration">Optional duration in milliseconds. Defaults to 5000ms.</param>
    void ShowError(string message, int duration = 5000);

    /// <summary>
    /// Shows a success toast notification.
    /// </summary>
    /// <param name="message">The success message to display.</param>
    /// <param name="duration">Optional duration in milliseconds. Defaults to 3000ms.</param>
    void ShowSuccess(string message, int duration = 3000);
}
