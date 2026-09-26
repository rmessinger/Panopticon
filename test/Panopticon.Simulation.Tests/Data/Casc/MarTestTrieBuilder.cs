using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Panopticon.Tests.Data.Casc;

internal static class MarTestTrieBuilder
{
    public static byte[] BuildTailTrie(string[] keys, bool topLevel = true)
    {
        using var stream = new MemoryStream();
        if (topLevel)
        {
            WriteUInt32(stream, 0x0052414D);
        }

        WriteEmptyBitVector(stream);
        WriteTerminalAndLinkTrie(stream, keys, linkTargets: null, writeNestedTrie: null);
        return stream.ToArray();
    }

    public static byte[] BuildNestedTrie(string[] keys)
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, 0x0052414D);
        WriteEmptyBitVector(stream);
        var nested = BuildTailTrie(keys, topLevel: false);
        var linkTargets = new int[keys.Length];
        for (var i = 0; i < linkTargets.Length; i++)
        {
            linkTargets[i] = i + 1;
        }

        WriteTerminalAndLinkTrie(stream, keys, linkTargets, () => stream.Write(nested));
        return stream.ToArray();
    }

    public static byte[] BuildBitVector(bool[] bits)
    {
        using var stream = new MemoryStream();
        WriteBitVector(stream, bits);
        return stream.ToArray();
    }

    private static void WriteTerminalAndLinkTrie(
        Stream stream,
        string[] keys,
        int[]? linkTargets,
        Action? writeNestedTrie)
    {
        var nodeCount = keys.Length;
        var bitCount = nodeCount + 1;
        var terminalBits = new bool[bitCount];
        var linkBits = new bool[bitCount];
        var bases = new byte[bitCount];
        var extras = new int[nodeCount];
        using var tail = new MemoryStream();

        for (var i = 0; i < keys.Length; i++)
        {
            var node = i + 1;
            terminalBits[node] = true;
            linkBits[node] = true;
            var link = linkTargets is null ? checked((int)tail.Position) : linkTargets[i];
            bases[node] = (byte)link;
            extras[i] = link >> 8;
            if (linkTargets is null)
            {
                tail.Write(Encoding.UTF8.GetBytes(keys[i]));
                tail.WriteByte(0);
            }
        }

        WriteBitVector(stream, terminalBits);
        WriteBitVector(stream, linkBits);
        WriteVector(stream, bases);
        WriteFlatVector(stream, extras);
        WriteVector(stream, tail.ToArray());
        WriteEmptyBitVector(stream);
        writeNestedTrie?.Invoke();
        WriteVector(stream, []);
        WriteUInt32(stream, (uint)nodeCount);
        WriteUInt32(stream, 0);
    }

    private static void WriteEmptyBitVector(Stream stream)
    {
        WriteBitVector(stream, []);
    }

    private static void WriteBitVector(Stream stream, bool[] bits)
    {
        var words = new uint[(bits.Length + 31) / 32];
        var ones = 0;
        for (var i = 0; i < bits.Length; i++)
        {
            if (!bits[i])
            {
                continue;
            }

            words[i / 32] |= 1u << (i % 32);
            ones++;
        }

        var data = new byte[words.Length * 4];
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4, 4), words[i]);
        }

        WriteVector(stream, data);
        WriteUInt32(stream, (uint)bits.Length);
        WriteUInt32(stream, (uint)ones);
        WriteVector(stream, []);
        WriteVector(stream, []);
        WriteVector(stream, []);
    }

    private static void WriteFlatVector(Stream stream, int[] values)
    {
        const int BitsPerEntry = 16;
        var bitCount = values.Length * BitsPerEntry;
        var words = new uint[(bitCount + 31) / 32];
        for (var i = 0; i < values.Length; i++)
        {
            var bitPosition = i * BitsPerEntry;
            var word = bitPosition / 32;
            var bit = bitPosition % 32;
            words[word] |= (uint)values[i] << bit;
            if (bit + BitsPerEntry > 32)
            {
                words[word + 1] |= (uint)values[i] >> (32 - bit);
            }
        }

        var data = new byte[words.Length * 4];
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4, 4), words[i]);
        }

        WriteVector(stream, data);
        WriteUInt32(stream, BitsPerEntry);
        WriteUInt32(stream, 0xFFFF);
        WriteUInt64(stream, (ulong)values.Length);
    }

    private static void WriteVector(Stream stream, byte[] bytes)
    {
        WriteUInt64(stream, (ulong)bytes.Length);
        stream.Write(bytes);
        var padding = (-bytes.Length) & 7;
        if (padding != 0)
        {
            stream.Write(new byte[padding]);
        }
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
