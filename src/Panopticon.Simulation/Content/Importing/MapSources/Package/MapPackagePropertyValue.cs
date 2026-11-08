#nullable enable

using System;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Typed property value for legacy object properties in the package manifest.</summary>
[JsonConverter(typeof(MapPackagePropertyValueJsonConverter))]
public abstract record MapPackagePropertyValue
{
    private MapPackagePropertyValue()
    {
    }

    public abstract string Type { get; }

    public sealed record BooleanValue(bool Value) : MapPackagePropertyValue
    {
        public override string Type => "boolean";
    }

    public sealed record IntegerValue(int Value) : MapPackagePropertyValue
    {
        public override string Type => "integer";
    }

    public sealed record RealValue(float Value) : MapPackagePropertyValue
    {
        public override string Type => "real";
    }

    public sealed record AsciiStringValue : MapPackagePropertyValue
    {
        public AsciiStringValue(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public override string Type => "asciiString";

        public string Value { get; }
    }

    public sealed record UnicodeStringValue : MapPackagePropertyValue
    {
        public UnicodeStringValue(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public override string Type => "unicodeString";

        public string Value { get; }
    }

    public static MapPackagePropertyValue FromModel(MapPropertyValue property) => property switch
    {
        MapPropertyValue.BooleanValue b => new BooleanValue(b.Value),
        MapPropertyValue.IntegerValue i => new IntegerValue(i.Value),
        MapPropertyValue.RealValue r => new RealValue(r.Value),
        MapPropertyValue.AsciiStringValue a => new AsciiStringValue(a.Value),
        MapPropertyValue.UnicodeStringValue u => new UnicodeStringValue(u.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    public MapPropertyValue ToModel() => this switch
    {
        BooleanValue b => new MapPropertyValue.BooleanValue(b.Value),
        IntegerValue i => new MapPropertyValue.IntegerValue(i.Value),
        RealValue r => new MapPropertyValue.RealValue(r.Value),
        AsciiStringValue a => new MapPropertyValue.AsciiStringValue(a.Value),
        UnicodeStringValue u => new MapPropertyValue.UnicodeStringValue(u.Value),
        _ => throw new InvalidOperationException($"Unknown property type '{Type}'."),
    };
}
