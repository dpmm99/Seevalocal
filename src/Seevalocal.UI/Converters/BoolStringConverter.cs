using Avalonia.Data.Converters;
using System.Globalization;

namespace Seevalocal.UI.Converters;

/// <summary>
/// Converts between bool and string ("true"/"false") for checkbox bindings.
/// </summary>
public sealed class BoolStringConverter : IValueConverter
{
    public static readonly BoolStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s ? s.Equals("true", StringComparison.InvariantCultureIgnoreCase) : value is bool b && b;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? b.ToString().ToLowerInvariant() : "false";
    }
}
