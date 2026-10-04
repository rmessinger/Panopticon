#nullable enable

using System;
using System.Numerics;
using Panopticon;

namespace Panopticon.Content.Importing;

public enum ImportedMaterialPropertyKind
{
    Bool,
    Float,
    Vector2,
    Vector3,
    Vector4,
    Int32,
    TextureName,
}

/// <summary>
/// One named, type-tagged material property. The kind determines which value accessor is valid;
/// reading the wrong kind throws. Texture names are plain strings; resolution stays in the
/// materializer.
/// </summary>
public sealed class ImportedMaterialProperty
{
    public string Name { get; }

    public ImportedMaterialPropertyKind Kind { get; }

    private readonly object _value;

    public bool BoolValue => ValueAs<bool>(ImportedMaterialPropertyKind.Bool);

    public float FloatValue => ValueAs<float>(ImportedMaterialPropertyKind.Float);

    public Vector2 Vector2Value => ValueAs<Vector2>(ImportedMaterialPropertyKind.Vector2);

    public Vector3 Vector3Value => ValueAs<Vector3>(ImportedMaterialPropertyKind.Vector3);

    public Vector4 Vector4Value => ValueAs<Vector4>(ImportedMaterialPropertyKind.Vector4);

    public int Int32Value => ValueAs<int>(ImportedMaterialPropertyKind.Int32);

    public string TextureName => ValueAs<string>(ImportedMaterialPropertyKind.TextureName);

    private ImportedMaterialProperty(string name, ImportedMaterialPropertyKind kind, object value)
    {
        Ensure.NotNullOrEmpty(name, "Property name must not be null or empty.");

        Name = name;
        Kind = kind;
        _value = value;
    }

    public static ImportedMaterialProperty FromBool(string name, bool value)
    {
        return new ImportedMaterialProperty(name, ImportedMaterialPropertyKind.Bool, value);
    }

    public static ImportedMaterialProperty FromFloat(string name, float value)
    {
        return new ImportedMaterialProperty(name, ImportedMaterialPropertyKind.Float, value);
    }

    public static ImportedMaterialProperty FromVector2(string name, in Vector2 value)
    {
        return new ImportedMaterialProperty(name, ImportedMaterialPropertyKind.Vector2, value);
    }

    public static ImportedMaterialProperty FromVector3(string name, in Vector3 value)
    {
        return new ImportedMaterialProperty(name, ImportedMaterialPropertyKind.Vector3, value);
    }

    public static ImportedMaterialProperty FromVector4(string name, in Vector4 value)
    {
        return new ImportedMaterialProperty(name, ImportedMaterialPropertyKind.Vector4, value);
    }

    public static ImportedMaterialProperty FromInt32(string name, int value)
    {
        return new ImportedMaterialProperty(name, ImportedMaterialPropertyKind.Int32, value);
    }

    public static ImportedMaterialProperty FromTextureName(string name, string textureName)
    {
        Ensure.NotNullOrEmpty(textureName, "Texture name must not be null or empty.");

        return new ImportedMaterialProperty(name, ImportedMaterialPropertyKind.TextureName, textureName);
    }

    private T ValueAs<T>(ImportedMaterialPropertyKind expectedKind)
    {
        if (Kind != expectedKind)
        {
            throw new InvalidOperationException(
                $"Property '{Name}' holds a {Kind} value, not {expectedKind}.");
        }

        return (T)_value;
    }
}
