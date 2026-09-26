using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.FileFormats.Casc;
using Xunit;

namespace Panopticon.Tests.Data.Casc;

public sealed class BlteDecoderTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "panopticon-casc-archive-" + Guid.NewGuid().ToString("N"));

    public BlteDecoderTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void SingleStoredFrameRoundTrips()
    {
        var decoded = BlteDecoder.Decode(new MemoryStream(BuildSingleFrameSpan('N', "plain"u8.ToArray())));

        Assert.Equal("plain"u8.ToArray(), decoded);
    }

    [Fact]
    public void MultiChunkStoredAndZlibFramesRoundTrip()
    {
        var span = BuildChunkedSpan(
            ('N', "stored"u8.ToArray()),
            ('Z', "compressed"u8.ToArray()));

        var decoded = BlteDecoder.Decode(new MemoryStream(span));

        Assert.Equal("storedcompressed"u8.ToArray(), decoded);
    }

    [Fact]
    public void BadChunkMd5Fails()
    {
        var span = BuildChunkedSpan(('N', "stored"u8.ToArray()));
        span[30 + 20] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => BlteDecoder.Decode(new MemoryStream(span)));
    }

    [Fact]
    public void EncryptedFrameFails()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => BlteDecoder.Decode(new MemoryStream(BuildSingleFrameSpan('E', "cipher"u8.ToArray()))));

        Assert.Contains("encrypted BLTE frame", ex.Message);
    }

    [Fact]
    public void UnknownFrameModeFailsNamingMode()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => BlteDecoder.Decode(new MemoryStream(BuildSingleFrameSpan('F', "future"u8.ToArray()))));

        Assert.Contains("F", ex.Message);
    }

    [Fact]
    public void ArchiveSetReadsSpanBytes()
    {
        var dataDirectory = Path.Combine(_tempDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllBytes(Path.Combine(dataDirectory, "data.001"), "0123456789abcdef"u8.ToArray());

        using var archives = new CascArchiveSet(dataDirectory);
        var bytes = archives.ReadSpan(new CascSpan(1, 4, 6));

        Assert.Equal("456789"u8.ToArray(), bytes);
    }

    private static byte[] BuildSingleFrameSpan(char mode, byte[] payload)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[30]);
        stream.Write("BLTE"u8);
        Span<byte> headerSize = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(headerSize, 0);
        stream.Write(headerSize);
        stream.WriteByte((byte)mode);
        stream.Write(mode == 'Z' ? Compress(payload) : payload);
        return stream.ToArray();
    }

    private static byte[] BuildChunkedSpan(params (char Mode, byte[] Payload)[] chunks)
    {
        var frames = chunks
            .Select(chunk =>
            {
                var payload = chunk.Mode == 'Z' ? Compress(chunk.Payload) : chunk.Payload;
                var frame = new byte[payload.Length + 1];
                frame[0] = (byte)chunk.Mode;
                payload.CopyTo(frame.AsSpan(1));
                return (Frame: frame, DecompressedSize: chunk.Payload.Length);
            })
            .ToArray();

        using var stream = new MemoryStream();
        stream.Write(new byte[30]);
        stream.Write("BLTE"u8);

        var headerSizeValue = 12 + frames.Length * 24;
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, headerSizeValue);
        stream.Write(header);
        stream.WriteByte(0x0F);
        stream.WriteByte((byte)(frames.Length >> 16));
        stream.WriteByte((byte)(frames.Length >> 8));
        stream.WriteByte((byte)frames.Length);

        Span<byte> tableEntry = stackalloc byte[24];
        foreach (var frame in frames)
        {
            tableEntry.Clear();
            BinaryPrimitives.WriteInt32BigEndian(tableEntry[..4], frame.Frame.Length);
            BinaryPrimitives.WriteInt32BigEndian(tableEntry[4..8], frame.DecompressedSize);
            MD5.HashData(frame.Frame).CopyTo(tableEntry[8..]);
            stream.Write(tableEntry);
        }

        foreach (var frame in frames)
        {
            stream.Write(frame.Frame);
        }

        return stream.ToArray();
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var stream = new MemoryStream();
        using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(bytes);
        }

        return stream.ToArray();
    }

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }
}
