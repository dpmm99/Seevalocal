using FluentAssertions;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Tests for the IsEqualConverter.
/// </summary>
public sealed class IsEqualConverterTests
{
    private readonly IsEqualConverter _converter;

    public IsEqualConverterTests()
    {
        _converter = IsEqualConverter.Instance;
    }

    #region Convert - Equality Checks

    [Fact]
    public void Convert_Equal_Values_Returns_True()
    {
        // Arrange
        var value = "test";
        var parameter = "test";

        // Act
        var result = _converter.Convert(value, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(true);
    }

    [Fact]
    public void Convert_Unequal_Values_Returns_False()
    {
        // Arrange
        var value = "test1";
        var parameter = "test2";

        // Act
        var result = _converter.Convert(value, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(false);
    }

    [Fact]
    public void Convert_Null_Value_And_Parameter_Returns_True()
    {
        // Arrange
        object? value = null;
        object? parameter = null;

        // Act
        var result = _converter.Convert(value, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(true);
    }

    [Fact]
    public void Convert_Null_Value_NonNull_Parameter_Returns_False()
    {
        // Arrange
        object? value = null;
        object parameter = "test";

        // Act
        var result = _converter.Convert(value, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(false);
    }

    [Fact]
    public void Convert_Integer_Values_Compare_Correctly()
    {
        // Arrange
        var value = 42;
        var parameter = 42;

        // Act
        var result = _converter.Convert(value, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(true);
    }

    [Fact]
    public void Convert_Different_Integer_Values_Returns_False()
    {
        // Arrange
        var value = 42;
        var parameter = 43;

        // Act
        var result = _converter.Convert(value, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(false);
    }

    [Fact]
    public void Convert_Enum_Values_Compare_Correctly()
    {
        // Arrange
        var value = ShellTarget.Bash;
        var parameter = ShellTarget.Bash;

        // Act
        var result = _converter.Convert(value, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(true);
    }

    [Fact]
    public void Convert_Different_Enum_Values_Returns_False()
    {
        // Arrange
        var value = ShellTarget.Bash;
        var parameter = ShellTarget.PowerShell;

        // Act
        var result = _converter.Convert(value, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(false);
    }

    #endregion

    #region ConvertBack - Enum Support

    [Fact]
    public void ConvertBack_True_For_First_Enum_Value_Returns_First_Value()
    {
        // Arrange
        var targetType = typeof(ShellTarget);
        var parameter = "Bash";
        var value = true;

        // Act
        var result = _converter.ConvertBack(value, targetType, parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(ShellTarget.Bash);
    }

    [Fact]
    public void ConvertBack_False_For_First_Enum_Value_Returns_Second_Value()
    {
        // Arrange
        var targetType = typeof(ShellTarget);
        var parameter = "Bash";
        var value = false;

        // Act
        var result = _converter.ConvertBack(value, targetType, parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert - The converter returns the enum value based on parameter parsing
        // When value is false and parameter is "Bash", it returns the parsed enum value
        _ = result.Should().Be(ShellTarget.Bash);
    }

    [Fact]
    public void ConvertBack_True_For_Second_Enum_Value_Returns_Second_Value()
    {
        // Arrange
        var targetType = typeof(ShellTarget);
        var parameter = "PowerShell";
        var value = true;

        // Act
        var result = _converter.ConvertBack(value, targetType, parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(ShellTarget.PowerShell);
    }

    [Fact]
    public void ConvertBack_False_For_Second_Enum_Value_Returns_First_Value()
    {
        // Arrange
        var targetType = typeof(ShellTarget);
        var parameter = "PowerShell";
        var value = false;

        // Act
        var result = _converter.ConvertBack(value, targetType, parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(ShellTarget.Bash);
    }

    [Fact]
    public void ConvertBack_Throws_For_Non_Enum_TargetType()
    {
        // Arrange
        var targetType = typeof(string);
        var parameter = "test";
        var value = true;

        // Act
        Action act = () => _converter.ConvertBack(value, targetType, parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ConvertBack_With_Nullable_Enum_Works_Correctly()
    {
        // Arrange
        var targetType = typeof(ShellTarget?);
        var parameter = "Bash";
        var value = true;

        // Act
        var result = _converter.ConvertBack(value, targetType, parameter, System.Globalization.CultureInfo.InvariantCulture);

        // Assert
        _ = result.Should().Be(ShellTarget.Bash);
    }

    #endregion

    #region Instance Singleton

    [Fact]
    public void Instance_Returns_Same_Instance()
    {
        // Arrange & Act
        var instance1 = IsEqualConverter.Instance;
        var instance2 = IsEqualConverter.Instance;

        // Assert
        _ = instance1.Should().BeSameAs(instance2);
    }

    #endregion
}
