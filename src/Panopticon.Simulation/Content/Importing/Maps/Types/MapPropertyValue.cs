#nullable enable

using System;

namespace Panopticon.Content.Importing.Maps;

/// <summary>
/// Typed value union for one legacy object property. The closed hierarchy mirrors the five
/// representable legacy property kinds; the legacy <c>Unknown</c> kind is rejected by the
/// adapter and deliberately has no case here.
/// </summary>
public abstract record MapPropertyValue
{
    private MapPropertyValue()
    {
    }

    public sealed record BooleanValue(bool Value) : MapPropertyValue;

    public sealed record IntegerValue(int Value) : MapPropertyValue;

    public sealed record RealValue(float Value) : MapPropertyValue;

    public sealed record AsciiStringValue : MapPropertyValue
    {
        public AsciiStringValue(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public string Value { get; }
    }

    public sealed record UnicodeStringValue : MapPropertyValue
    {
        public UnicodeStringValue(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public string Value { get; }
    }
}
