using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// Little-endian reader for NIF 10.1.0.0 data with bounds validation.
/// Every count is checked against the remaining stream bytes before the
/// caller can allocate from it.
/// </summary>
public sealed class NifBinaryReader
{
    private const long MaxStringBytes = 1024 * 1024;

    private readonly BinaryReader _reader;

    public long RemainingBytes => _reader.BaseStream.Length - _reader.BaseStream.Position;

    public NifBinaryReader(Stream stream)
    {
        _reader = new BinaryReader(stream, System.Text.Encoding.Latin1, leaveOpen: true);
    }

    public byte ReadByte(string fieldName)
    {
        EnsureAvailable(1, fieldName);
        return _reader.ReadByte();
    }

    public short ReadInt16(string fieldName)
    {
        EnsureAvailable(2, fieldName);
        return _reader.ReadInt16();
    }

    public ushort ReadUInt16(string fieldName)
    {
        EnsureAvailable(2, fieldName);
        return _reader.ReadUInt16();
    }

    public int ReadInt32(string fieldName)
    {
        EnsureAvailable(4, fieldName);
        return _reader.ReadInt32();
    }

    public uint ReadUInt32(string fieldName)
    {
        EnsureAvailable(4, fieldName);
        return _reader.ReadUInt32();
    }

    public float ReadSingle(string fieldName)
    {
        EnsureAvailable(4, fieldName);
        return _reader.ReadSingle();
    }

    /// <summary>
    /// Reads a NIF block reference. <c>-1</c> means "no reference".
    /// Range validation against the block count is the caller's job because
    /// the header owns that count.
    /// </summary>
    public int ReadBlockReference(string fieldName)
    {
        return ReadInt32(fieldName);
    }

    /// <summary>
    /// Reads a u32 length followed by that many bytes. Rejects lengths that
    /// exceed the remaining stream data.
    /// </summary>
    public string ReadSizedString(string fieldName)
    {
        var length = ReadUInt32(fieldName);
        if (length > MaxStringBytes || length > RemainingBytes)
        {
            throw NewInvalidData(fieldName,
                $"string length {length} exceeds remaining {RemainingBytes} bytes");
        }

        var bytes = _reader.ReadBytes((int)length);
        return System.Text.Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Reads a line-style string terminated by (and not including) 0x0A,
    /// used by the NIF header string.
    /// </summary>
    public string ReadLineString(string fieldName)
    {
        var bytes = new List<byte>();
        while (true)
        {
            EnsureAvailable(1, fieldName);
            var b = _reader.ReadByte();
            if (b == 0x0A)
            {
                return System.Text.Encoding.Latin1.GetString(bytes.ToArray());
            }

            if (bytes.Count > MaxStringBytes)
            {
                throw NewInvalidData(fieldName, "line exceeds maximum length");
            }

            bytes.Add(b);
        }
    }

    /// <summary>
    /// Reads a u32 element count and validates that at least
    /// <paramref name="bytesPerElement"/> times the count bytes remain,
    /// so callers cannot allocate from a corrupt count.
    /// </summary>
    public int ReadElementCount(int bytesPerElement, string fieldName)
    {
        var count = ReadUInt32(fieldName);
        if (count > int.MaxValue)
        {
            throw NewInvalidData(fieldName, $"count {count} exceeds int.MaxValue");
        }

        var needed = (long)count * bytesPerElement;
        if (needed > RemainingBytes)
        {
            throw NewInvalidData(fieldName,
                $"count {count} of {bytesPerElement}-byte elements exceeds remaining {RemainingBytes} bytes");
        }

        return (int)count;
    }

    /// <summary>
    /// Reads a u32 reference count where each element is a 4-byte block
    /// reference and returns the references.
    /// </summary>
    public int[] ReadBlockReferenceList(string fieldName)
    {
        var count = ReadElementCount(4, fieldName);
        var references = new int[count];
        for (var i = 0; i < count; i++)
        {
            references[i] = ReadBlockReference(fieldName);
        }

        return references;
    }

    public Vector3 ReadVector3(string fieldName)
    {
        var x = ReadSingle(fieldName);
        var y = ReadSingle(fieldName);
        var z = ReadSingle(fieldName);
        return new Vector3(x, y, z);
    }

    public NifMatrix33 ReadMatrix33(string fieldName)
    {
        var m11 = ReadSingle(fieldName);
        var m12 = ReadSingle(fieldName);
        var m13 = ReadSingle(fieldName);
        var m21 = ReadSingle(fieldName);
        var m22 = ReadSingle(fieldName);
        var m23 = ReadSingle(fieldName);
        var m31 = ReadSingle(fieldName);
        var m32 = ReadSingle(fieldName);
        var m33 = ReadSingle(fieldName);
        return new NifMatrix33(m11, m12, m13, m21, m22, m23, m31, m32, m33);
    }

    public Vector3 ReadColor3(string fieldName)
    {
        return ReadVector3(fieldName);
    }

    private void EnsureAvailable(int byteCount, string fieldName)
    {
        if (byteCount > RemainingBytes)
        {
            throw NewInvalidData(fieldName,
                $"{byteCount} bytes required but only {RemainingBytes} remain");
        }
    }

    private static InvalidDataException NewInvalidData(string fieldName, string message)
    {
        return new InvalidDataException($"NIF field '{fieldName}': {message}");
    }
}
