using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Seevalocal.UI.Converters;

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
        return value is int count && count > 10;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Returns true if the integer value is greater than the specified parameter.
/// For use with view models that have an EarlyCompletionsLimit property.
/// The parameter is ignored; this converter expects the DataContext to be a view model
/// with an EarlyCompletionsLimit property when used in a binding.
/// </summary>
public sealed class IsGreaterThanConverter : IValueConverter
{
    public static readonly IsGreaterThanConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // value is Results.Count, parameter is the property name "EarlyCompletionsLimit"
        // We need to get the limit from the target object's context
        // Since we can't easily access the DataContext here, we'll use a simpler approach:
        // The parameter will be a string like "EarlyCompletionsLimit" and we'll need to 
        // handle this differently in the XAML using a MultiBinding or by exposing a 
        // computed property on the view model.

        // For now, this is a placeholder - the actual visibility will be controlled
        // by the command's CanExecute in the button
        if (value is int count)
        {
            // Default behavior: show if count > 10 (fallback)
            return count > 10;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
