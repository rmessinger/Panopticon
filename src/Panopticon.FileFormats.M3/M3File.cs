using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Panopticon.FileFormats.M3;

public sealed class M3File
{
    private const uint HeaderTag = 0x4D443334;
    private const int HeaderSize = 24;

    private readonly byte[] _bytes;
    private readonly string _streamName;

    public M3Reference ModelReference { get; }

    public IReadOnlyList<M3IndexEntry> Index { get; }

    public M3Model Model { get; }

    private M3File(byte[] bytes, string streamName, M3Reference modelReference, IReadOnlyList<M3IndexEntry> index)
    {
        _bytes = bytes;
        _streamName = streamName;
        ModelReference = modelReference;
        Index = index;
        Model = ReadModel(modelReference);
    }

    public static M3File Read(Stream stream)
    {
        return Read(stream, null);
    }

    public static M3File Read(Stream stream, string? streamName)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var name = streamName ?? (stream is FileStream fileStream ? fileStream.Name : "<stream>");

        if (bytes.Length < HeaderSize)
        {
            throw new InvalidDataException($"M3 stream '{name}' is too short for an MD34 header.");
        }

        var span = bytes.AsSpan();
        var tag = BinaryPrimitives.ReadUInt32LittleEndian(span);
        if (tag != HeaderTag)
        {
            throw new InvalidDataException($"M3 stream '{name}' starts with 0x{tag:X8}; expected 43DM.");
        }

        var indexOffset = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4));
        var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8));
        var indexBytes = checked((long)indexCount * 16);
        if (indexOffset > bytes.Length || indexBytes > bytes.Length - indexOffset)
        {
            throw new InvalidDataException(
                $"M3 stream '{name}' has truncated index at offset {indexOffset} with {indexCount} entries.");
        }

        var modelReference = ReadReference(span, 12, name);
        var index = new M3IndexEntry[indexCount];
        for (var i = 0; i < index.Length; i++)
        {
            var offset = (int)indexOffset + i * 16;
            index[i] = new M3IndexEntry(
                ReadTag(span.Slice(offset, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 8)),
                BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 12)));
        }

        return new M3File(bytes, name, modelReference, index);
    }

    public M3ResolvedReference ResolveReference(M3Reference reference, string expectedTag)
    {
        var entry = GetEntry(reference);
        if (entry.Tag != expectedTag)
        {
            throw new InvalidDataException(
                $"M3 reference in '{_streamName}' expected tag {expectedTag}, found {entry.Tag}.");
        }

        var byteCount = checked((int)reference.Count);
        ValidateRange(entry.Offset, byteCount, expectedTag);
        return new M3ResolvedReference(entry, _bytes.AsMemory((int)entry.Offset, byteCount));
    }

    private M3Model ReadModel(M3Reference reference)
    {
        if (reference.Count != 1)
        {
            throw new InvalidDataException($"M3 stream '{_streamName}' model reference count is {reference.Count}; expected 1.");
        }

        var resolved = ResolveReference(reference, "MODL", 23, 784);
        var data = resolved.Data.Span;
        var name = ReadString(ReadReference(data, 0, _streamName));
        var bones = ReadBones(ReadReference(data, 80, _streamName));
        var skinBoneCount = ReadUInt32(data, 92);
        var vertexFlags = ReadUInt32(data, 96);
        var vertexFormat = M3VertexFormat.FromFlags(vertexFlags);
        var vertexBytes = ReadBytes(ReadReference(data, 100, _streamName), "U8__");
        if (vertexBytes.Count % vertexFormat.Stride != 0)
        {
            throw new InvalidDataException(
                $"M3 stream '{_streamName}' vertex byte count {vertexBytes.Count} is not a multiple of stride {vertexFormat.Stride}.");
        }

        return new M3Model(
            name,
            bones,
            skinBoneCount,
            vertexFlags,
            vertexFormat,
            vertexBytes,
            ReadDivisions(ReadReference(data, 112, _streamName)),
            ReadUInt16s(ReadReference(data, 124, _streamName), "U16_"),
            ReadMaterialReferences(ReadReference(data, 300, _streamName)),
            ReadMaterials(ReadReference(data, 312, _streamName)));
    }

    private IReadOnlyList<M3Bone> ReadBones(M3Reference reference)
    {
        var items = ResolveArray(reference, "BONE", 1, 160);
        var bones = new M3Bone[items.Count];
        for (var i = 0; i < bones.Length; i++)
        {
            var data = items.Item(i);
            bones[i] = new M3Bone(
                ReadString(ReadReference(data, 4, _streamName)),
                ReadUInt32(data, 16),
                ReadInt16(data, 20),
                new M3AnimatedVector3(ReadAnimationHeader(data, 24), ReadVector3(data, 32)),
                new M3AnimatedQuaternion(ReadAnimationHeader(data, 60), ReadQuaternion(data, 68)),
                new M3AnimatedVector3(ReadAnimationHeader(data, 104), ReadVector3(data, 112)));
        }

        return bones;
    }

    private IReadOnlyList<M3Division> ReadDivisions(M3Reference reference)
    {
        var items = ResolveArray(reference, "DIV_", 2, 52);
        var divisions = new M3Division[items.Count];
        for (var i = 0; i < divisions.Length; i++)
        {
            var data = items.Item(i);
            divisions[i] = new M3Division(
                ReadUInt16s(ReadReference(data, 0, _streamName), "U16_"),
                ReadRegions(ReadReference(data, 12, _streamName)),
                ReadBatches(ReadReference(data, 24, _streamName)));
        }

        return divisions;
    }

    private IReadOnlyList<M3Region> ReadRegions(M3Reference reference)
    {
        var items = ResolveArray(reference, "REGN", 3, 36);
        var regions = new M3Region[items.Count];
        for (var i = 0; i < regions.Length; i++)
        {
            var data = items.Item(i);
            regions[i] = new M3Region(
                ReadUInt32(data, 0),
                ReadUInt32(data, 4),
                ReadUInt32(data, 8),
                ReadUInt32(data, 12),
                ReadUInt32(data, 16),
                ReadUInt32(data, 20),
                ReadUInt16(data, 24),
                ReadUInt16(data, 26),
                ReadUInt16(data, 28),
                ReadUInt16(data, 30),
                data[32],
                data[33],
                ReadUInt16(data, 34));
        }

        return regions;
    }

    private IReadOnlyList<M3Batch> ReadBatches(M3Reference reference)
    {
        var items = ResolveArray(reference, "BAT_", 1, 14);
        var batches = new M3Batch[items.Count];
        for (var i = 0; i < batches.Length; i++)
        {
            var data = items.Item(i);
            batches[i] = new M3Batch(
                ReadUInt16(data, 0),
                ReadUInt16(data, 2),
                ReadUInt16(data, 4),
                ReadUInt16(data, 6),
                ReadUInt16(data, 8),
                ReadUInt16(data, 10),
                ReadInt16(data, 12));
        }

        return batches;
    }

    private IReadOnlyList<M3MaterialReference> ReadMaterialReferences(M3Reference reference)
    {
        var items = ResolveArray(reference, "MATM", 0, 8);
        var references = new M3MaterialReference[items.Count];
        for (var i = 0; i < references.Length; i++)
        {
            var data = items.Item(i);
            references[i] = new M3MaterialReference(ReadUInt32(data, 0), ReadUInt32(data, 4));
        }

        return references;
    }

    private IReadOnlyList<M3Material> ReadMaterials(M3Reference reference)
    {
        var items = ResolveArray(reference, "MAT_", 15, 268);
        var materials = new M3Material[items.Count];
        for (var i = 0; i < materials.Length; i++)
        {
            var data = items.Item(i);
            var layers = new M3Layer?[13];
            for (var layer = 0; layer < layers.Length; layer++)
            {
                layers[layer] = ReadLayer(ReadReference(data, 52 + layer * 12, _streamName));
            }

            materials[i] = new M3Material(
                ReadString(ReadReference(data, 0, _streamName)),
                ReadUInt32(data, 16),
                ReadUInt32(data, 20),
                ReadSingle(data, 32),
                ReadUInt32(data, 40),
                layers);
        }

        return materials;
    }

    private M3Layer? ReadLayer(M3Reference reference)
    {
        if (reference.Count == 0)
        {
            return null;
        }

        var items = ResolveArray(reference, "LAYR", 22, 356);
        if (items.Count != 1)
        {
            throw new InvalidDataException(
                $"M3 stream '{_streamName}' layer reference count is {items.Count}; expected 1.");
        }

        var data = items.Item(0);
        return new M3Layer(
            ReadString(ReadReference(data, 4, _streamName)),
            ReadUInt32(data, 36),
            ReadUInt32(data, 40),
            ReadUInt32(data, 44));
    }

    private IReadOnlyList<byte> ReadBytes(M3Reference reference, string expectedTag)
    {
        if (reference.Count == 0)
        {
            return Array.Empty<byte>();
        }

        var entry = GetEntry(reference);
        if (entry.Tag != expectedTag)
        {
            throw new InvalidDataException(
                $"M3 reference in '{_streamName}' expected tag {expectedTag}, found {entry.Tag}.");
        }

        if (entry.Repetitions != reference.Count)
        {
            throw new InvalidDataException(
                $"M3 {expectedTag} reference in '{_streamName}' has count {reference.Count} but index repetitions {entry.Repetitions}.");
        }

        ValidateRange(entry.Offset, reference.Count, expectedTag);
        var bytes = new byte[reference.Count];
        Array.Copy(_bytes, entry.Offset, bytes, 0, bytes.Length);
        return bytes;
    }

    private IReadOnlyList<ushort> ReadUInt16s(M3Reference reference, string expectedTag)
    {
        var items = ResolveArray(reference, expectedTag, null, 2);
        var values = new ushort[items.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ReadUInt16(items.Item(i), 0);
        }

        return values;
    }

    private string ReadString(M3Reference reference)
    {
        if (reference.Count == 0)
        {
            return string.Empty;
        }

        var resolved = ResolveReference(reference, "CHAR");
        var data = resolved.Data.Span;
        if (data.Length == 0 || data[^1] != 0)
        {
            throw new InvalidDataException($"M3 CHAR reference in '{_streamName}' is not NUL-terminated.");
        }

        return Encoding.Latin1.GetString(data[..^1]);
    }

    private ResolvedArray ResolveArray(M3Reference reference, string expectedTag, uint? expectedVersion, int itemSize)
    {
        if (reference.Count == 0)
        {
            return new ResolvedArray(ReadOnlyMemory<byte>.Empty, 0, itemSize);
        }

        var entry = GetEntry(reference);
        if (entry.Tag != expectedTag)
        {
            throw new InvalidDataException(
                $"M3 reference in '{_streamName}' expected tag {expectedTag}, found {entry.Tag}.");
        }

        if (expectedVersion is { } version && entry.Version != version)
        {
            ThrowUnsupportedVersion(entry);
        }

        if (entry.Repetitions != reference.Count)
        {
            throw new InvalidDataException(
                $"M3 {expectedTag} reference in '{_streamName}' has count {reference.Count} but index repetitions {entry.Repetitions}.");
        }

        var byteCount = checked((long)reference.Count * itemSize);
        ValidateRange(entry.Offset, byteCount, expectedTag);
        return new ResolvedArray(_bytes.AsMemory((int)entry.Offset, (int)byteCount), checked((int)reference.Count), itemSize);
    }

    private M3ResolvedReference ResolveReference(M3Reference reference, string expectedTag, uint expectedVersion, int itemSize)
    {
        var entry = GetEntry(reference);
        if (entry.Tag != expectedTag)
        {
            throw new InvalidDataException(
                $"M3 reference in '{_streamName}' expected tag {expectedTag}, found {entry.Tag}.");
        }

        if (entry.Version != expectedVersion)
        {
            ThrowUnsupportedVersion(entry);
        }

        if (entry.Repetitions != reference.Count)
        {
            throw new InvalidDataException(
                $"M3 {expectedTag} reference in '{_streamName}' has count {reference.Count} but index repetitions {entry.Repetitions}.");
        }

        var byteCount = checked((long)reference.Count * itemSize);
        ValidateRange(entry.Offset, byteCount, expectedTag);
        return new M3ResolvedReference(entry, _bytes.AsMemory((int)entry.Offset, (int)byteCount));
    }

    private M3IndexEntry GetEntry(M3Reference reference)
    {
        if (reference.IndexEntry >= Index.Count)
        {
            throw new InvalidDataException(
                $"M3 reference in '{_streamName}' points to index entry {reference.IndexEntry}, but the index has {Index.Count} entries.");
        }

        return Index[(int)reference.IndexEntry];
    }

    private void ValidateRange(uint offset, long byteCount, string tag)
    {
        if (offset > _bytes.Length || byteCount > _bytes.Length - offset)
        {
            throw new InvalidDataException(
                $"M3 tag {tag} in '{_streamName}' at offset {offset} with {byteCount} bytes exceeds stream length {_bytes.Length}.");
        }
    }

    private void ThrowUnsupportedVersion(M3IndexEntry entry)
    {
        throw new NotSupportedException(
            $"M3 tag {entry.Tag} version {entry.Version} is not supported in '{_streamName}'.");
    }

    private static M3Reference ReadReference(ReadOnlySpan<byte> data, int offset, string streamName)
    {
        EnsureAvailable(data, offset, 12, "reference", streamName);
        return new M3Reference(
            ReadUInt32(data, offset),
            ReadUInt32(data, offset + 4),
            ReadUInt32(data, offset + 8));
    }

    private static M3AnimationHeader ReadAnimationHeader(ReadOnlySpan<byte> data, int offset)
    {
        return new M3AnimationHeader(
            ReadUInt16(data, offset),
            ReadUInt16(data, offset + 2),
            ReadUInt32(data, offset + 4));
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
    {
        return new Vector3(
            ReadSingle(data, offset),
            ReadSingle(data, offset + 4),
            ReadSingle(data, offset + 8));
    }

    private static Quaternion ReadQuaternion(ReadOnlySpan<byte> data, int offset)
    {
        return new Quaternion(
            ReadSingle(data, offset),
            ReadSingle(data, offset + 4),
            ReadSingle(data, offset + 8),
            ReadSingle(data, offset + 12));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset, 4));
    }

    private static string ReadTag(ReadOnlySpan<byte> bytes)
    {
        Span<byte> reversed = stackalloc byte[4];
        reversed[0] = bytes[3];
        reversed[1] = bytes[2];
        reversed[2] = bytes[1];
        reversed[3] = bytes[0];
        return Encoding.Latin1.GetString(reversed);
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> data, int offset, int length, string field, string streamName)
    {
        if (offset < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException(
                $"M3 field {field} in '{streamName}' at offset {offset} needs {length} bytes, but only {Math.Max(0, data.Length - offset)} remain.");
        }
    }

    private readonly struct ResolvedArray
    {
        private readonly ReadOnlyMemory<byte> _data;
        private readonly int _itemSize;

        public int Count { get; }

        public ResolvedArray(ReadOnlyMemory<byte> data, int count, int itemSize)
        {
            _data = data;
            Count = count;
            _itemSize = itemSize;
        }

        public ReadOnlySpan<byte> Item(int index)
        {
            return _data.Span.Slice(index * _itemSize, _itemSize);
        }
    }
}
