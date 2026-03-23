namespace Seevalocal.Core;

/// <summary>
/// Specifies the default value to use during config merging when no partial config
/// supplies a value for this property. Place on properties of record types that
/// participate in merged config (e.g. <see cref="Models.JudgeConfig"/>, <see cref="Models.DataSourceConfig"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class MergeDefaultAttribute(object? value) : Attribute
{
    public object? Value { get; } = value;
}