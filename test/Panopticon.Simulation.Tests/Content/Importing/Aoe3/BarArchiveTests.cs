using System;
using System.IO;
using System.Text;
using Panopticon.FileFormats.Bar;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public sealed class BarArchiveTests
{
    [Fact]
    public void ReadParsesHeaderTableAndEntries()
    {
        var stored = Encoding.ASCII.GetBytes("hello stored entry");
        var compressed = new byte[300];
        for (var i = 0; i < compressed.Length; i++)
        {
            compressed[i] = (byte)(i % 7);
        }

        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Stored(@"units\a.txt", stored),
            SyntheticBarBuilder.Alz4(@"units\b.bin", compressed),
            SyntheticBarBuilder.Flagged(@"units\c.dat", new byte[8], 8, 2));

        var archive = BarArchive.Read(new MemoryStream(image));

        Assert.Equal("Art\\", archive.RootPath);
        Assert.Equal(3, archive.Entries.Count);

        var first = archive.Entries[0];
        Assert.Equal(@"Art\units\a.txt", first.FullName);
        Assert.Equal((uint)stored.Length, first.DecompressedSize);
        Assert.Equal((uint)stored.Length, first.StoredSize);
        Assert.Equal((uint)2048, first.PaddedSize);
        Assert.Equal(BarCompression.None, first.Compression);

        var second = archive.Entries[1];
        Assert.Equal(@"Art\units\b.bin", second.FullName);
        Assert.Equal((uint)compressed.Length, second.DecompressedSize);
        Assert.Equal(BarCompression.Alz4, second.Compression);

        var third = archive.Entries[2];
        Assert.Equal(@"Art\units\c.dat", third.FullName);
        Assert.Equal(BarCompression.Encrypted, third.Compression);
    }

    [Fact]
    public void OpenStoredEntryReadsExactPayload()
    {
        var payload = Encoding.ASCII.GetBytes("hello stored entry");

        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Stored(@"units\a.txt", payload));

        var archive = BarArchive.Read(new MemoryStream(image));

        using var stream = archive.Entries[0].Open();
        Assert.Equal((uint)payload.Length, (uint)stream.Length);

        var buffer = new byte[payload.Length];
        stream.ReadExactly(buffer, 0, buffer.Length);

        Assert.Equal(payload, buffer);
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public void OpenAlz4EntryDecompresses()
    {
        var payload = new byte[300];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 7);
        }

        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Alz4(@"units\b.bin", payload));

        var archive = BarArchive.Read(new MemoryStream(image));

        using var stream = archive.Entries[0].Open();

        var buffer = new byte[payload.Length];
        stream.ReadExactly(buffer, 0, buffer.Length);

        Assert.Equal(payload, buffer);
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public void OpenAlz4EntryDecompressesMatchCompressedBlock()
    {
        // alz4 header around the overlapping-match LZ4 vector: "abbbbbbbb".
        var stored = new byte[]
        {
            0x61, 0x6C, 0x7A, 0x34, // "alz4"
            0x08, 0x00, 0x00, 0x00, // decompressed size 8
            0x05, 0x00, 0x00, 0x00, // compressed size 5
            0x01, 0x00, 0x00, 0x00, // version 1
            0x22, (byte)'a', (byte)'b', 0x01, 0x00,
        };

        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Flagged(@"units\b.bin", stored, 8, 1));

        var archive = BarArchive.Read(new MemoryStream(image));

        using var stream = archive.Entries[0].Open();

        var buffer = new byte[8];
        stream.ReadExactly(buffer, 0, buffer.Length);

        Assert.Equal("a" + new string('b', 7), Encoding.ASCII.GetString(buffer));
    }

    [Fact]
    public void OpenAlz4EntryWithMismatchedRecordedSizeThrows()
    {
        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Alz4(@"units\b.bin", Encoding.ASCII.GetBytes("payload")));

        // Entry table at 0x128: u32 rootPathLength, "Art\" (8 bytes), u32 entryCount,
        // then the first entry's u64 offset and u32 decompressed size.
        image[0x128 + 4 + 8 + 4 + 8] += 1;

        var archive = BarArchive.Read(new MemoryStream(image));

        Assert.Throws<InvalidDataException>(() => archive.Entries[0].Open());
    }

    [Fact]
    public void OpenEncryptedEntryThrowsWithEntryPath()
    {
        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Flagged(@"units\c.dat", new byte[8], 8, 2));

        var archive = BarArchive.Read(new MemoryStream(image));

        var exception = Assert.Throws<NotSupportedException>(() => archive.Entries[0].Open());
        Assert.Contains(@"Art\units\c.dat", exception.Message);
    }

    [Fact]
    public void OpenUnknownCompressionFlagThrows()
    {
        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Flagged(@"units\d.dat", new byte[4], 4, 9));

        var archive = BarArchive.Read(new MemoryStream(image));

        var exception = Assert.Throws<NotSupportedException>(() => archive.Entries[0].Open());
        Assert.Contains("9", exception.Message);
    }

    [Fact]
    public void ReadThrowsOnHeaderCountMismatch()
    {
        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Stored(@"units\a.txt", Encoding.ASCII.GetBytes("data")));

        image[0x118] += 1;

        Assert.Throws<InvalidDataException>(() => BarArchive.Read(new MemoryStream(image)));
    }

    [Fact]
    public void ReadThrowsOnBadMagic()
    {
        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Stored(@"units\a.txt", Encoding.ASCII.GetBytes("data")));

        image[0] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => BarArchive.Read(new MemoryStream(image)));
    }

    [Fact]
    public void ReadThrowsOnUnsupportedVersion()
    {
        var image = SyntheticBarBuilder.Build(
            "Art\\",
            SyntheticBarBuilder.Stored(@"units\a.txt", Encoding.ASCII.GetBytes("data")));

        image[4] = 7;

        Assert.Throws<InvalidDataException>(() => BarArchive.Read(new MemoryStream(image)));
    }
}
