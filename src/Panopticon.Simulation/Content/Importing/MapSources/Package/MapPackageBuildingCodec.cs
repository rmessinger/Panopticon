#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Strict-JSON building metadata codec and PBM1 binary mesh codec for the buildings package layer.</summary>
public static class MapPackageBuildingCodec
{
    public const int DocumentVersion = 1;

    public const int MeshFormatVersion = 1;

    public const int MaxSegmentsPerChunk = 4096;

    public const int MaxVerticesPerSegment = 65535;

    public const int MaxVerticesPerChunk = 4_194_304;

    public const int MaxIndicesPerChunk = 12_582_912;

    private const int MeshHeaderBytes = 12;

    private const int SegmentHeaderBytes = 36;

    private const int VertexBytes = 24;

    private const int IndexBytes = 4;

    private static readonly byte[] MeshMagic = [(byte)'P', (byte)'B', (byte)'M', (byte)'1'];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] SerializeMetadata(IReadOnlyList<GeneratedBuilding> buildings)
    {
        ArgumentNullException.ThrowIfNull(buildings);
        var document = new MapPackageBuildingDocument(
            DocumentVersion,
            buildings.Select(MapPackageGeneratedBuilding.FromModel).ToArray());
        var json = JsonSerializer.Serialize(document, SerializerOptions).Replace("\r\n", "\n") + "\n";
        return Encoding.UTF8.GetBytes(json);
    }

    public static IReadOnlyList<GeneratedBuilding> DeserializeMetadata(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        MapPackageBuildingDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<MapPackageBuildingDocument>(bytes, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Building metadata JSON is malformed: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new FormatException("Building metadata JSON is empty.");
        }

        if (document.Version != DocumentVersion)
        {
            throw new FormatException($"Building metadata document version {document.Version} is unsupported; expected {DocumentVersion}.");
        }

        if (document.Buildings is null)
        {
            throw new FormatException("Building metadata JSON has no buildings list.");
        }

        try
        {
            return document.Buildings.Select(building => building.ToModel()).ToArray();
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Building metadata JSON holds an invalid building: {ex.Message}", ex);
        }
    }

    public static byte[] EncodeMesh(BuildingMeshChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var segments = chunk.Segments;
        if (segments.Count is < 1 or > MaxSegmentsPerChunk)
        {
            throw new FormatException($"Mesh chunk ({chunk.ChunkX}, {chunk.ChunkY}) has {segments.Count} segments; expected 1 to {MaxSegmentsPerChunk}.");
        }

        long vertexTotal = 0;
        long indexTotal = 0;
        foreach (var segment in segments)
        {
            ValidateSegmentCounts(segment.Vertices.Count, segment.Indices.Count, segment.Indices);
            vertexTotal += segment.Vertices.Count;
            indexTotal += segment.Indices.Count;
        }

        ValidateChunkTotals(vertexTotal, indexTotal);
        var total = ExpectedLength(segments.Count, vertexTotal, indexTotal);
        var buffer = new byte[total];
        var span = buffer.AsSpan();
        MeshMagic.CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], MeshFormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)segments.Count);
        var offset = MeshHeaderBytes;
        foreach (var segment in segments)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], segment.MaterialVariant);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 4)..], (uint)segment.Vertices.Count);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 8)..], (uint)segment.Indices.Count);
            offset += 12;
            WriteFloat(span, ref offset, segment.MinX);
            WriteFloat(span, ref offset, segment.MinY);
            WriteFloat(span, ref offset, segment.MinZ);
            WriteFloat(span, ref offset, segment.MaxX);
            WriteFloat(span, ref offset, segment.MaxY);
            WriteFloat(span, ref offset, segment.MaxZ);
            foreach (var vertex in segment.Vertices)
            {
                WriteFloat(span, ref offset, vertex.X);
                WriteFloat(span, ref offset, vertex.Y);
                WriteFloat(span, ref offset, vertex.Z);
                WriteFloat(span, ref offset, vertex.NormalX);
                WriteFloat(span, ref offset, vertex.NormalY);
                WriteFloat(span, ref offset, vertex.NormalZ);
            }

            foreach (var index in segment.Indices)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], index);
                offset += IndexBytes;
            }
        }

        return buffer;
    }

    public static BuildingMeshChunk DecodeMesh(byte[] bytes, int chunkX, int chunkY)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < MeshHeaderBytes || !bytes.AsSpan(0, 4).SequenceEqual(MeshMagic))
        {
            throw new FormatException("Mesh file is missing the PBM1 magic header.");
        }

        var formatVersion = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        if (formatVersion != MeshFormatVersion)
        {
            throw new FormatException($"Mesh format version {formatVersion} is unsupported; expected {MeshFormatVersion}.");
        }

        var segmentCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        if (segmentCount is < 1 or > MaxSegmentsPerChunk)
        {
            throw new FormatException($"Mesh segment count {segmentCount} is outside 1 to {MaxSegmentsPerChunk}.");
        }

        long vertexTotal = 0;
        long indexTotal = 0;
        long walk = MeshHeaderBytes;
        for (var s = 0; s < segmentCount; s++)
        {
            if (walk + SegmentHeaderBytes > bytes.Length)
            {
                throw new FormatException("Mesh file is truncated inside a segment header.");
            }

            var vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)walk + 4));
            var indexCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)walk + 8));
            if (vertexCount is < 1 or > MaxVerticesPerSegment)
            {
                throw new FormatException($"Mesh segment {s} vertex count {vertexCount} is outside 1 to {MaxVerticesPerSegment}.");
            }

            if (indexCount == 0 || indexCount % 3 != 0)
            {
                throw new FormatException($"Mesh segment {s} index count {indexCount} must be a positive multiple of 3.");
            }

            vertexTotal += vertexCount;
            indexTotal += indexCount;
            ValidateChunkTotals(vertexTotal, indexTotal);
            walk += SegmentHeaderBytes + (long)vertexCount * VertexBytes + (long)indexCount * IndexBytes;
        }

        var expected = ExpectedLength((int)segmentCount, vertexTotal, indexTotal);
        if (bytes.Length != expected || walk != expected)
        {
            throw new FormatException($"Mesh file length {bytes.Length} does not match the declared layout length {expected}.");
        }

        var segments = new List<BuildingMeshSegment>((int)segmentCount);
        var position = MeshHeaderBytes;
        for (var s = 0; s < segmentCount; s++)
        {
            var span = bytes.AsSpan(position);
            var materialVariant = BinaryPrimitives.ReadInt32LittleEndian(span);
            var vertexCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
            var indexCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
            var headerMin = ReadBounds(span[12..]);
            var headerMax = ReadBounds(span[24..]);
            var cursor = position + SegmentHeaderBytes;

            var vertices = new BuildingVertex[vertexCount];
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var minZ = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;
            var maxZ = double.NegativeInfinity;
            for (var v = 0; v < vertexCount; v++)
            {
                var vertexSpan = bytes.AsSpan(cursor + v * VertexBytes, VertexBytes);
                var x = ReadFloat(vertexSpan);
                var y = ReadFloat(vertexSpan[4..]);
                var z = ReadFloat(vertexSpan[8..]);
                var nx = ReadFloat(vertexSpan[12..]);
                var ny = ReadFloat(vertexSpan[16..]);
                var nz = ReadFloat(vertexSpan[20..]);
                if (nx * nx + ny * ny + nz * nz <= 0)
                {
                    throw new FormatException($"Mesh segment {s} vertex {v} has a zero-length normal.");
                }

                vertices[v] = new BuildingVertex(x, y, z, nx, ny, nz);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                minZ = Math.Min(minZ, z);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                maxZ = Math.Max(maxZ, z);
            }

            if (minX != headerMin[0] || minY != headerMin[1] || minZ != headerMin[2]
                || maxX != headerMax[0] || maxY != headerMax[1] || maxZ != headerMax[2])
            {
                throw new FormatException($"Mesh segment {s} header bounds do not equal the widened vertex bounds.");
            }

            cursor += vertexCount * VertexBytes;
            var indices = new uint[indexCount];
            for (var i = 0; i < indexCount; i++)
            {
                var index = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor + i * IndexBytes));
                if (index >= vertexCount)
                {
                    throw new FormatException($"Mesh segment {s} index {index} is outside its {vertexCount} vertices.");
                }

                indices[i] = index;
            }

            segments.Add(new BuildingMeshSegment(
                materialVariant,
                vertices,
                indices,
                headerMin[0],
                headerMin[1],
                headerMin[2],
                headerMax[0],
                headerMax[1],
                headerMax[2]));
            position = cursor + indexCount * IndexBytes;
        }

        return new BuildingMeshChunk(chunkX, chunkY, segments);
    }

    private static void ValidateSegmentCounts(int vertexCount, int indexCount, IReadOnlyList<uint> indices)
    {
        if (vertexCount is < 1 or > MaxVerticesPerSegment)
        {
            throw new FormatException($"Mesh segment vertex count {vertexCount} is outside 1 to {MaxVerticesPerSegment}.");
        }

        if (indexCount == 0 || indexCount % 3 != 0)
        {
            throw new FormatException($"Mesh segment index count {indexCount} must be a positive multiple of 3.");
        }

        foreach (var index in indices)
        {
            if (index >= vertexCount)
            {
                throw new FormatException($"Mesh segment index {index} is outside its {vertexCount} vertices.");
            }
        }
    }

    private static void ValidateChunkTotals(long vertexTotal, long indexTotal)
    {
        if (vertexTotal > MaxVerticesPerChunk || indexTotal > MaxIndicesPerChunk)
        {
            throw new FormatException($"Mesh chunk exceeds limits: {vertexTotal} vertices (max {MaxVerticesPerChunk}), {indexTotal} indices (max {MaxIndicesPerChunk}).");
        }
    }

    private static long ExpectedLength(int segmentCount, long vertexTotal, long indexTotal) =>
        checked(MeshHeaderBytes + (long)segmentCount * SegmentHeaderBytes + vertexTotal * VertexBytes + indexTotal * IndexBytes);

    private static void WriteFloat(Span<byte> span, ref int offset, double value)
    {
        var single = (float)value;
        if (!float.IsFinite(single))
        {
            throw new FormatException($"Mesh value {value} is not representable as a finite float32.");
        }

        BinaryPrimitives.WriteSingleLittleEndian(span[offset..], single);
        offset += 4;
    }

    private static double ReadFloat(ReadOnlySpan<byte> span)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(span);
        if (!float.IsFinite(value))
        {
            throw new FormatException("Mesh contains a non-finite float32 value.");
        }

        return value;
    }

    private static double[] ReadBounds(ReadOnlySpan<byte> span)
    {
        var bounds = new double[3];
        for (var i = 0; i < 3; i++)
        {
            bounds[i] = ReadFloat(span[(i * 4)..]);
        }

        return bounds;
    }
}
