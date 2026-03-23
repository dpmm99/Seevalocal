using Avalonia.Data.Converters;
using System.Globalization;

namespace Seevalocal.UI.Converters;

/// <summary>
/// Converts by comparing the value to a parameter using Equals.
/// </summary>
public sealed class IsEqualConverter : IValueConverter
{
    public static readonly IsEqualConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Equals(value, parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var nullableBaseType = Nullable.GetUnderlyingType(targetType);
        if (nullableBaseType?.IsEnum == true) targetType = nullableBaseType;
        if (targetType.IsEnum && parameter is string paramString && value is bool valueBool)
        {
            var enumVal = Enum.Parse(targetType, paramString);
            if (valueBool) return enumVal;
            else
            {
                var options = Enum.GetValues(targetType);
                if (options.Length == 2)
                {
                    return options.GetValue(0) == enumVal ? options.GetValue(1) : options.GetValue(0);
                }
            }
        }
        throw new NotSupportedException();
    }
}
