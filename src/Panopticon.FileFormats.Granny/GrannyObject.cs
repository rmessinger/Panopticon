using System;
using System.Collections.Generic;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// A 68-byte Granny transform: flag bits (1 translation, 2 rotation, 4 scale-shear)
/// plus the three float arrays it gates. Rotation is xyzw, scale-shear row-major.
/// </summary>
public readonly record struct GrannyTransform(
    uint Flags,
    float[] Translation,
    float[] Rotation,
    float[] ScaleShear);

/// <summary>
/// A parsed Granny struct instance: field values keyed by member name.
/// Values are boxed scalars, string, <see cref="GrannyTransform"/>,
/// <see cref="GrannyObject"/>, typed arrays for inline arrays, and
/// <see cref="GrannyObject"/> arrays for the reference-array member types.
/// </summary>
public sealed class GrannyObject
{
    private readonly Dictionary<string, object?> _fields = new Dictionary<string, object?>();

    public IReadOnlyDictionary<string, object?> Fields => _fields;

    public bool HasField(string name)
    {
        return _fields.ContainsKey(name);
    }

    public object? Get(string name)
    {
        return _fields.GetValueOrDefault(name);
    }

    public string? GetString(string name)
    {
        return Get(name) as string;
    }

    public int? GetInt32(string name)
    {
        return Get(name) is int value ? value : null;
    }

    public uint? GetUInt32(string name)
    {
        return Get(name) is uint value ? value : null;
    }

    public float? GetSingle(string name)
    {
        return Get(name) is float value ? value : null;
    }

    public GrannyObject? GetObject(string name)
    {
        return Get(name) as GrannyObject;
    }

    public IReadOnlyList<GrannyObject> GetObjects(string name)
    {
        return Get(name) as GrannyObject[] ?? Array.Empty<GrannyObject>();
    }

    public GrannyTransform? GetTransform(string name)
    {
        return Get(name) is GrannyTransform transform ? transform : null;
    }

    public Array? GetArray(string name)
    {
        return Get(name) as Array;
    }

    internal void Set(string name, object? value)
    {
        _fields[name] = value;
    }
}
