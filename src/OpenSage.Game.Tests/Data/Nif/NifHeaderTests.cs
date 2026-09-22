using System;
using System.IO;
using OpenSage.FileFormats.Nif;
using Xunit;

namespace OpenSage.Tests.Data.Nif;

public sealed class NifHeaderTests
{
    private static NifTestDataBuilder ValidBuilder()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var zType = builder.AddBlockType("NiZBufferProperty");
        builder.AddBlock(nodeType, [0, 0, 0, 0]);
        builder.AddBlock(zType, [0, 0, 0, 0]);
        builder.AddBlock(nodeType, [0, 0, 0, 0]);
        return builder;
    }

    private static NifHeader Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var reader = new NifBinaryReader(stream);
        return NifHeader.Parse(reader);
    }

    [Fact]
    public void ParsesValidHeaderWithTypeNamesAndIndices()
    {
        var bytes = ValidBuilder().Build();

        var header = Parse(bytes);

        Assert.Equal("Gamebryo File Format, Version 10.1.0.0", header.HeaderString);
        Assert.Equal(0x0A010000u, header.Version);
        Assert.Equal(0u, header.UserVersion);
        Assert.Equal(3, header.NumBlocks);
        Assert.Equal(new[] { "NiNode", "NiZBufferProperty" }, header.BlockTypes);
        Assert.Equal(3, header.Blocks.Count);
        Assert.Equal("NiNode", header.Blocks[0].TypeName);
        Assert.Equal("NiZBufferProperty", header.Blocks[1].TypeName);
        Assert.Equal("NiNode", header.Blocks[2].TypeName);
        Assert.Equal(0, header.Blocks[0].TypeIndex);
        Assert.Equal(1, header.Blocks[1].TypeIndex);
        Assert.Equal(0, header.Blocks[2].TypeIndex);
        for (var i = 0; i < header.Blocks.Count; i++)
        {
            Assert.Equal(i, header.Blocks[i].BlockIndex);
        }
    }

    [Fact]
    public void TruncatedHeaderStringFails()
    {
        var builder = ValidBuilder();
        builder.HeaderString = "Gamebryo File Format, Version 9.9";
        Assert.Throws<NotSupportedException>(() => Parse(builder.Build()));
    }

    [Fact]
    public void WrongVersionFailsWithNotSupported()
    {
        var builder = ValidBuilder();
        builder.Version = 0x0A000100;
        var ex = Assert.Throws<NotSupportedException>(() => Parse(builder.Build()));
        Assert.Contains("0x0A000100", ex.Message);
    }

    [Fact]
    public void NonZeroUserVersionFailsWithNotSupported()
    {
        var builder = ValidBuilder();
        builder.UserVersion = 11;
        Assert.Throws<NotSupportedException>(() => Parse(builder.Build()));
    }

    [Fact]
    public void NonZeroGroupsFailsWithInvalidData()
    {
        var builder = ValidBuilder();
        builder.NumGroups = 2;
        Assert.Throws<InvalidDataException>(() => Parse(builder.Build()));
    }

    [Fact]
    public void OutOfRangeTypeIndexFailsWithInvalidData()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, [0, 0, 0, 0]);
        builder.AddBlock(nodeType, [0, 0, 0, 0]);
        builder.BlockTypeIndices[1] = 5;
        var ex = Assert.Throws<InvalidDataException>(() => Parse(builder.Build()));
        Assert.Contains("block 1", ex.Message);
    }

    [Fact]
    public void BlockCountExceedingRemainingBytesFails()
    {
        var stream = new MemoryStream();
        var writer = new NifBodyWriter(stream);
        writer.WriteString("Gamebryo File Format, Version 10.1.0.0\n");
        writer.WriteUInt32(0x0A010000);
        writer.WriteUInt32(0);
        // Declares 1,000,000 blocks but provides a 4-byte body.
        writer.WriteUInt32(1_000_000);
        writer.WriteUInt16(1);
        writer.WriteSizedString("NiNode");
        writer.WriteUInt16(0);
        writer.WriteUInt32(0);
        writer.WriteBytes([0, 0, 0, 0]);
        var ex = Assert.Throws<InvalidDataException>(() => Parse(stream.ToArray()));
        Assert.Contains("NumBlocks", ex.Message);
    }

    [Fact]
    public void MalformedTypeStringLengthFails()
    {
        // Handcraft: valid prefix, then a type-string length of 0x7FFFFFFF.
        var stream = new MemoryStream();
        var writer = new NifBodyWriter(stream);
        writer.WriteString("Gamebryo File Format, Version 10.1.0.0\n");
        writer.WriteUInt32(0x0A010000);
        writer.WriteUInt32(0);
        writer.WriteUInt32(1);
        writer.WriteUInt16(1);
        writer.WriteUInt32(0x7FFFFFFF);
        // Enough trailing bytes that the block-count check passes, so the
        // string-length check is what fails.
        writer.WriteBytes(new byte[16]);
        var ex = Assert.Throws<InvalidDataException>(() => Parse(stream.ToArray()));
        Assert.Contains("BlockTypes[0]", ex.Message);
    }

    [Fact]
    public void TruncatedStreamFails()
    {
        var bytes = ValidBuilder().Build();
        // Cut into the block-type indices so a later index read runs past the end.
        var truncated = bytes[..^20];
        Assert.Throws<InvalidDataException>(() => Parse(truncated));
    }
}
