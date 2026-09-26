using System;
using System.Buffers.Binary;
using System.IO;
using Panopticon.FileFormats.Casc;
using Xunit;

namespace Panopticon.Tests.Data.Casc;

public sealed class CascEncodingTests
{
    [Fact]
    public void ParsesEntriesFromMultipleContentPages()
    {
        var contentKey1 = CascKey.Parse("11111111111111111111111111111111");
        var contentKey2 = CascKey.Parse("22222222222222222222222222222222");
        var encodingKey1 = CascKey.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var encodingKey2 = CascKey.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var encodingKey3 = CascKey.Parse("cccccccccccccccccccccccccccccccc");

        var encoding = CascEncoding.Parse(BuildEncoding(
            [(contentKey1, 123, new[] { encodingKey1, encodingKey2 })],
            [(contentKey2, 456, new[] { encodingKey3 })]));

        Assert.True(encoding.TryGetEntry(contentKey1, out var entry1));
        Assert.Equal(encodingKey1, entry1.EncodingKey);
        Assert.Equal(123, entry1.Size);
        Assert.True(encoding.TryGetEntry(contentKey2, out var entry2));
        Assert.Equal(encodingKey3, entry2.EncodingKey);
        Assert.Equal(456, entry2.Size);
        Assert.Equal(2, encoding.Count);
    }

    [Fact]
    public void InvalidKeySizesFail()
    {
        var data = BuildEncoding([], []);
        data[3] = 9;

        Assert.Throws<NotSupportedException>(() => CascEncoding.Parse(data));
    }

    private static byte[] BuildEncoding(
        (CascKey ContentKey, long Size, CascKey[] EncodingKeys)[] page1,
        (CascKey ContentKey, long Size, CascKey[] EncodingKeys)[] page2)
    {
        using var stream = new MemoryStream();
        stream.Write("EN"u8);
        stream.WriteByte(1);
        stream.WriteByte(16);
        stream.WriteByte(16);
        Span<byte> header = stackalloc byte[17];
        BinaryPrimitives.WriteUInt16BigEndian(header[..2], 1);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..4], 1);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..8], 2);
        BinaryPrimitives.WriteUInt32BigEndian(header[8..12], 0);
        header[12] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(header[13..17], 0);
        stream.Write(header);
        stream.Write(new byte[64]);
        WritePage(stream, page1);
        WritePage(stream, page2);
        return stream.ToArray();
    }

    private static void WritePage(
        Stream stream,
        (CascKey ContentKey, long Size, CascKey[] EncodingKeys)[] entries)
    {
        var page = new byte[1024];
        var offset = 0;
        foreach (var entry in entries)
        {
            page[offset++] = (byte)entry.EncodingKeys.Length;
            WriteUInt40BigEndian(page.AsSpan(offset, 5), entry.Size);
            offset += 5;
            entry.ContentKey.WriteTo(page.AsSpan(offset, CascKey.Length));
            offset += CascKey.Length;
            foreach (var encodingKey in entry.EncodingKeys)
            {
                encodingKey.WriteTo(page.AsSpan(offset, CascKey.Length));
                offset += CascKey.Length;
            }
        }

        stream.Write(page);
    }

    private static void WriteUInt40BigEndian(Span<byte> destination, long value)
    {
        for (var i = 4; i >= 0; i--)
        {
            destination[i] = (byte)value;
            value >>= 8;
        }
    }
}
