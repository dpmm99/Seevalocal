using Avalonia.Data.Converters;
using System.Globalization;

namespace Seevalocal.UI.Converters;

/// <summary>
/// Converts between nullable numeric types and strings for TextBox bindings.
/// An empty or whitespace string converts to null; null converts to an empty string.
/// Supports int?, double?, and any other IConvertible nullable struct.
/// </summary>
public sealed class NullableValueConverter : IValueConverter
{
    public static readonly NullableValueConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? "" : value.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Unwrap Nullable<T> to get the underlying type
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            return System.Convert.ChangeType(text.Trim(), underlying, culture);
        }
        catch
        {
            // Return UnsetValue so Avalonia ignores invalid input mid-typing
            return Avalonia.Data.BindingOperations.DoNothing;
        }
    }
}