using System;
using System.Collections.Generic;
using System.IO;

namespace OpenSage.Tests.Data.Nif;

/// <summary>
/// Builds synthetic NIF 10.1.0.0 byte streams in code. No game bytes.
/// </summary>
public sealed class NifTestDataBuilder
{
    private readonly List<string> _blockTypes = [];
    private readonly List<ushort> _blockTypeIndices = [];
    private readonly List<byte[]> _blockBodies = [];
    private readonly List<byte[]> _trailing = [];

    public string HeaderString { get; set; } = "Gamebryo File Format, Version 10.1.0.0";
    public uint Version { get; set; } = 0x0A010000;
    public uint UserVersion { get; set; } = 0;
    public uint NumGroups { get; set; } = 0;

    public ushort AddBlockType(string typeName)
    {
        _blockTypes.Add(typeName);
        return (ushort)(_blockTypes.Count - 1);
    }

    public void AddBlock(ushort typeIndex, byte[] body)
    {
        _blockTypeIndices.Add(typeIndex);
        _blockBodies.Add(body);
    }

    public IList<ushort> BlockTypeIndices => _blockTypeIndices;

    public void AddTrailing(byte[] bytes)
    {
        _trailing.Add(bytes);
    }

    public byte[] Build()
    {
        var stream = new MemoryStream();
        var writer = new NifBodyWriter(stream);

        writer.WriteString(HeaderString + "\n");
        writer.WriteUInt32(Version);
        writer.WriteUInt32(UserVersion);
        writer.WriteUInt32((uint)_blockBodies.Count);
        writer.WriteUInt16((ushort)_blockTypes.Count);
        foreach (var typeName in _blockTypes)
        {
            writer.WriteSizedString(typeName);
        }
        foreach (var index in _blockTypeIndices)
        {
            writer.WriteUInt16(index);
        }
        writer.WriteUInt32(NumGroups);
        foreach (var body in _blockBodies)
        {
            writer.WriteBytes(body);
        }
        foreach (var bytes in _trailing)
        {
            writer.WriteBytes(bytes);
        }
        return stream.ToArray();
    }
}

public sealed class NifBodyWriter(Stream stream)
{
    public void WriteString(string value)
    {
        WriteBytes(System.Text.Encoding.Latin1.GetBytes(value));
    }

    public void WriteBytes(byte[] bytes)
    {
        stream.Write(bytes, 0, bytes.Length);
    }

    public void WriteByte(byte value)
    {
        stream.WriteByte(value);
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> span = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span, value);
        stream.Write(span);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> span = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        stream.Write(span);
    }

    public void WriteInt32(int value)
    {
        WriteUInt32(unchecked((uint)value));
    }

    public void WriteSingle(float value)
    {
        WriteUInt32(unchecked((uint)BitConverter.SingleToUInt32Bits(value)));
    }

    public void WriteSizedString(string value)
    {
        WriteUInt32((uint)value.Length);
        WriteString(value);
    }
}

public static class NifTestBlocks
{
    public static byte[] Node(
        string name = "",
        int[]? extraData = null,
        int controller = -1,
        ushort flags = 0,
        (float X, float Y, float Z)? translation = null,
        float scale = 1.0f,
        int[]? properties = null,
        int collision = -1,
        int[]? children = null,
        int[]? effects = null)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        WriteRefList(body, extraData ?? []);
        body.WriteInt32(controller);
        body.WriteUInt16(flags);
        WriteVector3(body, translation ?? (0, 0, 0));
        WriteMatrix33Identity(body);
        body.WriteSingle(scale);
        WriteRefList(body, properties ?? []);
        body.WriteInt32(collision);
        WriteRefList(body, children ?? []);
        WriteRefList(body, effects ?? []);
        return stream.ToArray();
    }

    public static byte[] StringExtraData(string name, string data)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        body.WriteSizedString(data);
        return stream.ToArray();
    }

    public static void WriteRefList(NifBodyWriter body, int[] references)
    {
        body.WriteUInt32((uint)references.Length);
        foreach (var reference in references)
        {
            body.WriteInt32(reference);
        }
    }

    public static void WriteVector3(NifBodyWriter body, (float X, float Y, float Z) value)
    {
        body.WriteSingle(value.X);
        body.WriteSingle(value.Y);
        body.WriteSingle(value.Z);
    }

    public static void WriteMatrix33Identity(NifBodyWriter body)
    {
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                body.WriteSingle(row == column ? 1.0f : 0.0f);
            }
        }
    }

    public static byte[] Footer()
    {
        var stream = new MemoryStream();
        var writer = new NifBodyWriter(stream);
        writer.WriteUInt32(1);
        writer.WriteUInt32(0);
        return stream.ToArray();
    }
}
