using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using Panopticon.FileFormats.Granny;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Oodle1Decoder tests: synthetic valid streams, malformed inputs, and
/// (Interactive) parity with the recorded blendergranny oracle digest on the
/// falconet GR2 section.
/// </summary>
public sealed class GrannyOodle1DecoderTests
{
    private const string FalconetModelPath = @"Art\units\artillery\falconet\falconet.gr2";

    private const string FalconetSectionDigest =
        "5cf2c4d422b277abf54ca33ad19ccdc0f631a3055376e5ab571e2626169ba29e";

    [Fact]
    public void DecodesSyntheticSingleLiteralStream()
    {
        // Stream parameters: 0x10000 window, 256-entry literal alphabet, one
        // unique literal, no repeat lengths. With bitstream bytes 00 0F 00 00
        // the first length-code decode (escape + new symbol, alphabet 65)
        // returns 0, then the first literal decode (escape + new symbol,
        // alphabet 256) returns 3.
        var input = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(0), (0x10000u << 9) | 256);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(4), 1);
        input[37] = 0x0F;

        var output = Oodle1Decoder.Decode(input, 1, 1, 1);

        Assert.Equal(new byte[] { 3 }, output);
    }

    [Fact]
    public void SkipsEmptyStreamsAndDecodesTheLastStream()
    {
        // Stops (0, 0): streams 0 and 1 are empty, so their headers are unused
        // and the stream-2 header at bytes 24..36 drives the whole decode.
        var input = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(24), (0x10000u << 9) | 256);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(28), 1);
        input[37] = 0x0F;

        var output = Oodle1Decoder.Decode(input, 1, 0, 0);

        Assert.Equal(new byte[] { 3 }, output);
    }

    [Fact]
    public void ZeroOutputReturnsEmptyWithoutTouchingThePayload()
    {
        var output = Oodle1Decoder.Decode([], 0, 0, 0);

        Assert.Empty(output);
    }

    [Fact]
    public void NegativeOutputSizeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Oodle1Decoder.Decode(new byte[40], -1, 0, 0));
    }

    [Fact]
    public void OutOfOrderStopsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Oodle1Decoder.Decode(new byte[40], 16, 12, 8));
    }

    [Fact]
    public void StopsBeyondTheOutputSizeAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Oodle1Decoder.Decode(new byte[40], 16, 4, 17));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Oodle1Decoder.Decode(new byte[40], 16, 17, 17));
    }

    [Fact]
    public void TruncatedHeaderIsRejected()
    {
        Assert.Throws<InvalidDataException>(
            () => Oodle1Decoder.Decode(new byte[35], 4, 0, 0));
    }

    [Fact]
    public void EmptyPayloadWithOutputIsRejected()
    {
        Assert.Throws<InvalidDataException>(
            () => Oodle1Decoder.Decode([], 4, 0, 0));
    }

    [Fact]
    public void ZeroLiteralAlphabetIsRejected()
    {
        // All-zero stream header declares window 0 and an empty literal
        // alphabet, which cannot decode anything.
        Assert.Throws<InvalidDataException>(
            () => Oodle1Decoder.Decode(new byte[40], 1, 1, 1));
    }

    [Fact]
    public void GarbagePayloadsEitherFailContextuallyOrFillTheOutput()
    {
        // Fuzz with fixed seeds: every input either fails with InvalidDataException
        // (the only permitted failure type: no IndexOutOfRange, DivideByZero, or
        // arithmetic faults escape) or produces exactly the declared output size.
        for (var seed = 1; seed <= 128; seed++)
        {
            var random = new Random(seed);
            var input = new byte[36 + random.Next(0, 96)];
            random.NextBytes(input);
            var outputSize = random.Next(0, 512);
            var first16 = (uint)random.Next(0, outputSize + 1);
            var first8 = (uint)random.Next((int)first16, outputSize + 1);

            byte[] output;
            try
            {
                output = Oodle1Decoder.Decode(input, outputSize, first16, first8);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            Assert.Equal(outputSize, output.Length);
        }
    }

    [Fact]
    public void TruncatedPayloadsAtEveryLengthFailCleanly()
    {
        // Cut the synthetic stream at every length: each cut either still
        // decodes the single literal, or fails with InvalidDataException.
        var input = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(0), (0x10000u << 9) | 256);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(4), 1);
        input[37] = 0x0F;

        for (var cut = 0; cut <= input.Length; cut++)
        {
            var outcome = Record.Exception(() => Oodle1Decoder.Decode(input[..cut], 1, 1, 1));
            if (outcome is null)
            {
                continue;
            }

            Assert.IsType<InvalidDataException>(outcome);
        }
    }

    [Fact]
    public void DeclaredWindowAboveTheHardCapIsRejected()
    {
        // The codec's window is hard-capped at 256 KiB by specification; all
        // corpus sections declare 0x3FFFF. A larger window would also inflate
        // per-stream coder allocations.
        var input = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(0), (0x50000u << 9) | 256);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(4), 1);

        Assert.Throws<InvalidDataException>(() => Oodle1Decoder.Decode(input, 1, 1, 1));
    }

    [Fact]
    public void OversizedLiteralAlphabetGarbageFailsCleanly()
    {
        // A declared alphabet above the byte range cannot produce valid
        // literals; garbage under it must still only fail contextually.
        for (var seed = 1; seed <= 32; seed++)
        {
            var random = new Random(1_000 + seed);
            var input = new byte[40];
            BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(0), (0x10000u << 9) | 511);
            random.NextBytes(input.AsSpan(36));

            var outcome = Record.Exception(() => Oodle1Decoder.Decode(input, 64, 64, 64));
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
        // A 4 MiB output declared over a minimal payload must terminate
        // quickly: every symbol emits at least one byte and reads past the
        // padding margin fail immediately.
        var input = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(0), (0x10000u << 9) | 256);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(4), 4);

        var outcome = Record.Exception(() => Oodle1Decoder.Decode(input, 4 * 1024 * 1024, 4 * 1024 * 1024, 4 * 1024 * 1024));

        Assert.IsType<InvalidDataException>(outcome);
    }

    [Fact]
    public void TruncatedPayloadsFailDeterministically()
    {
        // The zero-padding contract makes the decoder a pure function of
        // input: identical truncated payloads always reach the identical
        // outcome. Here the zero-filled tail decodes a repeat that reaches
        // before the stream start, so the bounded backreference guard fires.
        var input = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(0), (0x10000u << 9) | 256);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(4), 4);
        input[37] = 0x0F;

        var first = Record.Exception(() => Oodle1Decoder.Decode(input, 64, 64, 64));
        var second = Record.Exception(() => Oodle1Decoder.Decode(input, 64, 64, 64));

        Assert.IsType<InvalidDataException>(first);
        Assert.Equal(first!.Message, second!.Message);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void DecodesFalconetSectionToOracleDigest()
    {
        // Parity with the blendergranny (MIT) Python decoder recorded in
        // ~/log/c10d during WI-C10d-02; the digest is a hash fixture, not an asset.
        var gr2 = ReadGr2Model(FalconetModelPath);

        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(gr2.AsSpan(0x20)));
        var sectionsOffset = BinaryPrimitives.ReadUInt32LittleEndian(gr2.AsSpan(0x2C));
        var sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(gr2.AsSpan(0x30));
        Assert.Equal(1u, sectionCount);

        var row = gr2.AsSpan(0x20 + (int)sectionsOffset, 44);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(row));
        var dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
        var compressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);
        var uncompressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
        var first16Bit = BinaryPrimitives.ReadUInt32LittleEndian(row[20..]);
        var first8Bit = BinaryPrimitives.ReadUInt32LittleEndian(row[24..]);
        Assert.Equal(0x39790, uncompressedSize);
        Assert.Equal(0x7A80, (int)first16Bit);
        Assert.Equal(0xDB30, (int)first8Bit);

        var decoded = Oodle1Decoder.Decode(
            gr2.AsSpan(dataOffset, compressedSize), uncompressedSize, first16Bit, first8Bit);

        Assert.Equal(uncompressedSize, decoded.Length);
        Assert.Equal(FalconetSectionDigest, Convert.ToHexString(SHA256.HashData(decoded)).ToLowerInvariant());
        Assert.True(decoded.AsSpan().IndexOf("ArtToolInfo"u8) >= 0, "the decompressed type tree names ArtToolInfo");
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void DecodesHajdukSectionWithTheDecayBoundaryCase()
    {
        // hajduk's stream 0 crosses the weight-sum window where Granny keys
        // decay to the renormalization threshold rather than the current
        // weight (validated against blendergranny; see Oodle1SymbolCoder).
        var gr2 = ReadGr2Model(@"Art\units\spc\outlaws\hajduk.gr2");
        var row = ReadSectionRow(gr2, 0);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(row));
        var dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
        var compressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);
        var uncompressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[12..]);
        var first16Bit = BinaryPrimitives.ReadUInt32LittleEndian(row[20..]);
        var first8Bit = BinaryPrimitives.ReadUInt32LittleEndian(row[24..]);
        Assert.Equal(83_344, uncompressedSize);

        var decoded = Oodle1Decoder.Decode(
            gr2.AsSpan(dataOffset, compressedSize), uncompressedSize, first16Bit, first8Bit);

        Assert.Equal(
            "68af9435e8b02c610b07d3364b4522058a6d677901f94c1fa16d02868b812632",
            Convert.ToHexString(SHA256.HashData(decoded)).ToLowerInvariant());
    }

    private static byte[] ReadGr2Model(string path)
    {
        using var fileSystem = Aoe3Install.CreateFileSystem();
        var entry = fileSystem.GetFile(path);
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
