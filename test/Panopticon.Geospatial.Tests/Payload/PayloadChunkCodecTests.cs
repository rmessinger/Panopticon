#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Panopticon.Geospatial.Tests.Payload;

public class PayloadChunkCodecTests
{
    private const int StoredAxis = ChunkLayout.StoredPointsPerChunkAxis;

    private const int CoreAxis = ChunkLayout.PointsPerChunkAxis;

    private const int TerrainBytes = 131 * 131 * 4;

    private const int CoreBytes = 129 * 129;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void TerrainRoundTripPreservesEverySampleTheHashAndTheRange()
    {
        var chunk = new TerrainChunk(3, 2, TerrainFromLattice(3, 2));
        var encoded = PayloadChunkCodec.EncodeTerrain(chunk);

        Assert.Equal(TerrainBytes, encoded.Length);
        Assert.Equal(chunk.ContentSha256Hex, Sha256Hex(encoded));

        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeTerrain(3, 2, encoded, diagnostics);

        Assert.NotNull(decoded);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(3, decoded!.ChunkX);
        Assert.Equal(2, decoded.ChunkY);
        Assert.Equal(chunk.ContentSha256Hex, decoded.ContentSha256Hex);
        Assert.Equal(chunk.MinHeightGameUnits, decoded.MinHeightGameUnits);
        Assert.Equal(chunk.MaxHeightGameUnits, decoded.MaxHeightGameUnits);

        for (var sy = 0; sy < StoredAxis; sy++)
        {
            for (var sx = 0; sx < StoredAxis; sx++)
            {
                Assert.Equal(chunk.GetHeight(sx, sy), decoded.GetHeight(sx, sy));
            }
        }
    }

    [Fact]
    public void TerrainRoundTripPreservesNegativeZeroBits()
    {
        var heights = TerrainFromLattice(0, 0);
        heights[(17 * StoredAxis) + 42] = -0f;

        var chunk = new TerrainChunk(0, 0, heights);

        Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(chunk.GetHeight(42, 17)));

        var decoded = PayloadChunkCodec.DecodeTerrain(0, 0, PayloadChunkCodec.EncodeTerrain(chunk));

        Assert.NotNull(decoded);
        Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(decoded!.GetHeight(42, 17)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TerrainBytes - 1)]
    [InlineData(TerrainBytes + 1)]
    public void DecodeTerrainRejectsWrongLengths(int length)
    {
        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeTerrain(0, 0, new byte[length], diagnostics);

        Assert.Null(decoded);
        Assert.Equal(PayloadChunkDiagnosticCodes.ChunkLengthInvalid, SingleCode(diagnostics));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void DecodeTerrainRejectsNonFiniteHeights(float poison)
    {
        var encoded = PayloadChunkCodec.EncodeTerrain(new TerrainChunk(0, 0, TerrainFromLattice(0, 0)));
        BinaryPrimitives.WriteSingleLittleEndian(encoded.AsSpan((64 * StoredAxis + 65) * 4), poison);

        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeTerrain(0, 0, encoded, diagnostics);

        Assert.Null(decoded);
        Assert.Equal(PayloadChunkDiagnosticCodes.TerrainHeightNotFinite, SingleCode(diagnostics));
    }

    [Fact]
    public void DecodeTerrainChecksCancellationBeforeDecoding()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => PayloadChunkCodec.DecodeTerrain(0, 0, new byte[TerrainBytes], cancellationToken: source.Token));
    }

    [Fact]
    public void MaterialRoundTripPreservesIndicesAndTheHash()
    {
        var palette = new MaterialPalette(["grass", "dirt", "rock"]);
        var chunk = new MaterialChunk(1, 4, palette, MaterialFromLattice(1, 4, palette.Count));
        var encoded = PayloadChunkCodec.EncodeMaterial(chunk);

        Assert.Equal(CoreBytes, encoded.Length);
        Assert.Equal(chunk.ContentSha256Hex, Sha256Hex(encoded));

        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeMaterial(1, 4, encoded, palette, diagnostics);

        Assert.NotNull(decoded);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(chunk.ContentSha256Hex, decoded!.ContentSha256Hex);
        Assert.Same(palette, decoded.Palette);

        for (var y = 0; y < CoreAxis; y++)
        {
            for (var x = 0; x < CoreAxis; x++)
            {
                Assert.Equal(chunk.GetIndex(x, y), decoded.GetIndex(x, y));
            }
        }
    }

    [Fact]
    public void DecodeMaterialAcceptsTheLargestPaletteAndRejectsItsFirstOutOfRangeByte()
    {
        var names = Enumerable.Range(0, MaterialPalette.MaxEntries).Select(i => $"material-{i}").ToArray();
        var palette = new MaterialPalette(names);
        var indices = MaterialFromLattice(0, 0, palette.Count);

        Assert.Equal(254, indices.Max());
        Assert.NotNull(PayloadChunkCodec.DecodeMaterial(0, 0, indices, palette));

        indices[(33 * CoreAxis) + 70] = 255;

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeMaterial(0, 0, indices, palette, diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.MaterialIndexOutOfPalette, SingleCode(diagnostics));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CoreBytes - 1)]
    [InlineData(CoreBytes + 1)]
    public void DecodeMaterialRejectsWrongLengths(int length)
    {
        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeMaterial(0, 0, new byte[length], new MaterialPalette(["grass"]), diagnostics);

        Assert.Null(decoded);
        Assert.Equal(PayloadChunkDiagnosticCodes.ChunkLengthInvalid, SingleCode(diagnostics));
    }

    [Fact]
    public void DecodeMaterialRejectsIndicesOutsideThePalette()
    {
        var palette = new MaterialPalette(["grass", "dirt", "rock"]);
        var indices = MaterialFromLattice(0, 0, palette.Count);
        indices[(100 * CoreAxis) + 3] = 3;

        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeMaterial(0, 0, indices, palette, diagnostics);

        Assert.Null(decoded);
        Assert.Equal(PayloadChunkDiagnosticCodes.MaterialIndexOutOfPalette, SingleCode(diagnostics));
    }

    [Fact]
    public void DecodeMaterialChecksCancellationBeforeDecoding()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => PayloadChunkCodec.DecodeMaterial(0, 0, new byte[CoreBytes], new MaterialPalette(["grass"]), cancellationToken: source.Token));
    }

    [Fact]
    public void MaskRoundTripPreservesFlagsAndTheHash()
    {
        var chunk = new TerrainMaskChunk(2, 2, MaskFromLattice(2, 2));
        var encoded = PayloadChunkCodec.EncodeMask(chunk);

        Assert.Equal(CoreBytes, encoded.Length);
        Assert.Equal(chunk.ContentSha256Hex, Sha256Hex(encoded));

        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeMask(2, 2, encoded, diagnostics);

        Assert.NotNull(decoded);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(chunk.ContentSha256Hex, decoded!.ContentSha256Hex);

        for (var y = 0; y < CoreAxis; y++)
        {
            for (var x = 0; x < CoreAxis; x++)
            {
                Assert.Equal(chunk.GetFlags(x, y), decoded.GetFlags(x, y));
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CoreBytes - 1)]
    [InlineData(CoreBytes + 1)]
    public void DecodeMaskRejectsWrongLengths(int length)
    {
        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeMask(0, 0, new byte[length], diagnostics);

        Assert.Null(decoded);
        Assert.Equal(PayloadChunkDiagnosticCodes.ChunkLengthInvalid, SingleCode(diagnostics));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0x80)]
    [InlineData(0xFF)]
    public void DecodeMaskRejectsUndefinedBits(byte poison)
    {
        var flags = MaskFromLattice(0, 0);
        flags[(51 * CoreAxis) + 12] = poison;

        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeMask(0, 0, flags, diagnostics);

        Assert.Null(decoded);
        Assert.Equal(PayloadChunkDiagnosticCodes.MaskFlagsUndefined, SingleCode(diagnostics));
    }

    [Fact]
    public void DecodeMaskChecksCancellationBeforeDecoding()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => PayloadChunkCodec.DecodeMask(0, 0, new byte[CoreBytes], cancellationToken: source.Token));
    }

    [Fact]
    public void TerrainSeamsMatchAcrossTheFullOverlapInBothDirections()
    {
        var west = new TerrainChunk(0, 0, TerrainFromLattice(0, 0));
        var east = new TerrainChunk(1, 0, TerrainFromLattice(1, 0));
        var south = new TerrainChunk(0, 0, TerrainFromLattice(0, 0));
        var north = new TerrainChunk(0, 1, TerrainFromLattice(0, 1));

        var diagnostics = new GeoDiagnosticBag();

        Assert.True(PayloadChunkCodec.ValidateTerrainHorizontalSeam(west, east, diagnostics));
        Assert.True(PayloadChunkCodec.ValidateTerrainVerticalSeam(south, north, diagnostics));
        Assert.Empty(diagnostics.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TerrainHorizontalSeamRejectsAMismatchInEachOverlapSample(int sample)
    {
        var eastHeights = TerrainFromLattice(1, 0);
        eastHeights[(17 * StoredAxis) + sample] += 1f;

        var diagnostics = new GeoDiagnosticBag();
        var valid = PayloadChunkCodec.ValidateTerrainHorizontalSeam(
            new TerrainChunk(0, 0, TerrainFromLattice(0, 0)),
            new TerrainChunk(1, 0, eastHeights),
            diagnostics);

        Assert.False(valid);
        Assert.Equal(PayloadChunkDiagnosticCodes.ChunkSeamMismatch, SingleCode(diagnostics));
        Assert.Contains($"sample {sample}", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TerrainVerticalSeamRejectsAMismatchInEachOverlapRow(int sample)
    {
        var northHeights = TerrainFromLattice(0, 1);
        northHeights[(sample * StoredAxis) + 17] += 1f;

        var diagnostics = new GeoDiagnosticBag();
        var valid = PayloadChunkCodec.ValidateTerrainVerticalSeam(
            new TerrainChunk(0, 0, TerrainFromLattice(0, 0)),
            new TerrainChunk(0, 1, northHeights),
            diagnostics);

        Assert.False(valid);
        Assert.Equal(PayloadChunkDiagnosticCodes.ChunkSeamMismatch, SingleCode(diagnostics));
        Assert.Contains($"sample {sample}", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TerrainSeamComparesBitsSoNegativeZeroMismatchesPositiveZero()
    {
        var westHeights = new float[StoredAxis * StoredAxis];
        Array.Fill(westHeights, 1f);
        var eastHeights = new float[StoredAxis * StoredAxis];
        Array.Fill(eastHeights, 1f);

        for (var sample = 0; sample < 3; sample++)
        {
            westHeights[(5 * StoredAxis) + 128 + sample] = 0f;
            eastHeights[(5 * StoredAxis) + sample] = -0f;
        }

        var west = new TerrainChunk(0, 0, westHeights);
        var east = new TerrainChunk(1, 0, eastHeights);

        // Float equality alone would accept the seam; the codec must compare bits.
        Assert.Equal(west.GetHeight(128, 5), east.GetHeight(0, 5));

        var diagnostics = new GeoDiagnosticBag();

        Assert.False(PayloadChunkCodec.ValidateTerrainHorizontalSeam(west, east, diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.ChunkSeamMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void MaterialSeamsMatchAndRejectASingleEdgeMismatch()
    {
        var palette = new MaterialPalette(["grass", "dirt", "rock"]);

        Assert.True(PayloadChunkCodec.ValidateMaterialHorizontalSeam(
            new MaterialChunk(0, 0, palette, MaterialFromLattice(0, 0, palette.Count)),
            new MaterialChunk(1, 0, palette, MaterialFromLattice(1, 0, palette.Count))));
        Assert.True(PayloadChunkCodec.ValidateMaterialVerticalSeam(
            new MaterialChunk(0, 0, palette, MaterialFromLattice(0, 0, palette.Count)),
            new MaterialChunk(0, 1, palette, MaterialFromLattice(0, 1, palette.Count))));

        var eastIndices = MaterialFromLattice(1, 0, palette.Count);
        eastIndices[(9 * CoreAxis) + 0] = (byte)((eastIndices[(9 * CoreAxis) + 0] + 1) % palette.Count);

        var diagnostics = new GeoDiagnosticBag();

        Assert.False(PayloadChunkCodec.ValidateMaterialHorizontalSeam(
            new MaterialChunk(0, 0, palette, MaterialFromLattice(0, 0, palette.Count)),
            new MaterialChunk(1, 0, palette, eastIndices),
            diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.ChunkSeamMismatch, SingleCode(diagnostics));

        var northIndices = MaterialFromLattice(0, 1, palette.Count);
        northIndices[(0 * CoreAxis) + 9] = (byte)((northIndices[(0 * CoreAxis) + 9] + 1) % palette.Count);

        Assert.False(PayloadChunkCodec.ValidateMaterialVerticalSeam(
            new MaterialChunk(0, 0, palette, MaterialFromLattice(0, 0, palette.Count)),
            new MaterialChunk(0, 1, palette, northIndices),
            diagnostics));
        Assert.Equal(2, diagnostics.Items.Count);
    }

    [Fact]
    public void MaskSeamsMatchAndRejectASingleEdgeMismatch()
    {
        Assert.True(PayloadChunkCodec.ValidateMaskHorizontalSeam(
            new TerrainMaskChunk(0, 0, MaskFromLattice(0, 0)),
            new TerrainMaskChunk(1, 0, MaskFromLattice(1, 0))));
        Assert.True(PayloadChunkCodec.ValidateMaskVerticalSeam(
            new TerrainMaskChunk(0, 0, MaskFromLattice(0, 0)),
            new TerrainMaskChunk(0, 1, MaskFromLattice(0, 1))));

        var eastFlags = MaskFromLattice(1, 0);
        eastFlags[(21 * CoreAxis) + 0] ^= 1;

        var diagnostics = new GeoDiagnosticBag();

        Assert.False(PayloadChunkCodec.ValidateMaskHorizontalSeam(
            new TerrainMaskChunk(0, 0, MaskFromLattice(0, 0)),
            new TerrainMaskChunk(1, 0, eastFlags),
            diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.ChunkSeamMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void SeamValidationRejectsNonAdjacentChunks()
    {
        var west = new TerrainChunk(0, 0, TerrainFromLattice(0, 0));
        var far = new TerrainChunk(2, 0, TerrainFromLattice(2, 0));
        var wrongRow = new TerrainChunk(1, 1, TerrainFromLattice(1, 1));
        var south = new TerrainChunk(0, 0, TerrainFromLattice(0, 0));
        var diagonal = new TerrainChunk(1, 1, TerrainFromLattice(1, 1));

        Assert.Throws<ArgumentException>(() => PayloadChunkCodec.ValidateTerrainHorizontalSeam(west, far));
        Assert.Throws<ArgumentException>(() => PayloadChunkCodec.ValidateTerrainHorizontalSeam(west, wrongRow));
        Assert.Throws<ArgumentException>(() => PayloadChunkCodec.ValidateTerrainVerticalSeam(south, diagonal));
    }

    [Fact]
    public void ImageryDecodeRoundTripsRealEncoderOutputWithoutReencoding()
    {
        const int pixels = 32;
        var rgba = GradientRgba(pixels);
        var png = EncodeRealPng(pixels, rgba);
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics);

        Assert.NotNull(decoded);
        Assert.Empty(diagnostics.Items);
        Assert.Same(chunk, decoded!.Chunk);
        Assert.Equal(pixels, decoded.PixelsPerAxis);
        Assert.Equal(rgba, decoded.Rgba.ToArray());
        Assert.Equal(png, decoded.Chunk.Png.ToArray());
    }

    [Fact]
    public void ImageryDecodeAcceptsTheBoundedAncillaryChunksOfTheFixedEncoder()
    {
        const int pixels = 16;
        var rgba = GradientRgba(pixels);
        var chunks = SplitChunks(EncodeRealPng(pixels, rgba));
        var rebuilt = new PngBuilder()
            .Add("IHDR", chunks["IHDR"])
            .Add("gAMA", [0, 1, 0x86, 0xA0])
            .Add("sRGB", [0])
            .Add("IDAT", chunks["IDAT"])
            .Add("pHYs", [0, 0, 0x0B, 0x13, 0, 0, 0x0B, 0x13, 1])
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, rebuilt);

        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics);

        Assert.NotNull(decoded);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(rgba, decoded!.Rgba.ToArray());
    }

    [Fact]
    public void RealEncoderOutputStaysInsideTheAllowedChunkSet()
    {
        const int pixels = 16;
        var png = EncodeRealPng(pixels, GradientRgba(pixels));
        var allowed = new HashSet<string>(["IHDR", "IDAT", "IEND", "pHYs", "gAMA", "sRGB"], StringComparer.Ordinal);

        foreach (var type in SplitChunks(png).Keys)
        {
            Assert.Contains(type, allowed);
        }

        Assert.NotNull(PayloadChunkCodec.DecodeImagery(new ImageryChunk(0, 0, pixels, png), pixels));
    }

    [Theory]
    [InlineData("tEXt")]
    [InlineData("eXIf")]
    [InlineData("cHRM")]
    [InlineData("bKGD")]
    [InlineData("tIME")]
    public void ImageryDecodeRejectsMetadataChunksOutsideTheFixedEncoderSet(string type)
    {
        var chunk = new ImageryChunk(0, 0, 16, new PngBuilder()
            .Add("IHDR", IhdrData(16, 16))
            .Add(type, new byte[4])
            .Add("IDAT", [1])
            .Add("IEND", [])
            .ToArray());

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, 16, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngStructureInvalid, SingleCode(diagnostics));
    }

    [Theory]
    [InlineData("iCCP")]
    [InlineData("zTXt")]
    [InlineData("iTXt")]
    public void ImageryDecodeRejectsCompressedAncillaryBombsBeforeAnyDecode(string type)
    {
        // 64 KiB of compressed metadata could inflate without bound inside ImageSharp;
        // the allowlist must reject it during preflight, before any decode allocation.
        var chunk = new ImageryChunk(0, 0, 16, new PngBuilder()
            .Add("IHDR", IhdrData(16, 16))
            .Add(type, new byte[64 * 1024])
            .Add("IDAT", [1])
            .Add("IEND", [])
            .ToArray());

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, 16, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngStructureInvalid, SingleCode(diagnostics));
        Assert.Contains("compressed metadata", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pHYs", 8)]
    [InlineData("gAMA", 5)]
    [InlineData("sRGB", 2)]
    public void ImageryDecodeRejectsAllowedAncillaryChunksWithUnexpectedLengths(string type, int length)
    {
        var chunk = new ImageryChunk(0, 0, 16, new PngBuilder()
            .Add("IHDR", IhdrData(16, 16))
            .Add(type, new byte[length])
            .Add("IDAT", [1])
            .Add("IEND", [])
            .ToArray());

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, 16, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngStructureInvalid, SingleCode(diagnostics));
    }

    [Fact]
    public void ImageryDecodeRejectsADeclaredPixelSizeThatDiffersFromTheExpectation()
    {
        var chunk = new ImageryChunk(0, 0, 16, EncodeRealPng(16, GradientRgba(16)));

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, 17, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngFormatMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void ImageryDecodeRejectsAHugeIhdrBeforeAnyDecodeAllocation()
    {
        var crafted = CraftedPng(65535, 65535);
        var chunk = new ImageryChunk(0, 0, 32, crafted);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, 32, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngFormatMismatch, SingleCode(diagnostics));
    }

    [Theory]
    [InlineData(16, 6)]
    [InlineData(2, 6)]
    [InlineData(8, 3)]
    [InlineData(8, 2)]
    public void ImageryDecodeRejectsIhdrFieldsOutsideTheFixedRgba8Format(int bitDepth, int colorType)
    {
        var pixels = 16;
        var header = IhdrData(pixels, pixels);
        header[8] = (byte)bitDepth;
        header[9] = (byte)colorType;

        var chunk = new ImageryChunk(0, 0, pixels, new PngBuilder().Add("IHDR", header).Add("IDAT", [1]).Add("IEND", []).ToArray());

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngFormatMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void ImageryDecodeRejectsInterlacedImages()
    {
        var pixels = 16;
        var header = IhdrData(pixels, pixels);
        header[12] = 1;

        var chunk = new ImageryChunk(0, 0, pixels, new PngBuilder().Add("IHDR", header).Add("IDAT", [1]).Add("IEND", []).ToArray());

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngFormatMismatch, SingleCode(diagnostics));
    }

    [Theory]
    [InlineData("header-short")]
    [InlineData("length-overrun")]
    [InlineData("type-not-text")]
    [InlineData("trailing-garbage")]
    [InlineData("missing-iend")]
    [InlineData("first-not-ihdr")]
    [InlineData("palette-chunk")]
    [InlineData("second-ihdr")]
    [InlineData("iend-with-data")]
    [InlineData("split-idat")]
    [InlineData("unknown-ancillary")]
    public void ImageryDecodeRejectsInvalidChunkStructure(string mode)
    {
        var pixels = 16;
        var header = IhdrData(pixels, pixels);
        byte[] png = mode switch
        {
            "header-short" => PngSignature.Concat("IHDR"u8.ToArray()).ToArray(),
            "length-overrun" => PngBuilder.Create().AddRaw(0x7FFF_FFFF, "IDAT"u8.ToArray(), [1]).ToArray(),
            "type-not-text" => PngBuilder.Create().AddRaw(1, "\0IDT"u8.ToArray(), [1]).Add("IEND", []).ToArray(),
            "trailing-garbage" => Concat(
                new PngBuilder().Add("IHDR", header).Add("IDAT", [1]).Add("IEND", []).ToArray(), [0xAB]),
            "missing-iend" => new PngBuilder().Add("IHDR", header).Add("IDAT", [1]).ToArray(),
            "first-not-ihdr" => new PngBuilder().Add("tEXt", "k\0v"u8.ToArray()).Add("IHDR", header).Add("IDAT", [1]).Add("IEND", []).ToArray(),
            "palette-chunk" => new PngBuilder().Add("IHDR", header).Add("PLTE", new byte[3]).Add("IDAT", [1]).Add("IEND", []).ToArray(),
            "second-ihdr" => new PngBuilder().Add("IHDR", header).Add("IHDR", header).Add("IDAT", [1]).Add("IEND", []).ToArray(),
            "iend-with-data" => new PngBuilder().Add("IHDR", header).Add("IDAT", [1]).Add("IEND", [1]).ToArray(),
            "split-idat" => new PngBuilder().Add("IHDR", header).Add("IDAT", [1]).Add("pHYs", new byte[9]).Add("IDAT", [2]).Add("IEND", []).ToArray(),
            "unknown-ancillary" => new PngBuilder().Add("IHDR", header).Add("cuSt", [1]).Add("IDAT", [1]).Add("IEND", []).ToArray(),
            _ => throw new ArgumentException($"Unknown mode {mode}.", nameof(mode)),
        };

        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngStructureInvalid, SingleCode(diagnostics));
    }

    [Theory]
    [InlineData("acTL")]
    [InlineData("fcTL")]
    [InlineData("fdAT")]
    public void ImageryDecodeRejectsAnimationChunks(string type)
    {
        var pixels = 16;
        var png = new PngBuilder()
            .Add("IHDR", IhdrData(pixels, pixels))
            .Add(type, new byte[8])
            .Add("IDAT", [1])
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngStructureInvalid, SingleCode(diagnostics));
        Assert.Contains("animation", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageryDecodeVerifiesEveryChunkCrc()
    {
        var pixels = 16;
        var valid = new PngBuilder().Add("IHDR", IhdrData(pixels, pixels)).Add("IDAT", [1, 2, 3]).Add("IEND", []).ToArray();

        // Signature (8) + IHDR chunk (12 + 13) + IDAT header (8) puts the IDAT data at
        // offset 41; corrupt its middle byte, leaving the stored CRC stale.
        valid[42] ^= 0xFF;

        var chunk = new ImageryChunk(0, 0, pixels, valid);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngChecksumInvalid, SingleCode(diagnostics));
    }

    [Fact]
    public void ImageryDecodeRejectsMoreChunksThanTheBound()
    {
        var pixels = 16;
        var builder = new PngBuilder().Add("IHDR", IhdrData(pixels, pixels));

        for (var i = 0; i < 4100; i++)
        {
            builder.Add("pHYs", new byte[9]);
        }

        var chunk = new ImageryChunk(0, 0, pixels, builder.ToArray());

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngBudgetExceeded, SingleCode(diagnostics));
    }

    [Theory]
    [InlineData(40000, "budget")]
    [InlineData(32768, "allocatable array")]
    public void ImageryDecodeRejectsUndecodableBudgetsBeforeAnyAllocation(int pixels, string expectedMessagePart)
    {
        // 40000 x 40000 x 4 exceeds MaxRasterDecodedBytes; 32768^2 x 4 equals it but
        // exceeds the largest byte[]; both must fail on arithmetic alone.
        var crafted = CraftedPng(pixels, pixels);
        var chunk = new ImageryChunk(0, 0, pixels, crafted);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngBudgetExceeded, SingleCode(diagnostics));
        Assert.Contains(expectedMessagePart, diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageryDecodeAcceptsACustomBudgetOfLongMaxValue()
    {
        const int pixels = 16;
        var rgba = GradientRgba(pixels);
        var chunk = new ImageryChunk(0, 0, pixels, EncodeRealPng(pixels, rgba));

        var diagnostics = new GeoDiagnosticBag();
        var decoded = PayloadChunkCodec.DecodeImagery(
            chunk,
            pixels,
            new GeospatialImportLimits { MaxRasterDecodedBytes = long.MaxValue },
            diagnostics);

        Assert.NotNull(decoded);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(rgba, decoded!.Rgba.ToArray());
    }

    [Fact]
    public void ImageryDecodeRejectsADecodedBufferBeyondTheConfiguredBudget()
    {
        // 16 x 16 x 4 = 1024 bytes; one byte below must fail without any allocation.
        const int pixels = 16;
        var chunk = new ImageryChunk(0, 0, pixels, EncodeRealPng(pixels, GradientRgba(pixels)));

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(
            chunk,
            pixels,
            new GeospatialImportLimits { MaxRasterDecodedBytes = 1023 },
            diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngBudgetExceeded, SingleCode(diagnostics));
        Assert.Contains("budget", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageryDecodeRejectsACorruptImageStreamBehindAValidStructure()
    {
        var pixels = 16;
        var png = new PngBuilder()
            .Add("IHDR", IhdrData(pixels, pixels))
            .Add("IDAT", [0xDE, 0xAD, 0xBE, 0xEF])
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngDecodeFailed, SingleCode(diagnostics));
    }

    [Fact]
    public void ImageryDecodeChecksCancellationBeforeDecoding()
    {
        var chunk = new ImageryChunk(0, 0, 16, CraftedPng(16, 16));

        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => PayloadChunkCodec.DecodeImagery(chunk, 16, cancellationToken: source.Token));
    }

    [Fact]
    public void ImageryDecodeChecksCancellationBeforeInflating()
    {
        // A fully decodable encoder-produced PNG: only the cancelled token can fail this
        // call, and it must fail before any inflation work.
        var chunk = new ImageryChunk(0, 0, 16, EncodeRealPng(16, GradientRgba(16)));

        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => PayloadChunkCodec.DecodeImagery(chunk, 16, cancellationToken: source.Token));
    }

    [Fact]
    public void ImageryDecodeRejectsAZlibStreamThatEndsAfterOneScanline()
    {
        // CRC-valid framing with a complete zlib encoding of only the first scanline of
        // 16: the decoder alone would return a 16 x 16 image with unfilled rows.
        const int pixels = 16;
        var png = new PngBuilder()
            .Add("IHDR", IhdrData(pixels, pixels))
            .Add("IDAT", ZlibCompress(RawScanlines(pixels, rowCount: 1)))
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngDecodeFailed, SingleCode(diagnostics));
        Assert.Contains("not a complete zlib encoding", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageryDecodeRejectsAReservedDeflateBlockAsARecoverableFailure()
    {
        // A reserved deflate block makes the inflater throw InvalidDataException, which
        // must surface as a recoverable diagnostic, never as an escaping exception.
        const int pixels = 16;
        var png = new PngBuilder()
            .Add("IHDR", IhdrData(pixels, pixels))
            .Add("IDAT", [0x78, 0x9C, 0x07, 0x00, 0x00, 0x00, 0x00])
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngDecodeFailed, SingleCode(diagnostics));
    }

    [Fact]
    public void ImageryDecodeRejectsExcessInflatedDataWithoutInflatingIt()
    {
        // A complete valid scanline set followed by 8 MiB of extra inflated data: the
        // single probe byte must reject the stream before the excess is inflated.
        const int pixels = 16;
        var png = new PngBuilder()
            .Add("IHDR", IhdrData(pixels, pixels))
            .Add("IDAT", ZlibCompress(RawScanlines(pixels, rowCount: pixels, extraBytes: 8 * 1024 * 1024)))
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngDecodeFailed, SingleCode(diagnostics));
        Assert.Contains("more raw image data", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void ImageryDecodeRejectsATruncatedZlibTrailer(int trimmedBytes)
    {
        // .NET's ZLibStream returns every inflated scanline without touching the Adler-32
        // trailer, so removing up to five trailing bytes still yields all 16 rows; only
        // the explicit trailer comparison rejects the stream.
        const int pixels = 16;
        var compressed = ZlibCompress(RawScanlines(pixels, rowCount: pixels));
        var truncated = compressed[..^trimmedBytes];
        var png = new PngBuilder()
            .Add("IHDR", IhdrData(pixels, pixels))
            .Add("IDAT", truncated)
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngDecodeFailed, SingleCode(diagnostics));
        Assert.Contains("zlib", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageryDecodeRejectsABadAdler32Trailer()
    {
        // Complete deflate data for all rows with a present but wrong checksum trailer.
        const int pixels = 16;
        var compressed = ZlibCompress(RawScanlines(pixels, rowCount: pixels));
        compressed[^4] ^= 0xFF;
        compressed[^1] ^= 0x0F;
        var png = new PngBuilder()
            .Add("IHDR", IhdrData(pixels, pixels))
            .Add("IDAT", compressed)
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngDecodeFailed, SingleCode(diagnostics));
        Assert.Contains("zlib", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x79, 0x18)] // CM 9: not deflate.
    [InlineData(0x88, 0x0B)] // CINFO 8: window beyond 32 KiB.
    [InlineData(0x78, 0x9D)] // FCHECK not divisible by 31.
    [InlineData(0x78, 0xBB)] // FDICT set: preset dictionary required.
    public void ImageryDecodeRejectsInvalidZlibHeaders(byte cmf, byte flg)
    {
        const int pixels = 16;
        var compressed = ZlibCompress(RawScanlines(pixels, rowCount: pixels));
        compressed[0] = cmf;
        compressed[1] = flg;
        var png = new PngBuilder()
            .Add("IHDR", IhdrData(pixels, pixels))
            .Add("IDAT", compressed)
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngDecodeFailed, SingleCode(diagnostics));
        Assert.Contains("zlib header", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void ImageryDecodeRejectsIdatDataShorterThanACompleteZlibStream(int idatBytes)
    {
        const int pixels = 16;
        var png = new PngBuilder()
            .Add("IHDR", IhdrData(pixels, pixels))
            .Add("IDAT", new byte[idatBytes])
            .Add("IEND", [])
            .ToArray();
        var chunk = new ImageryChunk(0, 0, pixels, png);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadChunkCodec.DecodeImagery(chunk, pixels, diagnostics: diagnostics));
        Assert.Equal(PayloadChunkDiagnosticCodes.PngDecodeFailed, SingleCode(diagnostics));
        Assert.Contains("at least 6", diagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodedImageryCopiesTheExternalBuffer()
    {
        var chunk = new ImageryChunk(0, 0, 2, CraftedPng(2, 2));
        var rgba = new byte[16];
        Array.Fill(rgba, (byte)0xAB);

        var decoded = new DecodedImagery(chunk, rgba);
        rgba[0] = 0xFF;

        Assert.Equal(0xAB, decoded.Rgba.ToArray()[0]);
    }

    [Fact]
    public void DecodedImageryValidatesItsBufferSize()
    {
        var chunk = new ImageryChunk(0, 0, 2, CraftedPng(2, 2));

        Assert.Throws<ArgumentException>(() => new DecodedImagery(chunk, new byte[15]));
        Assert.NotNull(new DecodedImagery(chunk, new byte[16]));
    }

    [Fact]
    public void CodecDiagnosticsAreValidationStageErrors()
    {
        var diagnostics = new GeoDiagnosticBag();
        PayloadChunkCodec.DecodeTerrain(0, 0, new byte[1], diagnostics);

        var diagnostic = Assert.Single(diagnostics.Items);

        Assert.Equal(GeoStage.Validate, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
    }

    private static float GlobalHeight(int globalX, int globalY)
    {
        return ((globalX * 7) - (globalY * 3)) * 0.25f;
    }

    private static float[] TerrainFromLattice(int chunkX, int chunkY)
    {
        var heights = new float[StoredAxis * StoredAxis];

        for (var sy = 0; sy < StoredAxis; sy++)
        {
            for (var sx = 0; sx < StoredAxis; sx++)
            {
                heights[(sy * StoredAxis) + sx] = GlobalHeight((chunkX * ChunkLayout.IntervalsPerChunk) + sx - 1, (chunkY * ChunkLayout.IntervalsPerChunk) + sy - 1);
            }
        }

        return heights;
    }

    private static byte[] MaterialFromLattice(int chunkX, int chunkY, int paletteCount)
    {
        var indices = new byte[CoreAxis * CoreAxis];

        for (var y = 0; y < CoreAxis; y++)
        {
            for (var x = 0; x < CoreAxis; x++)
            {
                var globalX = (chunkX * ChunkLayout.IntervalsPerChunk) + x;
                var globalY = (chunkY * ChunkLayout.IntervalsPerChunk) + y;
                indices[(y * CoreAxis) + x] = (byte)((globalX + (2 * globalY)) % paletteCount);
            }
        }

        return indices;
    }

    private static byte[] MaskFromLattice(int chunkX, int chunkY)
    {
        var flags = new byte[CoreAxis * CoreAxis];

        for (var y = 0; y < CoreAxis; y++)
        {
            for (var x = 0; x < CoreAxis; x++)
            {
                var globalX = (chunkX * ChunkLayout.IntervalsPerChunk) + x;
                var globalY = (chunkY * ChunkLayout.IntervalsPerChunk) + y;
                flags[(y * CoreAxis) + x] = (byte)((globalX + (5 * globalY)) & 3);
            }
        }

        return flags;
    }

    private static byte[] ZlibCompress(byte[] raw)
    {
        using var output = new System.IO.MemoryStream();

        using (var zlib = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return output.ToArray();
    }

    /// <summary>Raw non-interlaced RGBA8 scanlines: a filter byte 0..4 followed by width * 4 channel bytes per row.</summary>
    private static byte[] RawScanlines(int pixels, int rowCount, int extraBytes = 0)
    {
        var rowBytes = (pixels * 4) + 1;
        var raw = new byte[(rowCount * rowBytes) + extraBytes];

        for (var y = 0; y < rowCount; y++)
        {
            raw[y * rowBytes] = (byte)(y % 5);

            for (var x = 0; x < pixels * 4; x++)
            {
                raw[(y * rowBytes) + 1 + x] = (byte)(x + y);
            }
        }

        return raw;
    }

    private static byte[] GradientRgba(int pixels)
    {
        var rgba = new byte[pixels * pixels * 4];

        for (var y = 0; y < pixels; y++)
        {
            for (var x = 0; x < pixels; x++)
            {
                var offset = ((y * pixels) + x) * 4;
                rgba[offset] = (byte)(x * (255 / Math.Max(1, pixels - 1)));
                rgba[offset + 1] = (byte)(y * (255 / Math.Max(1, pixels - 1)));
                rgba[offset + 2] = (byte)((x + y) % 256);
                rgba[offset + 3] = (byte)(255 - rgba[offset]);
            }
        }

        return rgba;
    }

    private static byte[] EncodeRealPng(int pixels, byte[] rgba)
    {
        using var image = new Image<Rgba32>(pixels, pixels);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < pixels; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (var x = 0; x < pixels; x++)
                {
                    var offset = ((y * pixels) + x) * 4;
                    row[x] = new Rgba32(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                }
            }
        });

        using var stream = new System.IO.MemoryStream();
        var encoder = new PngEncoder
        {
            ColorType = PngColorType.RgbWithAlpha,
            BitDepth = PngBitDepth.Bit8,
            CompressionLevel = PngCompressionLevel.DefaultCompression,
            FilterMethod = PngFilterMethod.Adaptive,
            InterlaceMethod = PngInterlaceMode.None,
            ChunkFilter = PngChunkFilter.None,
        };

        encoder.Encode(image, stream);

        return stream.ToArray();
    }

    private static Dictionary<string, byte[]> SplitChunks(byte[] png)
    {
        var chunks = new Dictionary<string, byte[]>();
        var offset = PngSignature.Length;

        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            chunks[type] = png[(offset + 8)..(offset + 8 + length)];
            offset += 12 + length;
        }

        return chunks;
    }

    private static byte[] CraftedPng(int width, int height)
    {
        return new PngBuilder()
            .Add("IHDR", IhdrData(width, height))
            .Add("IDAT", [0xDE, 0xAD, 0xBE, 0xEF])
            .Add("IEND", [])
            .ToArray();
    }

    private static byte[] IhdrData(int width, int height)
    {
        var data = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(data, width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), height);
        data[8] = 8;
        data[9] = 6;
        return data;
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string SingleCode(GeoDiagnosticBag diagnostics)
    {
        return Assert.Single(diagnostics.Items).Code;
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }

    /// <summary>Builds crafted PNG files with independent CRC computation; IHDR-first is not enforced here.</summary>
    private sealed class PngBuilder
    {
        private readonly List<(uint Length, byte[] Type, byte[] Data)> _chunks = [];

        public static PngBuilder Create()
        {
            return new PngBuilder();
        }

        public PngBuilder Add(string type, byte[] data)
        {
            return AddRaw((uint)data.Length, System.Text.Encoding.ASCII.GetBytes(type), data);
        }

        public PngBuilder AddRaw(uint length, byte[] type, byte[] data)
        {
            _chunks.Add((length, type, data));
            return this;
        }

        public byte[] ToArray()
        {
            using var stream = new System.IO.MemoryStream();
            stream.Write(PngSignature);

            foreach (var (length, type, data) in _chunks)
            {
                var header = new byte[8];
                BinaryPrimitives.WriteUInt32BigEndian(header, length);
                type.CopyTo(header, 4);
                stream.Write(header);
                stream.Write(data);

                var crc = Crc32(header.AsSpan(4, 4).ToArray().Concat(data).ToArray());

                var crcBytes = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
                stream.Write(crcBytes);
            }

            return stream.ToArray();
        }

        private static uint Crc32(byte[] data)
        {
            // Bitwise reference implementation, independent of the codec's table form.
            var crc = 0xFFFFFFFFu;

            foreach (var value in data)
            {
                crc ^= value;

                for (var bit = 0; bit < 8; bit++)
                {
                    var mask = (crc & 1) != 0 ? 0xEDB88320u : 0u;
                    crc = (crc >> 1) ^ mask;
                }
            }

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
