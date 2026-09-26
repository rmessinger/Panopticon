using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NIF 10.1 key interpolation types. Values are the wire encodings.
/// </summary>
public enum NifKeyType : uint
{
    Linear = 1,
    Quadratic = 2,
    Tbc = 3,
    XyzRotation = 4,
    Constant = 5,
}

/// <summary>
/// One keyed sample. Forward and backward tangents are present only on
/// quadratic keys; interpretation belongs to the curve evaluator, not the
/// parser.
/// </summary>
public sealed record NifKey<T>(float Time, T Value, T? Forward, T? Backward)
    where T : struct;

/// <summary>
/// One linear rotation key: a time plus a stored (w, x, y, z) quaternion.
/// </summary>
public sealed record NifQuaternionKey(float Time, Quaternion Value);

/// <summary>
/// A decoded key group. Interpolation is null for an absent group, which
/// carries only its zero count on the wire.
/// </summary>
public sealed record NifKeyGroup<T>(NifKeyType? Interpolation, IReadOnlyList<T> Keys);

/// <summary>
/// Reads NIF 10.1 key groups for the value types C4c supports. Group
/// layout: count (u32); when nonzero, key type (u32) and that many keys.
/// </summary>
internal static class NifKeyGroupReader
{
    public static NifKeyType ReadKeyType(NifBinaryReader reader, string fieldName)
    {
        var raw = reader.ReadUInt32($"{fieldName}.KeyType");
        var type = (NifKeyType)raw;
        if (!Enum.IsDefined(typeof(NifKeyType), type))
        {
            throw new NotSupportedException(
                $"NIF key group '{fieldName}' has unknown key type {raw}.");
        }

        return type;
    }

    /// <summary>
    /// Reads a float key group. Returns null when the group is empty.
    /// </summary>
    public static NifKeyGroup<NifKey<float>>? ReadFloatGroup(
        NifBinaryReader reader,
        string blockName,
        string fieldName)
    {
        var count = reader.ReadElementCount(8, $"{fieldName}.Count");
        if (count == 0)
        {
            return null;
        }

        var type = ReadKeyType(reader, fieldName);
        if (type != NifKeyType.Linear && type != NifKeyType.Quadratic)
        {
            throw NewUnsupported(
                blockName,
                fieldName,
                type,
                "C4c supports Linear (1) and Quadratic (2) keys");
        }

        var keys = new NifKey<float>[count];
        for (var i = 0; i < count; i++)
        {
            var time = reader.ReadSingle($"{fieldName}[{i}].Time");
            var value = reader.ReadSingle($"{fieldName}[{i}].Value");
            if (type == NifKeyType.Quadratic)
            {
                var forward = reader.ReadSingle($"{fieldName}[{i}].Forward");
                var backward = reader.ReadSingle($"{fieldName}[{i}].Backward");
                keys[i] = new NifKey<float>(time, value, forward, backward);
            }

            else
            {
                keys[i] = new NifKey<float>(time, value, null, null);
            }
        }

        return new NifKeyGroup<NifKey<float>>(type, keys);
    }

    /// <summary>
    /// Reads a Vector3 key group. Returns null when the group is empty.
    /// </summary>
    public static NifKeyGroup<NifKey<Vector3>>? ReadVector3Group(
        NifBinaryReader reader,
        string blockName,
        string fieldName)
    {
        var count = reader.ReadElementCount(16, $"{fieldName}.Count");
        if (count == 0)
        {
            return null;
        }

        var type = ReadKeyType(reader, fieldName);
        if (type != NifKeyType.Linear && type != NifKeyType.Quadratic)
        {
            throw NewUnsupported(
                blockName,
                fieldName,
                type,
                "C4c supports Linear (1) and Quadratic (2) keys");
        }

        var keys = new NifKey<Vector3>[count];
        for (var i = 0; i < count; i++)
        {
            var time = reader.ReadSingle($"{fieldName}[{i}].Time");
            var value = reader.ReadVector3($"{fieldName}[{i}].Value");
            if (type == NifKeyType.Quadratic)
            {
                var forward = reader.ReadVector3($"{fieldName}[{i}].Forward");
                var backward = reader.ReadVector3($"{fieldName}[{i}].Backward");
                keys[i] = new NifKey<Vector3>(time, value, forward, backward);
            }

            else
            {
                keys[i] = new NifKey<Vector3>(time, value, null, null);
            }
        }

        return new NifKeyGroup<NifKey<Vector3>>(type, keys);
    }

    /// <summary>
    /// Reads a byte key group (NiBoolData). Linear and constant keys share
    /// the same wire shape; both are accepted. Returns null when the group
    /// is empty.
    /// </summary>
    public static NifKeyGroup<NifKey<byte>>? ReadByteGroup(
        NifBinaryReader reader,
        string blockName,
        string fieldName)
    {
        var count = reader.ReadElementCount(5, $"{fieldName}.Count");
        if (count == 0)
        {
            return null;
        }

        var type = ReadKeyType(reader, fieldName);
        if (type != NifKeyType.Linear && type != NifKeyType.Constant)
        {
            throw NewUnsupported(
                blockName,
                fieldName,
                type,
                "C4e bool keys support Linear (1) and Constant (5) keys");
        }

        var keys = new NifKey<byte>[count];
        for (var i = 0; i < count; i++)
        {
            var time = reader.ReadSingle($"{fieldName}[{i}].Time");
            var value = reader.ReadByte($"{fieldName}[{i}].Value");
            keys[i] = new NifKey<byte>(time, value, null, null);
        }

        return new NifKeyGroup<NifKey<byte>>(type, keys);
    }

    /// <summary>
    /// Reads count linear quaternion keys for the rotation section of
    /// NiKeyframeData, whose count and type live at the data level rather
    /// than inside a nested group.
    /// </summary>
    public static NifKeyGroup<NifQuaternionKey> ReadLinearQuaternionKeys(
        NifBinaryReader reader,
        int count,
        string blockName,
        string fieldName)
    {
        var keys = new NifQuaternionKey[count];
        for (var i = 0; i < count; i++)
        {
            var time = reader.ReadSingle($"{fieldName}[{i}].Time");
            var w = reader.ReadSingle($"{fieldName}[{i}].W");
            var x = reader.ReadSingle($"{fieldName}[{i}].X");
            var y = reader.ReadSingle($"{fieldName}[{i}].Y");
            var z = reader.ReadSingle($"{fieldName}[{i}].Z");
            keys[i] = new NifQuaternionKey(time, new Quaternion(x, y, z, w));
        }

        return new NifKeyGroup<NifQuaternionKey>(NifKeyType.Linear, keys);
    }

    private static NotSupportedException NewUnsupported(
        string blockName,
        string fieldName,
        NifKeyType type,
        string supported)
    {
        return new NotSupportedException(
            $"NIF {blockName} key group '{fieldName}' uses unsupported key type " +
            $"{type} ({(uint)type}); {supported}.");
    }
}
