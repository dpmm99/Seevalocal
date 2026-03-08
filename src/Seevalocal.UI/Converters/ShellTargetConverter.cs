using Avalonia.Data.Converters;
using Seevalocal.Core.Models;
using System.Globalization;

namespace Seevalocal.UI.Converters;

/// <summary>
/// Converts between ShellTarget? enum and boolean for radio button binding.
/// Returns true if the ShellTarget equals the parameter, false otherwise.
/// Handles null by returning false for all options.
/// </summary>
public sealed class ShellTargetConverter : IValueConverter
{
    public static readonly ShellTargetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string paramString)
            return false;

        if (value is ShellTarget shellTarget)
        {
            if (Enum.TryParse<ShellTarget>(paramString, out var target))
            {
                return shellTarget == target;
            }
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool isChecked || parameter is not string paramString)
            return null;

        if (!Enum.TryParse<ShellTarget>(paramString, out var target))
            return null;

        // Only return the target if checked, otherwise return null (don't unset)
        return isChecked ? target : null;
    }
}
