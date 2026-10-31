using System;
using System.IO;
using System.Text;
using Panopticon.FileFormats.Bar;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Builds synthetic BAR v6 images for install-free tests: fixed 0x128-byte
/// header, a root-path prefixed entry table at 0x128, then payload regions
/// aligned to 2048 bytes.
/// </summary>
internal static class SyntheticBarBuilder
{
    internal readonly record struct EntrySpec(
        string RelativePath,
        byte[] StoredPayload,
        uint DecompressedSize,
        uint CompressionFlag);

    internal static EntrySpec Stored(string relativePath, byte[] payload)
    {
        return new EntrySpec(relativePath, payload, (uint)payload.Length, 0);
    }

    internal static EntrySpec Alz4(string relativePath, byte[] decompressedPayload)
    {
        return new EntrySpec(relativePath, WrapInAlz4(decompressedPayload), (uint)decompressedPayload.Length, 1);
    }

    internal static byte[] WrapInAlz4(byte[] data)
    {
        var block = BuildLiteralOnlyLz4Block(data);

        using var payloadStream = new MemoryStream();
        using var payloadWriter = new BinaryWriter(payloadStream, Encoding.Unicode, leaveOpen: true);
        payloadWriter.Write(Encoding.ASCII.GetBytes("alz4"));
        payloadWriter.Write((uint)data.Length);
        payloadWriter.Write((uint)block.Length);
        payloadWriter.Write(1u);
        payloadWriter.Write(block);

        return payloadStream.ToArray();
    }

    internal static EntrySpec Flagged(string relativePath, byte[] storedPayload, uint decompressedSize, uint compressionFlag)
    {
        return new EntrySpec(relativePath, storedPayload, decompressedSize, compressionFlag);
    }

    internal static byte[] Build(string rootPath, params EntrySpec[] entries)
    {
        var tableOffset = 0x128L;
        var entryCount = (uint)entries.Length;

        var tableSize = 4 + rootPath.Length * 2 + 4;
        foreach (var entry in entries)
        {
            tableSize += 8 + 4 + 4 + 4 + 4 + entry.RelativePath.Length * 2 + 4;
        }

        var offsets = new long[entries.Length];
        var nextPayloadOffset = tableOffset + tableSize;
        for (var i = 0; i < entries.Length; i++)
        {
            offsets[i] = nextPayloadOffset;
            nextPayloadOffset += (entries[i].StoredPayload.Length + 2047) & ~2047;
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("ESPN"));
        writer.Write(6u);
        writer.Write(0x44332211u);
        writer.BaseStream.Position = 0x114;
        writer.Write(0u); // checksum
        writer.Write(entryCount);
        writer.Write(0u); // reserved
        writer.Write(tableOffset);

        writer.BaseStream.Position = tableOffset;
        writer.Write((uint)rootPath.Length);
        writer.Write(Encoding.Unicode.GetBytes(rootPath));
        writer.Write(entryCount);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            writer.Write((ulong)offsets[i]);
            writer.Write(entry.DecompressedSize);
            writer.Write((uint)entry.StoredPayload.Length);
            writer.Write((uint)((entry.StoredPayload.Length + 2047) & ~2047));
            writer.Write((uint)entry.RelativePath.Length);
            writer.Write(Encoding.Unicode.GetBytes(entry.RelativePath));
            writer.Write(entry.CompressionFlag);
        }

        for (var i = 0; i < entries.Length; i++)
        {
            writer.BaseStream.Position = offsets[i];
            writer.Write(entries[i].StoredPayload);
        }

        writer.Flush();
        return stream.ToArray();
    }

    // A literals-only block is a valid LZ4 encoding of incompressible data.
    private static byte[] BuildLiteralOnlyLz4Block(byte[] data)
    {
        using var stream = new MemoryStream();

        var length = data.Length;
        var nibble = Math.Min(15, length);
        stream.WriteByte((byte)(nibble << 4));
        length -= nibble;

        while (nibble == 15)
        {
            var extra = Math.Min(255, length);
            stream.WriteByte((byte)extra);
            length -= extra;

            if (extra < 255)
            {
                break;
            }
        }

        stream.Write(data, 0, data.Length);
        return stream.ToArray();
    }
}
