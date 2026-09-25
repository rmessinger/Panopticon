using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.Tests.Data.Nif;

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

    public void WriteInt16(short value)
    {
        Span<byte> span = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span, value);
        stream.Write(span);
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
        FileFormats.Nif.NifMatrix33? rotation = null,
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
        if (rotation is { } nodeRotation)
        {
            WriteMatrix33(body, nodeRotation);
        }
        else
        {
            WriteMatrix33Identity(body);
        }

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

    public static void WriteMatrix33(NifBodyWriter body, FileFormats.Nif.NifMatrix33 rotation)
    {
        body.WriteSingle(rotation.M11);
        body.WriteSingle(rotation.M12);
        body.WriteSingle(rotation.M13);
        body.WriteSingle(rotation.M21);
        body.WriteSingle(rotation.M22);
        body.WriteSingle(rotation.M23);
        body.WriteSingle(rotation.M31);
        body.WriteSingle(rotation.M32);
        body.WriteSingle(rotation.M33);
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

public static class NifTestGeometry
{
    public static byte[] Shape(
        string name = "",
        int data = 0,
        int skin = -1,
        byte hasShader = 0,
        int[]? properties = null,
        (float X, float Y, float Z)? translation = null,
        float scale = 1.0f)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        NifTestBlocks.WriteRefList(body, []);
        body.WriteInt32(-1);
        body.WriteUInt16(0);
        NifTestBlocks.WriteVector3(body, translation ?? (0, 0, 0));
        NifTestBlocks.WriteMatrix33Identity(body);
        body.WriteSingle(scale);
        NifTestBlocks.WriteRefList(body, properties ?? []);
        body.WriteInt32(-1);
        body.WriteInt32(data);
        body.WriteInt32(skin);
        body.WriteByte(hasShader);
        return stream.ToArray();
    }

    public static byte[] GeometryData(
        int numVertices,
        (float X, float Y, float Z)[]? vertices = null,
        bool hasNormals = true,
        (float X, float Y, float Z)[]? normals = null,
        ushort dataFlags = 1,
        (float X, float Y, float Z) boundCenter = default,
        float boundRadius = 1.0f,
        bool hasColors = false,
        ushort consistency = 0x4000,
        (ushort V1, ushort V2, ushort V3)[]? triangles = null,
        int? numTrianglePoints = null,
        byte hasTriangles = 1,
        ushort[]? matchGroupIndices = null)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        vertices ??= DefaultVertices(numVertices);
        normals ??= vertices;
        triangles ??= [(0, 1, 2)];
        matchGroupIndices ??= [];

        body.WriteUInt32(0);
        body.WriteUInt16((ushort)numVertices);
        body.WriteByte(0);
        body.WriteByte(0);
        body.WriteByte(1);
        foreach (var v in vertices)
        {
            NifTestBlocks.WriteVector3(body, v);
        }

        body.WriteUInt16(dataFlags);
        body.WriteByte((byte)(hasNormals ? 1 : 0));
        if (hasNormals)
        {
            foreach (var n in normals)
            {
                NifTestBlocks.WriteVector3(body, n);
            }
        }

        NifTestBlocks.WriteVector3(body, boundCenter);
        body.WriteSingle(boundRadius);
        body.WriteByte((byte)(hasColors ? 1 : 0));
        var numUvSets = dataFlags & 0x3F;
        for (var set = 0; set < numUvSets; set++)
        {
            for (var i = 0; i < numVertices; i++)
            {
                body.WriteSingle(i * 0.1f);
                body.WriteSingle(1.0f - i * 0.1f);
            }
        }

        body.WriteUInt16(consistency);
        body.WriteUInt16((ushort)triangles.Length);
        body.WriteInt32(numTrianglePoints ?? 3 * triangles.Length);
        body.WriteByte(hasTriangles);
        foreach (var t in triangles)
        {
            body.WriteUInt16(t.V1);
            body.WriteUInt16(t.V2);
            body.WriteUInt16(t.V3);
        }

        if (matchGroupIndices.Length == 0)
        {
            body.WriteUInt16(0);
        }

        else
        {
            body.WriteUInt16(1);
            body.WriteUInt16((ushort)matchGroupIndices.Length);
            foreach (var index in matchGroupIndices)
            {
                body.WriteUInt16(index);
            }
        }

        return stream.ToArray();
    }

    private static (float, float, float)[] DefaultVertices(int count)
    {
        var result = new (float, float, float)[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = (i * 1.0f, i * 2.0f, i * 3.0f);
        }

        return result;
    }
}

public static class NifTestProperties
{
    public static byte[] Base(string name = "")
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        NifTestBlocks.WriteRefList(body, []);
        body.WriteInt32(-1);
        return stream.ToArray();
    }

    public static byte[] Material(
        (float R, float G, float B) ambient = default,
        (float R, float G, float B) diffuse = default,
        (float R, float G, float B) specular = default,
        (float R, float G, float B) emissive = default,
        float glossiness = 10.0f,
        float alpha = 1.0f,
        string name = "")
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteBytes(Base(name));
        WriteColor(body, ambient);
        WriteColor(body, diffuse);
        WriteColor(body, specular);
        WriteColor(body, emissive);
        body.WriteSingle(glossiness);
        body.WriteSingle(alpha);
        return stream.ToArray();
    }

    public static byte[] Texturing(
        uint applyMode = 4,
        uint textureCount = 7,
        (int Source, uint Clamp, uint Filter, uint UvSet, short Ps2L, short Ps2K)? baseSlot = null,
        string name = "",
        uint numShaderTextures = 0,
        byte? baseTransformFlag = null)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteBytes(Base(name));
        body.WriteUInt32(applyMode);
        body.WriteUInt32(textureCount);
        var slots = new[] { "Base", "Dark", "Detail", "Gloss", "Glow", "BumpMap", "Decal0", "Decal1", "Decal2", "Decal3" };
        for (var i = 0; i < slots.Length; i++)
        {
            if (i > 4 && textureCount <= (uint)i)
            {
                break;
            }

            if (i == 0 && baseSlot is { } slot)
            {
                body.WriteByte(1);
                body.WriteInt32(slot.Source);
                body.WriteUInt32(slot.Clamp);
                body.WriteUInt32(slot.Filter);
                body.WriteUInt32(slot.UvSet);
                body.WriteInt16(slot.Ps2L);
                body.WriteInt16(slot.Ps2K);
                body.WriteByte(baseTransformFlag ?? 0);
            }

            else
            {
                body.WriteByte(0);
            }
        }

        body.WriteUInt32(numShaderTextures);
        return stream.ToArray();
    }

    public static byte[] SourceTexture(
        string fileName = "model.tga",
        byte useExternal = 1,
        int pixelData = -1,
        uint pixelLayout = 3,
        uint useMipmaps = 1,
        uint alphaFormat = 1,
        byte isStatic = 1,
        string name = "")
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteBytes(Base(name));
        body.WriteByte(useExternal);
        body.WriteSizedString(fileName);
        body.WriteInt32(pixelData);
        body.WriteUInt32(pixelLayout);
        body.WriteUInt32(useMipmaps);
        body.WriteUInt32(alphaFormat);
        body.WriteByte(isStatic);
        return stream.ToArray();
    }

    public static byte[] Alpha(ushort flags = 0x12ED, byte threshold = 100)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteBytes(Base());
        body.WriteUInt16(flags);
        body.WriteByte(threshold);
        return stream.ToArray();
    }

    public static byte[] Specular(ushort flags = 1)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteBytes(Base());
        body.WriteUInt16(flags);
        return stream.ToArray();
    }

    public static byte[] ZBuffer(ushort flags = 3, uint function = 3)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteBytes(Base());
        body.WriteUInt16(flags);
        body.WriteUInt32(function);
        return stream.ToArray();
    }

    public static byte[] VertexColor(ushort flags = 0xCDCD, uint vertexMode = 0, uint lightingMode = 1)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteBytes(Base());
        body.WriteUInt16(flags);
        body.WriteUInt32(vertexMode);
        body.WriteUInt32(lightingMode);
        return stream.ToArray();
    }

    private static void WriteColor(NifBodyWriter body, (float R, float G, float B) color)
    {
        body.WriteSingle(color.R);
        body.WriteSingle(color.G);
        body.WriteSingle(color.B);
    }
}

public static class NifTestLod
{
    public static byte[] LodNode(int[] children, int lodData, ushort switchFlags = 1, int switchIndex = 0, string name = "body")
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        NifTestBlocks.WriteRefList(body, []);
        body.WriteInt32(-1);
        body.WriteUInt16(0);
        NifTestBlocks.WriteVector3(body, (0, 0, 0));
        NifTestBlocks.WriteMatrix33Identity(body);
        body.WriteSingle(1.0f);
        NifTestBlocks.WriteRefList(body, []);
        body.WriteInt32(-1);
        NifTestBlocks.WriteRefList(body, children);
        NifTestBlocks.WriteRefList(body, []);
        body.WriteUInt16(switchFlags);
        body.WriteInt32(switchIndex);
        body.WriteInt32(lodData);
        return stream.ToArray();
    }

    public static byte[] RangeLodData(params (float Near, float Far)[] ranges)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        NifTestBlocks.WriteVector3(body, (0, 0, 0));
        body.WriteUInt32((uint)ranges.Length);
        foreach (var (near, far) in ranges)
        {
            body.WriteSingle(near);
            body.WriteSingle(far);
        }

        return stream.ToArray();
    }
}

public static class NifTestKeyframe
{
    public static byte[] ControllerSequence(
        string name,
        string accumulationRoot,
        int textKeys,
        params (string TargetName, int Controller)[] controlledBlocks)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        body.WriteSizedString(accumulationRoot);
        body.WriteInt32(textKeys);
        body.WriteUInt32((uint)controlledBlocks.Length);
        foreach (var (targetName, controller) in controlledBlocks)
        {
            body.WriteSizedString(targetName);
            body.WriteInt32(controller);
        }

        return stream.ToArray();
    }

    public static byte[] TextKeyExtraData(string name, params (float Time, string Value)[] keys)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteSizedString(name);
        body.WriteUInt32((uint)keys.Length);
        foreach (var (time, value) in keys)
        {
            body.WriteSingle(time);
            body.WriteSizedString(value);
        }

        return stream.ToArray();
    }

    public static byte[] KeyframeController(
        int data = -1,
        ushort flags = 0x2C,
        float frequency = 1.0f,
        float phase = 0.0f,
        float startTime = 0.0f,
        float stopTime = 0.0f,
        int next = -1,
        int target = -1)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteInt32(next);
        body.WriteUInt16(flags);
        body.WriteSingle(frequency);
        body.WriteSingle(phase);
        body.WriteSingle(startTime);
        body.WriteSingle(stopTime);
        body.WriteInt32(target);
        body.WriteInt32(data);
        return stream.ToArray();
    }

    public static byte[] KeyframeData(
        IReadOnlyList<(float Time, float W, float X, float Y, float Z)>? quaternionRotationKeys = null,
        uint? rotationKeyTypeOverride = null,
        float? xyzRotationOrder = null,
        IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)>? xRotationKeys = null,
        IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)>? yRotationKeys = null,
        IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)>? zRotationKeys = null,
        IReadOnlyList<(float Time, (float X, float Y, float Z) Value, (float X, float Y, float Z)? Forward, (float X, float Y, float Z)? Backward)>? translationKeys = null,
        IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)>? scaleKeys = null,
        uint? translationKeyTypeOverride = null)
    {
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);

        if (xyzRotationOrder is { } order)
        {
            body.WriteUInt32(1);
            body.WriteUInt32(4);
            body.WriteSingle(order);
            WriteFloatGroup(body, xRotationKeys ?? []);
            WriteFloatGroup(body, yRotationKeys ?? []);
            WriteFloatGroup(body, zRotationKeys ?? []);
        }

        else if (quaternionRotationKeys is { Count: > 0 } quaternionKeys)
        {
            body.WriteUInt32((uint)quaternionKeys.Count);
            body.WriteUInt32(rotationKeyTypeOverride ?? 1);
            foreach (var (time, w, x, y, z) in quaternionKeys)
            {
                body.WriteSingle(time);
                body.WriteSingle(w);
                body.WriteSingle(x);
                body.WriteSingle(y);
                body.WriteSingle(z);
            }
        }

        else
        {
            body.WriteUInt32(0);
        }

        WriteVector3Group(body, translationKeys, translationKeyTypeOverride);
        WriteFloatGroup(body, scaleKeys ?? []);
        return stream.ToArray();
    }

    private static void WriteFloatGroup(
        NifBodyWriter body,
        IReadOnlyList<(float Time, float Value, float? Forward, float? Backward)> keys)
    {
        body.WriteUInt32((uint)keys.Count);
        if (keys.Count == 0)
        {
            return;
        }

        var quadratic = false;
        foreach (var key in keys)
        {
            if (key.Forward is not null || key.Backward is not null)
            {
                quadratic = true;
            }
        }

        body.WriteUInt32(quadratic ? 2u : 1u);
        foreach (var (time, value, forward, backward) in keys)
        {
            body.WriteSingle(time);
            body.WriteSingle(value);
            if (quadratic)
            {
                body.WriteSingle(forward ?? 0.0f);
                body.WriteSingle(backward ?? 0.0f);
            }
        }
    }

    private static void WriteVector3Group(
        NifBodyWriter body,
        IReadOnlyList<(float Time, (float X, float Y, float Z) Value, (float X, float Y, float Z)? Forward, (float X, float Y, float Z)? Backward)>? keys,
        uint? keyTypeOverride)
    {
        keys ??= [];
        body.WriteUInt32((uint)keys.Count);
        if (keys.Count == 0)
        {
            return;
        }

        if (keyTypeOverride is { } overrideType)
        {
            // Rejection tests inject an unsupported type word; keys are
            // written in linear shape because the parser stops at the type.
            body.WriteUInt32(overrideType);
            foreach (var (time, value, _, _) in keys)
            {
                body.WriteSingle(time);
                NifTestBlocks.WriteVector3(body, value);
            }

            return;
        }

        var quadratic = false;
        foreach (var key in keys)
        {
            if (key.Forward is not null || key.Backward is not null)
            {
                quadratic = true;
            }
        }

        body.WriteUInt32(quadratic ? 2u : 1u);
        foreach (var (time, value, forward, backward) in keys)
        {
            body.WriteSingle(time);
            NifTestBlocks.WriteVector3(body, value);
            if (quadratic)
            {
                NifTestBlocks.WriteVector3(body, forward ?? (0, 0, 0));
                NifTestBlocks.WriteVector3(body, backward ?? (0, 0, 0));
            }
        }
    }
}

public static class NifTestTexcache
{
    /// <summary>
    /// Builds a synthetic texcache byte stream: one NiPixelData block with the
    /// given DXT format and mip chain (sizes derived from the block math).
    /// </summary>
    public static byte[] Texcache(uint format, params (uint Width, uint Height)[] mips)
    {
        var block = new MemoryStream();
        var body = new NifBodyWriter(block);

        body.WriteUInt32(0);               // leading u32 (read by NifFile)
        body.WriteUInt32(format);          // format
        body.WriteUInt32(0);               // red mask
        body.WriteUInt32(0);               // green mask
        body.WriteUInt32(0);               // blue mask
        body.WriteUInt32(0);               // alpha mask
        body.WriteUInt32(0);               // bits per pixel
        body.WriteUInt32(format);          // format repeat
        body.WriteUInt32(0);               // unknown 1
        body.WriteUInt32(0);               // unknown 2
        body.WriteInt32(-1);               // palette
        body.WriteUInt32((uint)mips.Length);
        body.WriteUInt32(0);               // bytes per pixel

        uint blockBytes = format == 4 ? 8u : 16u;
        uint offset = 0;
        var pixels = new MemoryStream();
        foreach (var (w, h) in mips)
        {
            body.WriteUInt32(w);
            body.WriteUInt32(h);
            body.WriteUInt32(offset);
            var size = Math.Max(1u, (w + 3) / 4) * Math.Max(1u, (h + 3) / 4) * blockBytes;
            var data = new byte[size];
            pixels.Write(data);
            offset += size;
        }

        body.WriteUInt32((uint)pixels.Length);
        var blockBytes2 = block.ToArray();
        var pixelBytes = pixels.ToArray();

        var builder = new NifTestDataBuilder();
        var pixelType = builder.AddBlockType("NiPixelData");
        builder.AddBlock(pixelType, [.. blockBytes2, .. pixelBytes]);
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }
}
