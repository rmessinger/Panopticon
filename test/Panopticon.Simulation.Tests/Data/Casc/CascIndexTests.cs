using System;
using System.Buffers.Binary;
using System.IO;
using Panopticon.FileFormats.Casc;
using Xunit;

namespace Panopticon.Tests.Data.Casc;

public sealed class CascIndexTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "panopticon-casc-index-" + Guid.NewGuid().ToString("N"));

    public CascIndexTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void LoadsHighestBucketVersionAndKeepsFirstDuplicate()
    {
        var firstKey = CascKey.Parse("00112233445566778899aabbccddeeff");
        var duplicateKey = CascKey.Parse("101122334455667788ffffffffffffff");
        WriteIndex("0000000001.idx", (firstKey, 32, 100));
        WriteIndex("0000000002.idx", (firstKey, 0x0040000010, 5), (duplicateKey, 0x0080000020, 7));

        var index = CascIndex.Load(_tempDirectory);

        Assert.True(index.TryGetSpan(firstKey, out var firstSpan));
        Assert.Equal(new CascSpan(1, 16, 5), firstSpan);
        Assert.True(index.TryGetSpan(duplicateKey, out var duplicateSpan));
        Assert.Equal(new CascSpan(2, 32, 7), duplicateSpan);
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void DuplicatesResolveFirstWins()
    {
        var key = CascKey.Parse("00112233445566778899aabbccddeeff");
        WriteIndex("0000000001.idx", (key, 0x0040000010, 5), (key, 0x0080000020, 7));

        var index = CascIndex.Load(_tempDirectory);

        Assert.True(index.TryGetSpan(key, out var span));
        Assert.Equal(new CascSpan(1, 16, 5), span);
    }

    [Fact]
    public void UnsupportedVersionFails()
    {
        var key = CascKey.Parse("00112233445566778899aabbccddeeff");
        WriteIndex("0000000001.idx", 6, (key, 0x0040000010, 5));

        Assert.Throws<NotSupportedException>(() => CascIndex.Load(_tempDirectory));
    }

    private void WriteIndex(string fileName, params (CascKey Key, long Offset, int Size)[] entries)
    {
        WriteIndex(fileName, 7, entries);
    }

    private void WriteIndex(string fileName, ushort version, params (CascKey Key, long Offset, int Size)[] entries)
    {
        using var stream = File.Create(Path.Combine(_tempDirectory, fileName));
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header[..4], 8);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..10], version);
        header[10] = 0;
        header[11] = 0;
        header[12] = 4;
        header[13] = 5;
        header[14] = 9;
        header[15] = 30;
        stream.Write(header);

        Span<byte> entriesHeader = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(entriesHeader[..4], (uint)(entries.Length * 18));
        stream.Write(entriesHeader);

        Span<byte> keyBytes = stackalloc byte[CascKey.Length];
        Span<byte> record = stackalloc byte[18];
        foreach (var entry in entries)
        {
            record.Clear();
            entry.Key.WriteTo(keyBytes);
            keyBytes[..9].CopyTo(record);
            WriteUInt40BigEndian(record[9..14], entry.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(record[14..18], entry.Size);
            stream.Write(record);
        }
    }

    private static void WriteUInt40BigEndian(Span<byte> destination, long value)
    {
        for (var i = 4; i >= 0; i--)
        {
            destination[i] = (byte)value;
            value >>= 8;
        }
    }

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }
}
