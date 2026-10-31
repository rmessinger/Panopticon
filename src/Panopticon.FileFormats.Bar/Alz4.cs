using System;
using System.Buffers.Binary;
using System.IO;

namespace Panopticon.FileFormats.Bar;

/// <summary>
/// AoE3 DE alz4 payload: a 16-byte header followed by a raw LZ4 block.
/// Header: 'alz4' magic, u32 decompressedSize, u32 compressedSize, u32 version (1).
/// </summary>
public static class Alz4
{
    public const int HeaderSize = 16;

    private const uint Magic = 0x347A6C61; // "alz4" little-endian
    private const uint SupportedVersion = 1;

    /// <summary>
    /// Returns true when the stream starts with the alz4 magic; the stream
    /// position is restored.
    /// </summary>
    public static bool IsAlz4(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek || stream.Length - stream.Position < 4)
        {
            return false;
        }

        var header = new byte[4];
        var read = stream.Read(header, 0, header.Length);
        stream.Seek(-read, SeekOrigin.Current);

        return read == header.Length && BinaryPrimitives.ReadUInt32LittleEndian(header) == Magic;
    }

    public static byte[] Decompress(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderSize)
        {
            throw new InvalidDataException($"alz4 payload is smaller than its {HeaderSize}-byte header.");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]);
        if (magic != Magic)
        {
            throw new InvalidDataException($"Invalid alz4 magic 0x{magic:X8}.");
        }

        var decompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
        var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
        var version = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));
        if (version != SupportedVersion)
        {
            throw new InvalidDataException($"Unsupported alz4 version {version}; expected {SupportedVersion}.");
        }

        if (compressedSize != payload.Length - HeaderSize)
        {
            throw new InvalidDataException(
                $"alz4 compressed size {compressedSize} does not match payload length {payload.Length - HeaderSize}.");
        }

        var destination = new byte[decompressedSize];
        var written = Lz4BlockDecoder.Decode(payload.Slice(HeaderSize, (int)compressedSize), destination);
        if (written != destination.Length)
        {
            throw new InvalidDataException(
                $"LZ4 block produced {written} bytes; alz4 header records {destination.Length}.");
        }

        return destination;
    }
}
