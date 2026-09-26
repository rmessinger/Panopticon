using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Panopticon.FileFormats.Casc;

public static class BlteDecoder
{
    private const int ArchiveHeaderSize = 30;
    private const byte ChunkTableFlags = 0x0F;

    public static byte[] Decode(Stream span)
    {
        ArgumentNullException.ThrowIfNull(span);

        using var memory = new MemoryStream();
        span.CopyTo(memory);
        var data = memory.ToArray();
        if (data.Length < ArchiveHeaderSize + 8)
        {
            throw new InvalidDataException("BLTE span is truncated.");
        }

        return DecodeBlte(data.AsSpan(ArchiveHeaderSize));
    }

    private static byte[] DecodeBlte(ReadOnlySpan<byte> data)
    {
        if (!data[..4].SequenceEqual("BLTE"u8))
        {
            throw new InvalidDataException("BLTE span has an invalid magic.");
        }

        var headerSize = BinaryPrimitives.ReadUInt32BigEndian(data[4..8]);
        if (headerSize == 0)
        {
            return DecodeFrame(data[8..], expectedSize: null);
        }

        if (headerSize > data.Length || headerSize < 12)
        {
            throw new InvalidDataException("BLTE chunk table is invalid.");
        }

        if (data[8] != ChunkTableFlags)
        {
            throw new InvalidDataException($"BLTE chunk table uses unsupported flags 0x{data[8]:X2}.");
        }

        var chunkCount = ReadUInt24BigEndian(data[9..12]);
        var tableSize = 12 + chunkCount * 24;
        if (tableSize != headerSize)
        {
            throw new InvalidDataException("BLTE chunk table size does not match the header size.");
        }

        var chunks = new ChunkInfo[chunkCount];
        var tableOffset = 12;
        for (var i = 0; i < chunkCount; i++)
        {
            var compressedSize = BinaryPrimitives.ReadInt32BigEndian(data[tableOffset..(tableOffset + 4)]);
            var decompressedSize = BinaryPrimitives.ReadInt32BigEndian(data[(tableOffset + 4)..(tableOffset + 8)]);
            var md5 = data[(tableOffset + 8)..(tableOffset + 24)].ToArray();
            chunks[i] = new ChunkInfo(compressedSize, decompressedSize, md5);
            tableOffset += 24;
        }

        using var output = new MemoryStream();
        var frameOffset = checked((int)headerSize);
        foreach (var chunk in chunks)
        {
            if (chunk.CompressedSize < 1 || frameOffset + chunk.CompressedSize > data.Length)
            {
                throw new InvalidDataException("BLTE chunk frame is truncated.");
            }

            var frame = data.Slice(frameOffset, chunk.CompressedSize);
            var hash = MD5.HashData(frame);
            if (!hash.AsSpan().SequenceEqual(chunk.Md5))
            {
                throw new InvalidDataException("BLTE chunk MD5 mismatch.");
            }

            var decoded = DecodeFrame(frame, chunk.DecompressedSize);
            output.Write(decoded);
            frameOffset += chunk.CompressedSize;
        }

        return output.ToArray();
    }

    private static byte[] DecodeFrame(ReadOnlySpan<byte> frame, int? expectedSize)
    {
        if (frame.Length == 0)
        {
            throw new InvalidDataException("BLTE frame is empty.");
        }

        byte[] decoded;
        var payload = frame[1..];
        switch (frame[0])
        {
            case (byte)'N':
                decoded = payload.ToArray();
                break;
            case (byte)'Z':
                decoded = Inflate(payload);
                break;
            case (byte)'E':
                throw new NotSupportedException("encrypted BLTE frame is not supported.");
            default:
                throw new NotSupportedException($"Unsupported BLTE frame mode 0x{frame[0]:X2} ('{(char)frame[0]}').");
        }

        if (expectedSize.HasValue && decoded.Length != expectedSize.Value)
        {
            throw new InvalidDataException("BLTE chunk decompressed size mismatch.");
        }

        return decoded;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> payload)
    {
        using var compressed = new MemoryStream(payload.ToArray());
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static int ReadUInt24BigEndian(ReadOnlySpan<byte> bytes)
    {
        return (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
    }

    private sealed record ChunkInfo(int CompressedSize, int DecompressedSize, byte[] Md5);
}
