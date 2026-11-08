#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>JSON converter for typed map package property values.</summary>
public sealed class MapPackagePropertyValueJsonConverter : JsonConverter<MapPackagePropertyValue>
{
    public override MapPackagePropertyValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Property value must be a JSON object.");
        }

        string? type = null;
        JsonElement? valueElement = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name.");
            }

            var propName = reader.GetString();
            reader.Read();

            if (string.Equals(propName, "type", StringComparison.OrdinalIgnoreCase))
            {
                type = reader.GetString();
            }
            else if (string.Equals(propName, "value", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                valueElement = doc.RootElement.Clone();
            }
            else
            {
                throw new JsonException($"The JSON property '{propName}' could not be mapped to any .NET object member of type 'MapPackagePropertyValue'.");
            }
        }

        if (type is null)
        {
            throw new JsonException("Property value is missing required 'type' field.");
        }

        if (valueElement is null)
        {
            throw new JsonException("Property value is missing required 'value' field.");
        }

        var element = valueElement.Value;

        return type switch
        {
            "boolean" => new MapPackagePropertyValue.BooleanValue(element.GetBoolean()),
            "integer" => new MapPackagePropertyValue.IntegerValue(element.GetInt32()),
            "real" => new MapPackagePropertyValue.RealValue(element.GetSingle()),
            "asciiString" => new MapPackagePropertyValue.AsciiStringValue(element.GetString() ?? string.Empty),
            "unicodeString" => new MapPackagePropertyValue.UnicodeStringValue(element.GetString() ?? string.Empty),
            _ => throw new JsonException($"Unknown property type '{type}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, MapPackagePropertyValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        writer.WritePropertyName("value");

        switch (value)
        {
            case MapPackagePropertyValue.BooleanValue b:
                writer.WriteBooleanValue(b.Value);
                break;

            case MapPackagePropertyValue.IntegerValue i:
                writer.WriteNumberValue(i.Value);
                break;

            case MapPackagePropertyValue.RealValue r:
                writer.WriteNumberValue(r.Value);
                break;

            case MapPackagePropertyValue.AsciiStringValue a:
                writer.WriteStringValue(a.Value);
                break;

            case MapPackagePropertyValue.UnicodeStringValue u:
                writer.WriteStringValue(u.Value);
                break;

            default:
                throw new JsonException($"Unknown property value type '{value.GetType()}'.");
        }

        writer.WriteEndObject();
    }
}
