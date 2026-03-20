using Avalonia.Data.Converters;
using System.Globalization;

namespace Seevalocal.UI.Converters;

/// <summary>
/// Converts string to true if not null or empty, false otherwise.
/// </summary>
public sealed class IsNotNullOrEmptyConverter : IValueConverter
{
    public static readonly IsNotNullOrEmptyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
