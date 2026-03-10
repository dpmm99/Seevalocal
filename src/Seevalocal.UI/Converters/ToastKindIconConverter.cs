using Avalonia.Data.Converters;
using Seevalocal.UI.Services;
using System.Globalization;

namespace Seevalocal.UI.Converters;

/// <summary>
/// Converts a ToastKind to an emoji icon for display in toast notifications.
/// </summary>
public sealed class ToastKindIconConverter : IValueConverter
{
    public static readonly ToastKindIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ToastKind.Success => "✅",
            ToastKind.Error => "❌",
            ToastKind.Info => "ℹ️",
            _ => "ℹ️"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
