using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Panopticon.FileFormats.Casc;
using Xunit;

namespace Panopticon.Tests.Data.Casc;

public sealed class MndxRootTests
{
    [Fact]
    public void ParsesPackagesNamesAndDropsEqualDuplicates()
    {
        var contentKey1 = CascKey.Parse("11111111111111111111111111111111");
        var contentKey2 = CascKey.Parse("22222222222222222222222222222222");
        var root = MndxRoot.Parse(BuildMndxRoot(contentKey1, contentKey1, contentKey2));

        Assert.Equal(2, root.Entries.Count);
        Assert.Contains(root.Entries, e =>
            e.Path == "mods/a.sc2mod/base.sc2assets/assets/unit.m3" &&
            e.ContentKey == contentKey1 &&
            e.Size == 10);
        Assert.Contains(root.Entries, e =>
            e.Path == "mods/b.sc2mod/base.sc2assets/assets/other.dds" &&
            e.ContentKey == contentKey2 &&
            e.Size == 20);
    }

    [Fact]
    public void DuplicatePathWithDifferentContentKeyFails()
    {
        var contentKey1 = CascKey.Parse("11111111111111111111111111111111");
        var contentKey2 = CascKey.Parse("22222222222222222222222222222222");
        var contentKey3 = CascKey.Parse("33333333333333333333333333333333");

        Assert.Throws<InvalidDataException>(() => MndxRoot.Parse(BuildMndxRoot(contentKey1, contentKey3, contentKey2)));
    }

    [Fact]
    public void NonMndxRootFails()
    {
        Assert.Throws<NotSupportedException>(() => MndxRoot.Parse("ROOT"u8.ToArray()));
    }

    [Fact]
    public void UnsupportedVersionFails()
    {
        var data = BuildMndxRoot(
            CascKey.Parse("11111111111111111111111111111111"),
            CascKey.Parse("11111111111111111111111111111111"),
            CascKey.Parse("22222222222222222222222222222222"));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4, 4), 1);

        Assert.Throws<NotSupportedException>(() => MndxRoot.Parse(data));
    }

    private static byte[] BuildMndxRoot(CascKey firstKey, CascKey duplicateKey, CascKey secondKey)
    {
        var packages = MarTestTrieBuilder.BuildTailTrie(
            [
                "mods/a.sc2mod/base.sc2assets",
                "mods/b.sc2mod/base.sc2assets"
            ]);
        var names = MarTestTrieBuilder.BuildTailTrie(
            [
                "assets/unit.m3",
                "assets/other.dds"
            ]);
        var full = MarTestTrieBuilder.BuildTailTrie(["unused/full/name"]);
        var mars = new[] { packages, names, full };

        using var stream = new MemoryStream();
        stream.Write(new byte[48]);
        stream.Write(new byte[mars.Length * 20]);
        var contentEntryOffset = checked((int)stream.Position);
        WriteCKeyEntry(stream, 0, firstKey, 10);
        WriteCKeyEntry(stream, 0x80000000, duplicateKey, 10);
        WriteCKeyEntry(stream, 0x80000001, secondKey, 20);

        var marOffsets = new int[mars.Length];
        for (var i = 0; i < mars.Length; i++)
        {
            marOffsets[i] = checked((int)stream.Position);
            stream.Write(mars[i]);
        }

        var data = stream.ToArray();
        "MNDX"u8.CopyTo(data.AsSpan(0, 4));
        WriteInt32(data, 4, 2);
        WriteInt32(data, 8, 2);
        WriteInt32(data, 12, 97563);
        WriteInt32(data, 16, 97563);
        WriteInt32(data, 20, 48);
        WriteInt32(data, 24, mars.Length);
        WriteInt32(data, 28, 20);
        WriteInt32(data, 32, contentEntryOffset);
        WriteInt32(data, 36, 3);
        WriteInt32(data, 40, 2);
        WriteInt32(data, 44, 24);

        for (var i = 0; i < mars.Length; i++)
        {
            var offset = 48 + i * 20;
            WriteInt32(data, offset, i);
            WriteInt32(data, offset + 4, mars[i].Length);
            WriteInt32(data, offset + 8, 0);
            WriteInt32(data, offset + 12, marOffsets[i]);
            WriteInt32(data, offset + 16, 0);
        }

        return data;
    }

    private static void WriteCKeyEntry(Stream stream, uint flags, CascKey key, int size)
    {
        Span<byte> entry = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(entry[..4], flags);
        key.WriteTo(entry[4..20]);
        BinaryPrimitives.WriteInt32LittleEndian(entry[20..24], size);
        stream.Write(entry);
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
    }
}
