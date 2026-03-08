using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seevalocal.Core.Models;

/// <summary>
/// A typed, named measurement emitted by a pipeline stage.
/// RULE: Name MUST end with a unit suffix (Seconds, Count, Bytes, Ratio, Percent, etc.).
/// See 00-conventions.md §2.1 for the full list.
/// </summary>
public record MetricValue
{
    /// <summary>Name of the metric, including unit suffix. PascalCase.</summary>
    public string Name { get; init; } = "";

    /// <summary>The actual value.</summary>
    public MetricScalar Value { get; init; } = new MetricScalar.StringMetric("");

    /// <summary>
    /// Optional group/stage that produced this metric, for disambiguation.
    /// E.g., "CompileStage", "JudgeStage".
    /// </summary>
    public string? SourceStage { get; init; }
}

/// <summary>Discriminated union of all metric scalar types.</summary>
[JsonConverter(typeof(MetricScalarJsonConverter))]
public abstract record MetricScalar
{
    public sealed record IntMetric(int Value) : MetricScalar;
    public sealed record DoubleMetric(double Value) : MetricScalar;
    public sealed record BoolMetric(bool Value) : MetricScalar;
    public sealed record StringMetric(string Value) : MetricScalar;

    private MetricScalar() { }
}

/// <summary>
/// JSON converter for MetricScalar discriminated union.
/// Serializes to: { "type": "int|double|bool|string", "value": ... }
/// </summary>
public class MetricScalarJsonConverter : JsonConverter<MetricScalar>
{
    public override MetricScalar? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject for MetricScalar");

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp) ||
            !root.TryGetProperty("value", out var valueProp))
            throw new JsonException("MetricScalar must have 'type' and 'value' properties");

        var type = typeProp.GetString() ?? throw new JsonException("MetricScalar type must be a string");

        return type switch
        {
            "int" => new MetricScalar.IntMetric(valueProp.GetInt32()),
            "double" => new MetricScalar.DoubleMetric(valueProp.GetDouble()),
            "bool" => new MetricScalar.BoolMetric(valueProp.GetBoolean()),
            "string" => new MetricScalar.StringMetric(valueProp.GetString() ?? ""),
            _ => throw new JsonException($"Unknown MetricScalar type: {type}")
        };
    }

    public override void Write(Utf8JsonWriter writer, MetricScalar value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        switch (value)
        {
            case MetricScalar.IntMetric m:
                writer.WriteString("type", "int");
                writer.WriteNumber("value", m.Value);
                break;
            case MetricScalar.DoubleMetric m:
                writer.WriteString("type", "double");
                writer.WriteNumber("value", m.Value);
                break;
            case MetricScalar.BoolMetric m:
                writer.WriteString("type", "bool");
                writer.WriteBoolean("value", m.Value);
                break;
            case MetricScalar.StringMetric m:
                writer.WriteString("type", "string");
                writer.WriteString("value", m.Value);
                break;
            default:
                throw new JsonException($"Unknown MetricScalar type: {value.GetType().Name}");
        }

        writer.WriteEndObject();
    }
}

public static class MetricScalarExtensions
{
    public static object? ToObject(this MetricScalar m) => m switch
    {
        MetricScalar.IntMetric(var v) => v,
        MetricScalar.DoubleMetric(var v) => v,
        MetricScalar.BoolMetric(var v) => v,
        MetricScalar.StringMetric(var v) => v,
        _ => null
    };
}

public enum MetricType { Int, Double, Bool, String }
