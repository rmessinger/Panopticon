using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using Panopticon.FileFormats.Granny;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// BitKnit2Decoder tests: malformed inputs, fuzz robustness, and (Interactive)
/// parity with the recorded pybg3 oracle digests on the sloop_damaged GR2
/// section 0 and its compressed relocation blob.
/// </summary>
public sealed class GrannyBitKnit2DecoderTests
{
    private const string SloopModelPath = @"Art\units\naval\sloop\sloop_damaged.gr2";

    private const string SloopSectionDigest =
        "b3b69b3247c62cba0f402dad5b0a63bd1b7bb5c3742e209288960d56d2afd2f3";

    private const string SloopRelocationDigest =
        "2ccf603ce8761420ff91f6853127f73c36cc8b961048a55e44eb7ae95ccb4951";

    [Fact]
    public void ZeroOutputReturnsEmpty()
    {
        Assert.Empty(BitKnit2Decoder.Decode([], 0));
    }

    [Fact]
    public void NegativeOutputSizeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BitKnit2Decoder.Decode(new byte[4], -1));
    }

    [Fact]
    public void EmptyPayloadWithOutputIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => BitKnit2Decoder.Decode([], 16));
    }

    [Fact]
    public void MissingMarkerWordIsRejected()
    {
        var input = new byte[] { 0xB0, 0x75, 0x00, 0x00 };

        Assert.Throws<InvalidDataException>(() => BitKnit2Decoder.Decode(input, 8));
    }

    [Fact]
    public void PayloadShorterThanTheMarkerIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => BitKnit2Decoder.Decode(new byte[] { 0xB1 }, 4));
    }

    [Fact]
    public void GarbagePayloadsEitherFailContextuallyOrFillTheOutput()
    {
        // Fuzz with fixed seeds: every input either fails with InvalidDataException
        // (the only permitted failure type) or produces exactly the declared
        // output size.
        for (var seed = 1; seed <= 128; seed++)
        {
            var random = new Random(seed);
            var input = new byte[2 + random.Next(0, 128)];
            random.NextBytes(input);
            input[0] = 0xB1;
            input[1] = 0x75;
            var outputSize = random.Next(0, 512);

            byte[] output;
            try
            {
                output = BitKnit2Decoder.Decode(input, outputSize);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            Assert.Equal(outputSize, output.Length);
        }
    }

    [Fact]
    public void TruncatedGarbageAtEveryLengthFailsCleanly()
    {
        // Cut a garbage-with-marker payload at every length: the decoder only
        // ever fails with InvalidDataException.
        var random = new Random(777);
        var payload = new byte[96];
        random.NextBytes(payload);
        payload[0] = 0xB1;
        payload[1] = 0x75;

        for (var cut = 0; cut <= payload.Length; cut++)
        {
            var outcome = Record.Exception(() => BitKnit2Decoder.Decode(payload[..cut], 128));
            if (outcome is null)
            {
                continue;
            }

            Assert.IsType<InvalidDataException>(outcome);
        }
    }

    [Fact]
    public void LargeDeclaredOutputsFromTinyInputsTerminate()
    {
        // A 4 MiB output declared over a marker-only payload must terminate
        // quickly: every command emits at least one byte and the stream end is
        // detected as an InvalidDataException on the next word read.
        var input = new byte[] { 0xB1, 0x75 };

        var outcome = Record.Exception(() => BitKnit2Decoder.Decode(input, 4 * 1024 * 1024));

        Assert.IsType<InvalidDataException>(outcome);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void DecodesSloopSectionZeroToOracleDigest()
    {
        var gr2 = ReadSloopModel();
        var row = ReadSectionRow(gr2, 0);
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(row));
        var dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
        var compressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);
        var uncompressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
        Assert.Equal(110_972, uncompressedSize);

        var decoded = BitKnit2Decoder.Decode(gr2.AsSpan(dataOffset, compressedSize), uncompressedSize);

        Assert.Equal(uncompressedSize, decoded.Length);
        Assert.Equal(SloopSectionDigest, Convert.ToHexString(SHA256.HashData(decoded)).ToLowerInvariant());
        Assert.True(decoded.AsSpan().IndexOf("ArtToolInfo"u8) >= 0, "the decompressed type tree names ArtToolInfo");
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void DecodesSloopRelocationBlobToOracleDigest()
    {
        // Type-4 sections store their relocation table as a u32-size-prefixed
        // BitKnit2 blob decoding to numRelocations * 12 bytes.
        var gr2 = ReadSloopModel();
        var row = ReadSectionRow(gr2, 0);
        var relocationsOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[28..]);
        var relocationCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[32..]);
        Assert.Equal(587, relocationCount);

        var storedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(gr2.AsSpan(relocationsOffset));
        var blob = gr2.AsSpan(relocationsOffset + 4, storedSize);
        var decoded = BitKnit2Decoder.Decode(blob, relocationCount * 12);

        Assert.Equal(relocationCount * 12, decoded.Length);
        Assert.Equal(SloopRelocationDigest, Convert.ToHexString(SHA256.HashData(decoded)).ToLowerInvariant());
    }

    private static byte[] ReadSloopModel()
    {
        using var fileSystem = Aoe3Install.CreateFileSystem();
        var entry = fileSystem.GetFile(SloopModelPath);
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static ReadOnlySpan<byte> ReadSectionRow(byte[] gr2, int sectionIndex)
    {
        var sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(gr2.AsSpan(0x30));
        Assert.True(sectionIndex < sectionCount);
        var table = 0x20 + (int)BinaryPrimitives.ReadUInt32LittleEndian(gr2.AsSpan(0x2C));
        return gr2.AsSpan(table + sectionIndex * 44, 44);
    }
}
