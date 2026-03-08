using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Seevalocal.UI;

/// <summary>
/// Converts boolean success status to a background color for status badges.
/// </summary>
public sealed class StatusToBackgroundConverter : IValueConverter
{
    public static readonly StatusToBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool succeeded)
        {
            return succeeded 
                ? new SolidColorBrush(Color.Parse("#4CAF50"))  // Green for success
                : new SolidColorBrush(Color.Parse("#F44336")); // Red for failure
        }
        return new SolidColorBrush(Color.Parse("#7F849C")); // Gray for unknown
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Returns true if the value is not null.
/// </summary>
public sealed class IsNotNullConverter : IValueConverter
{
    public static readonly IsNotNullConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Returns true if the integer value is greater than 10.
/// </summary>
public sealed class IsGreaterThan10Converter : IValueConverter
{
    public static readonly IsGreaterThan10Converter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 10;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
