using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Geospatial.Manifest;

internal static class ManifestTokens
{
    public static string Kebab<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToString())
        {
            if (char.IsUpper(character))
            {
                if (builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}

internal sealed class ManifestKebabEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum && typeToConvert.Namespace == typeof(ManifestKebabEnumConverterFactory).Namespace;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(ManifestKebabEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

internal sealed class ManifestKebabEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly Dictionary<string, TEnum> _valuesByToken;
    private readonly Dictionary<TEnum, string> _tokensByValue;

    public ManifestKebabEnumConverter()
    {
        _valuesByToken = [];
        _tokensByValue = [];
        foreach (TEnum value in Enum.GetValues<TEnum>())
        {
            var token = ManifestTokens.Kebab(value);
            _valuesByToken.Add(token, value);
            _tokensByValue.Add(value, token);
        }
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a string token for {typeof(TEnum).Name}.");
        }

        var token = reader.GetString()!;
        if (!_valuesByToken.TryGetValue(token, out var value))
        {
            var expected = string.Join(", ", _valuesByToken.Keys);
            throw new JsonException($"Unknown {typeof(TEnum).Name} token '{token}'; expected one of: {expected}.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(_tokensByValue[value]);
}
