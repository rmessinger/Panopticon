#nullable enable

using System;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// An optionally authored value. Omitted and an explicit zero or empty value stay distinct, so
/// inheritance can tell "not written" from "written as zero". <see cref="Origin"/> names the
/// record that supplied the value once inheritance has run.
/// </summary>
public readonly struct Authored<T>
{
    private readonly T _value;

    public bool IsPresent { get; }

    public T Value => IsPresent ? _value : throw new InvalidOperationException("The value was not authored.");

    public ContentId? Origin { get; }

    private Authored(T value, ContentId? origin)
    {
        _value = value;
        IsPresent = true;
        Origin = origin;
    }

    public static Authored<T> Omitted => default;

    public static Authored<T> Present(T value) => new(value, null);

    public T GetValueOrDefault(T fallback) => IsPresent ? _value : fallback;

    internal Authored<T> WithOrigin(ContentId origin)
    {
        return IsPresent && Origin is null ? new Authored<T>(_value, origin) : this;
    }

    /// <summary>The child value wins when present; otherwise the parent's value and origin carry over.</summary>
    internal static Authored<T> Inherit(Authored<T> child, Authored<T> parent) => child.IsPresent ? child : parent;

    public override string ToString() => IsPresent ? $"{_value}" : "<omitted>";
}

public static class Authored
{
    public static Authored<T> Of<T>(T value) => Authored<T>.Present(value);
}
