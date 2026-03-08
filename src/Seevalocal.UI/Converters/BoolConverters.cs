using Avalonia.Data.Converters;
using System.Globalization;

namespace Seevalocal.UI.Converters;

/// <summary>
/// Converts bool to "Managing locally" / "Connecting to existing" text.
/// </summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public static readonly BoolToTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? b ? "Managing locally" : "Connecting to existing" : "—";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s ? s switch
        {
            "Managing locally" => true,
            "Connecting to existing" => false,
            _ => null
        } : null;
    }
}

/// <summary>
/// Converts bool to "Yes" / "No" text.
/// </summary>
public sealed class BoolToYesNoConverter : IValueConverter
{
    public static readonly BoolToYesNoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? b ? "Yes" : "No" : "—";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s ? s switch
        {
            "Yes" => true,
            "No" => false,
            _ => null
        } : null;
    }
}

/// <summary>
/// Converts bool? to display text: null = "Use llama.cpp default", true = "true", false = "false".
/// For three-state boolean settings where null means "unspecified/use default".
/// </summary>
public sealed class ThreeStateBoolConverter : IValueConverter
{
    public static readonly ThreeStateBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b.ToString().ToLowerInvariant();
        return null; // null = unspecified
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            if (s.Equals("true", StringComparison.InvariantCultureIgnoreCase)) return true;
            if (s.Equals("false", StringComparison.InvariantCultureIgnoreCase)) return false;
        }
        return null; // unspecified
    }
}

/// <summary>
/// Converts bool? to display text for three-state settings:
/// null = "Unspecified (use default)", true = "Enabled", false = "Disabled"
/// </summary>
public sealed class ThreeStateBoolTextConverter : IValueConverter
{
    public static readonly ThreeStateBoolTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            true => "Enabled",
            false => "Disabled",
            null => "Unspecified (use default)",
            _ => "—"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            "Enabled" => true,
            "Disabled" => false,
            "Unspecified (use default)" => null,
            _ => null
        };
    }
}
