using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Panopticon.Tests.Data.M3;

public sealed class M3TestDataBuilder
{
    public uint ModelVersion { get; set; } = 23;

    public uint RegionVersion { get; set; } = 3;

    public uint VertexFlags { get; set; } = 0x0186007D;

    public bool AddUnreferencedTags { get; set; }

    public byte[] Build()
    {
        var chunks = new List<Chunk>();
        var modelNameIndex = AddString(chunks, "SyntheticModel");
        var boneNameIndex = AddString(chunks, "Root");
        var boneIndex = AddChunk(chunks, "BONE", 1, WriteBone(boneNameIndex));
        var verticesIndex = AddChunk(chunks, "U8__", 0, new byte[36]);
        var facesIndex = AddChunk(chunks, "U16_", 0, WriteUInt16s([0, 1, 2]));
        var regionIndex = AddChunk(chunks, "REGN", RegionVersion, WriteRegion());
        var batchIndex = AddChunk(chunks, "BAT_", 1, WriteBatch());
        var divisionIndex = AddChunk(chunks, "DIV_", 2, WriteDivision(facesIndex, regionIndex, batchIndex));
        var boneLookupIndex = AddChunk(chunks, "U16_", 0, WriteUInt16s([0]));
        var materialNameIndex = AddString(chunks, "SyntheticMaterial");
        var diffusePathIndex = AddString(chunks, "Assets/Textures/Synthetic_Diffuse.dds");
        var layerIndex = AddChunk(chunks, "LAYR", 22, WriteLayer(diffusePathIndex));
        var materialReferenceIndex = AddChunk(chunks, "MATM", 0, WriteMaterialReference());
        var materialIndex = AddChunk(chunks, "MAT_", 15, WriteMaterial(materialNameIndex, layerIndex));

        if (AddUnreferencedTags)
        {
            AddChunk(chunks, "PAR_", 0, [0xAA]);
            AddChunk(chunks, "RIB_", 0, [0xBB]);
            AddChunk(chunks, "LITE", 0, [0xCC]);
        }

        var modelIndex = AddChunk(
            chunks,
            "MODL",
            ModelVersion,
            WriteModel(modelNameIndex, boneIndex, verticesIndex, divisionIndex, boneLookupIndex, materialReferenceIndex, materialIndex));

        return WriteFile(chunks, modelIndex);
    }

    private static int AddString(List<Chunk> chunks, string value)
    {
        return AddChunk(chunks, "CHAR", 0, [.. Encoding.Latin1.GetBytes(value), 0]);
    }

    private static int AddChunk(List<Chunk> chunks, string tag, uint version, byte[] bytes)
    {
        var index = chunks.Count;
        chunks.Add(new Chunk(tag, version, bytes));
        return index;
    }

    private static byte[] WriteBone(int nameIndex)
    {
        using var stream = new MemoryStream(new byte[160], writable: true);
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        WriteReference(writer, 4, 5, nameIndex);
        writer.BaseStream.Position = 16;
        writer.Write((uint)0x12345);
        writer.Write((short)-1);
        writer.BaseStream.Position = 24;
        WriteAnimationHeader(writer, interpolation: 1, flags: 2, animationId: 100);
        WriteVector3(writer, new Vector3(1, 2, 3));
        writer.BaseStream.Position = 60;
        WriteAnimationHeader(writer, interpolation: 1, flags: 4, animationId: 101);
        WriteQuaternion(writer, new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
        writer.BaseStream.Position = 104;
        WriteAnimationHeader(writer, interpolation: 1, flags: 8, animationId: 102);
        WriteVector3(writer, new Vector3(4, 5, 6));
        return stream.ToArray();
    }

    private static byte[] WriteRegion()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write((uint)7);
        writer.Write((uint)8);
        writer.Write((uint)0);
        writer.Write((uint)1);
        writer.Write((uint)0);
        writer.Write((uint)3);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)9);
        writer.Write((byte)4);
        writer.Write((byte)10);
        writer.Write((ushort)0);
        return stream.ToArray();
    }

    private static byte[] WriteBatch()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write((ushort)0x11);
        writer.Write((ushort)0x22);
        writer.Write((ushort)0);
        writer.Write((ushort)0x33);
        writer.Write((ushort)0x44);
        writer.Write((ushort)0);
        writer.Write((short)-1);
        return stream.ToArray();
    }

    private static byte[] WriteDivision(int facesIndex, int regionIndex, int batchIndex)
    {
        using var stream = new MemoryStream(new byte[52], writable: true);
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        WriteReference(writer, 0, 3, facesIndex);
        WriteReference(writer, 12, 1, regionIndex);
        WriteReference(writer, 24, 1, batchIndex);
        return stream.ToArray();
    }

    private static byte[] WriteMaterialReference()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write((uint)1);
        writer.Write((uint)0);
        return stream.ToArray();
    }

    private static byte[] WriteLayer(int pathIndex)
    {
        using var stream = new MemoryStream(new byte[356], writable: true);
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        WriteReference(writer, 4, 38, pathIndex);
        writer.BaseStream.Position = 36;
        writer.Write((uint)0x55);
        writer.Write((uint)1);
        writer.Write((uint)2);
        return stream.ToArray();
    }

    private static byte[] WriteMaterial(int nameIndex, int layerIndex)
    {
        using var stream = new MemoryStream(new byte[268], writable: true);
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        WriteReference(writer, 0, 18, nameIndex);
        writer.BaseStream.Position = 16;
        writer.Write((uint)0x66);
        writer.Write((uint)0);
        writer.BaseStream.Position = 32;
        writer.Write(80.0f);
        writer.BaseStream.Position = 40;
        writer.Write((uint)17);
        WriteReference(writer, 52, 1, layerIndex);
        return stream.ToArray();
    }

    private byte[] WriteModel(
        int nameIndex,
        int boneIndex,
        int verticesIndex,
        int divisionIndex,
        int boneLookupIndex,
        int materialReferenceIndex,
        int materialIndex)
    {
        using var stream = new MemoryStream(new byte[784], writable: true);
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        WriteReference(writer, 0, 15, nameIndex);
        WriteReference(writer, 80, 1, boneIndex);
        writer.BaseStream.Position = 92;
        writer.Write((uint)1);
        writer.Write(VertexFlags);
        WriteReference(writer, 100, 36, verticesIndex);
        WriteReference(writer, 112, 1, divisionIndex);
        WriteReference(writer, 124, 1, boneLookupIndex);
        WriteReference(writer, 300, 1, materialReferenceIndex);
        WriteReference(writer, 312, 1, materialIndex);
        return stream.ToArray();
    }

    private static byte[] WriteUInt16s(IReadOnlyList<ushort> values)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        foreach (var value in values)
        {
            writer.Write(value);
        }

        return stream.ToArray();
    }

    private static byte[] WriteFile(IReadOnlyList<Chunk> chunks, int modelIndex)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write(Encoding.Latin1.GetBytes("43DM"));
        writer.Write((uint)0);
        writer.Write((uint)chunks.Count);
        writer.Write((uint)1);
        writer.Write((uint)modelIndex);
        writer.Write((uint)0);

        var offsets = new uint[chunks.Count];
        for (var i = 0; i < chunks.Count; i++)
        {
            offsets[i] = (uint)writer.BaseStream.Position;
            writer.Write(chunks[i].Bytes);
        }

        var indexOffset = (uint)writer.BaseStream.Position;
        for (var i = 0; i < chunks.Count; i++)
        {
            writer.Write(Encoding.Latin1.GetBytes(ReverseTag(chunks[i].Tag)));
            writer.Write(offsets[i]);
            writer.Write((uint)Repetitions(chunks[i]));
            writer.Write(chunks[i].Version);
        }

        writer.BaseStream.Position = 4;
        writer.Write(indexOffset);
        return stream.ToArray();
    }

    private static int Repetitions(Chunk chunk)
    {
        return chunk.Tag switch
        {
            "CHAR" => chunk.Bytes.Length,
            "U8__" => chunk.Bytes.Length,
            "U16_" => chunk.Bytes.Length / 2,
            "BONE" => chunk.Bytes.Length / 160,
            "REGN" => chunk.Bytes.Length / 36,
            "BAT_" => chunk.Bytes.Length / 14,
            "DIV_" => chunk.Bytes.Length / 52,
            "MATM" => chunk.Bytes.Length / 8,
            "MAT_" => chunk.Bytes.Length / 268,
            "LAYR" => chunk.Bytes.Length / 356,
            _ => 1,
        };
    }

    private static string ReverseTag(string tag)
    {
        return new string([tag[3], tag[2], tag[1], tag[0]]);
    }

    private static void WriteReference(BinaryWriter writer, long offset, uint count, int index)
    {
        writer.BaseStream.Position = offset;
        writer.Write(count);
        writer.Write((uint)index);
        writer.Write((uint)0);
    }

    private static void WriteAnimationHeader(BinaryWriter writer, ushort interpolation, ushort flags, uint animationId)
    {
        writer.Write(interpolation);
        writer.Write(flags);
        writer.Write(animationId);
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }

    private sealed record Chunk(string Tag, uint Version, byte[] Bytes);
}
