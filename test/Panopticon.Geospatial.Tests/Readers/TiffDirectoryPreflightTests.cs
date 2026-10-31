#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Readers;
using Xunit;

namespace Panopticon.Geospatial.Tests.Readers;

public class TiffDirectoryPreflightTests
{
    private const ushort ImageWidthTag = 256;

    private const ushort ImageLengthTag = 257;

    private const ushort BitsPerSampleTag = 258;

    private const ushort CompressionTag = 259;

    private const ushort PhotometricTag = 262;

    private const ushort StripOffsetsTag = 273;

    private const ushort SamplesPerPixelTag = 277;

    private const ushort RowsPerStripTag = 278;

    private const ushort StripByteCountsTag = 279;

    private const ushort PlanarConfigTag = 284;

    private const ushort TileWidthTag = 322;

    private const ushort TileLengthTag = 323;

    private const ushort TileOffsetsTag = 324;

    private const ushort TileByteCountsTag = 325;

    private const ushort SubIfdsTag = 330;

    private const ushort SampleFormatTag = 339;

    private const uint OneGiB = 0x4000_0000;

    [Fact]
    public void LittleEndianStrippedImage_Passes()
    {
        var result = Validate(StrippedImage(littleEndian: true), out var diagnostics);

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void BigEndianStrippedImage_Passes()
    {
        var result = Validate(StrippedImage(littleEndian: false), out var diagnostics);

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void LittleEndianTiledImage_Passes()
    {
        var result = Validate(TiledImage(littleEndian: true), out var diagnostics);

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void BigEndianTiledImage_Passes()
    {
        var result = Validate(TiledImage(littleEndian: false), out var diagnostics);

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void SeparatePlanesMultiplyStripCount_Passes()
    {
        // 2x2, two bands, separate planes, one row per strip: four 2-byte strips.
        var result = Validate(StrippedImage(littleEndian: true, width: 2, height: 2, bands: 2, planar: 2, rowsPerStrip: 1), out var diagnostics);

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void RowsPerStripOmitted_DefaultsToWholeImage_Passes()
    {
        var builder = StrippedBuilder(littleEndian: true, width: 4, height: 2, omitRowsPerStrip: true);

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void CompressedStripDeclaredBytesAreTrusted_Passes()
    {
        var result = Validate(
            StrippedImage(littleEndian: true, width: 4, height: 2, rowsPerStrip: 2, compression: 8, byteCounts: [8], segmentLengths: [8]),
            out var diagnostics);

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void InlineShortByteCounts_Passes()
    {
        // StripByteCounts SHORT[2] fits inline in the 4-byte value field.
        var builder = StrippedBuilder(littleEndian: true, width: 4, height: 2, rowsPerStrip: 1, byteCountsAsShorts: true);

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void BigTiffVersion_IsRejected()
    {
        var file = new byte[] { 0x49, 0x49, 43, 0, 8, 0, 0, 0 };

        var result = Validate(file, out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-006", GeoStage.Parse, "BigTIFF");
    }

    [Fact]
    public void UnknownVersionNumber_IsRejected()
    {
        var file = new byte[] { 0x49, 0x49, 41, 0, 8, 0, 0, 0 };

        var result = Validate(file, out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "version 42");
    }

    [Fact]
    public void InvalidByteOrderMarker_IsRejected()
    {
        var file = new byte[] { 0x41, 0x42, 42, 0, 8, 0, 0, 0 };

        var result = Validate(file, out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "neither II nor MM");
    }

    [Fact]
    public void TruncatedHeader_IsRejected()
    {
        var file = StrippedImage(littleEndian: true)[..4];

        var result = Validate(file, out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "header");
    }

    [Fact]
    public void FirstIfdOffsetPastEnd_IsRejected()
    {
        var file = new byte[24];
        file[0] = 0x49;
        file[1] = 0x49;
        file[2] = 42;
        file[4] = 0x00;
        file[5] = 0x00;
        file[6] = 0x01;
        file[7] = 0x00;

        var result = Validate(file, out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "outside");
    }

    [Fact]
    public void NextIfdLink_IsRejectedAsMultiPage()
    {
        var file = StrippedImage(littleEndian: true, nextIfd: 8);

        var result = Validate(file, out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-006", GeoStage.Parse, "Multi-page");
    }

    [Fact]
    public void SubIfdTag_IsRejected()
    {
        var builder = StrippedBuilder(littleEndian: true);
        builder.Long(SubIfdsTag, 100);

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-006", GeoStage.Parse, "Sub-IFD");
    }

    [Fact]
    public void DuplicateTag_IsRejected()
    {
        var builder = StrippedBuilder(littleEndian: true);
        builder.Short(ImageWidthTag, 4);

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "more than once");
    }

    [Fact]
    public void BigTiffFieldType_IsRejected()
    {
        var builder = new TiffBuilder(littleEndian: true);
        builder.Short(ImageWidthTag, 1);
        builder.Short(ImageLengthTag, 1);
        builder.RawInline(700, type: 16, count: 1, field: [1, 0, 0, 0]);

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "field type");
    }

    [Fact]
    public void IfdEntryCountOverrunsFile_IsRejected()
    {
        // Header claims a directory of three entries with room for one.
        var file = new byte[]
        {
            0x49, 0x49, 42, 0, 8, 0, 0, 0,
            3, 0,
            0, 1, 4, 0, 1, 0, 0, 0, 4, 0, 0, 0,
        };

        var result = Validate(file, out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "claims 3 entries");
    }

    [Fact]
    public void GiantDeclaredArrayAbsentFromFiles_IsRejected()
    {
        // A DOUBLE array of 134217728 values declares 1 GiB of payload that the file lacks.
        var builder = new TiffBuilder(littleEndian: true);
        builder.Short(ImageWidthTag, 1);
        builder.Short(ImageLengthTag, 1);
        builder.RawArray(700, type: 12, count: 134_217_728, declaredOffset: 100);

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "past the end");
    }

    [Fact]
    public void MetadataBudgetExceeded_IsRejected()
    {
        // Two 9 MiB ASCII arrays fit the sparse file but total 18 MiB, over the 16 MiB budget.
        var builder = new TiffBuilder(littleEndian: true);
        builder.Short(ImageWidthTag, 1);
        builder.Short(ImageLengthTag, 1);
        builder.RawArray(700, type: 2, count: 9_437_184, declaredOffset: 100);
        builder.RawArray(701, type: 2, count: 9_437_184, declaredOffset: 100 + 9_437_184);

        var file = builder.Build();
        var result = Validate(file, out var diagnostics, extendTo: 100 + 2L * 9_437_184 + 16);

        Assert.False(result);
        AssertError(diagnostics, "GEO-RAS-005", GeoStage.Raster, "budget");
    }

    [Fact]
    public void GiantEncodedStripBeyondCap_IsRejected()
    {
        // 1x1 Deflate strip declaring 1 GiB of encoded bytes; the sparse file actually holds
        // the range, so only the encoded cap can reject it under the 64-byte decoded limit.
        var file = StrippedImage(
            littleEndian: true,
            width: 1,
            height: 1,
            compression: 8,
            byteCounts: [OneGiB],
            segmentLengths: [1]);

        var result = Validate(file, out var diagnostics, limits: new GeospatialImportLimits(MaxRasterDecodedBytes: 64), extendTo: file.Length + OneGiB);

        Assert.False(result);
        AssertError(diagnostics, "GEO-RAS-005", GeoStage.Raster, "over the cap");
    }

    [Fact]
    public void GiantEncodedStripAbsentPayload_IsRejected()
    {
        var file = StrippedImage(
            littleEndian: true,
            width: 1,
            height: 1,
            compression: 8,
            byteCounts: [OneGiB],
            segmentLengths: [1]);

        var result = Validate(file, out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-007", GeoStage.Parse, "outside the");
    }

    [Fact]
    public void WidthOverDimensionLimit_IsRejected()
    {
        var result = Validate(
            StrippedImage(littleEndian: true, width: 200, height: 1),
            out var diagnostics,
            limits: new GeospatialImportLimits(MaxRasterWidth: 100));

        Assert.False(result);
        AssertError(diagnostics, "GEO-RAS-005", GeoStage.Raster, "exceed the limit");
    }

    [Fact]
    public void DecodedBytesOverLimit_IsRejected()
    {
        // 64x64 doubles normalize to 32768 bytes, over the 1024-byte limit.
        var result = Validate(
            StrippedImage(littleEndian: true, width: 64, height: 64, rowsPerStrip: 64),
            out var diagnostics,
            limits: new GeospatialImportLimits(MaxRasterDecodedBytes: 1024));

        Assert.False(result);
        AssertError(diagnostics, "GEO-RAS-005", GeoStage.Raster, "normalized bytes");
    }

    [Fact]
    public void ZeroWidth_IsRejected()
    {
        var result = Validate(StrippedImage(littleEndian: true, width: 0), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "invalid dimensions");
    }

    [Fact]
    public void OperatorChunkLimit_DoesNotRejectMultiStripInput()
    {
        // MaxChunkCount governs the output grid, not input segment counts; a valid multi-strip
        // DEM-style file must pass with MaxChunkCount 1.
        var result = Validate(
            StrippedImage(littleEndian: true, width: 4, height: 2, rowsPerStrip: 1),
            out var diagnostics,
            limits: new GeospatialImportLimits(MaxChunkCount: 1));

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void OperatorChunkLimit_DoesNotRejectTiledInput()
    {
        var result = Validate(
            TiledImage(littleEndian: true),
            out var diagnostics,
            limits: new GeospatialImportLimits(MaxChunkCount: 1));

        Assert.True(result);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void SegmentCountOverMetadataDerivedCap_IsRejected()
    {
        // Four million single-row strips exceed the 16 MiB / 8 metadata-derived segment cap.
        var builder = new TiffBuilder(littleEndian: true);
        builder.Long(ImageWidthTag, 1);
        builder.Long(ImageLengthTag, 4_000_000);
        builder.Short(BitsPerSampleTag, 8);
        builder.Short(CompressionTag, 1);
        builder.Short(SamplesPerPixelTag, 1);
        builder.Long(RowsPerStripTag, 1);
        builder.Short(PlanarConfigTag, 1);
        builder.Longs(StripOffsetsTag, [100]);
        builder.Longs(StripByteCountsTag, [100]);

        var result = Validate(
            builder.Build(),
            out var diagnostics,
            limits: new GeospatialImportLimits(MaxRasterHeight: 4_000_000));

        Assert.False(result);
        AssertError(diagnostics, "GEO-RAS-005", GeoStage.Raster, "metadata-derived limit");
    }

    [Fact]
    public void OversizedDimensionsWithGenerousLimits_OverflowFailsTyped()
    {
        // width * height * bands overflows checked long arithmetic under limits generous
        // enough to pass the dimension checks; the typed resource diagnostic fires before
        // any segment array is read or allocated.
        var builder = new TiffBuilder(littleEndian: true);
        builder.Long(ImageWidthTag, int.MaxValue);
        builder.Long(ImageLengthTag, int.MaxValue);
        builder.Short(SamplesPerPixelTag, 4);

        var result = Validate(
            builder.Build(),
            out var diagnostics,
            limits: new GeospatialImportLimits(
                MaxRasterWidth: int.MaxValue,
                MaxRasterHeight: int.MaxValue,
                MaxRasterDecodedBytes: long.MaxValue));

        Assert.False(result);
        AssertError(diagnostics, "GEO-RAS-005", GeoStage.Raster, "overflow");
    }

    [Fact]
    public void GenerousDecodedLimit_OverflowFailsTyped()
    {
        // A decoded limit near long.MaxValue overflows the encoded-cap addition itself.
        var file = StrippedImage(littleEndian: true, width: 1, height: 1);

        var result = Validate(
            file,
            out var diagnostics,
            limits: new GeospatialImportLimits(MaxRasterDecodedBytes: long.MaxValue));

        Assert.False(result);
        AssertError(diagnostics, "GEO-RAS-005", GeoStage.Raster, "overflow");
    }

    [Fact]
    public void UndersizedNoneStripWouldReadMetadataAsPixels_IsRejected()
    {
        // 2x1 uint8 needs two bytes; the declared count of one leaves the following metadata
        // byte for LibTiff to absorb while repairing the strip.
        var builder = StrippedBuilder(littleEndian: true, width: 2, height: 1, byteCounts: [1], segmentLengths: [2]);
        builder.Ascii(700, "meta");

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-007", GeoStage.Parse, "requires 2");
    }

    [Fact]
    public void StripCountMismatch_IsRejected()
    {
        // Two strips are required by the layout but StripByteCounts declares one entry.
        var result = Validate(
            StrippedImage(littleEndian: true, width: 4, height: 2, rowsPerStrip: 1, byteCounts: [4]),
            out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "requires 2");
    }

    [Fact]
    public void StripTruncatedAtEnd_IsRejected()
    {
        var file = StrippedImage(littleEndian: true)[..^2];

        var result = Validate(file, out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-007", GeoStage.Parse, "outside the");
    }

    [Fact]
    public void MissingSegmentTags_IsRejected()
    {
        var builder = new TiffBuilder(littleEndian: true);
        builder.Short(ImageWidthTag, 4);
        builder.Short(ImageLengthTag, 2);

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "neither strip nor tile");
    }

    [Fact]
    public void BothStripAndTileTags_IsRejected()
    {
        var builder = StrippedBuilder(littleEndian: true);
        builder.Longs(TileOffsetsTag, [0]);
        builder.Longs(TileByteCountsTag, [0]);

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "both strip and tile");
    }

    [Fact]
    public void TileDimensionNotMultipleOfSixteen_IsRejected()
    {
        var result = Validate(TiledImage(littleEndian: true, tileWidth: 20), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "multiples of 16");
    }

    [Fact]
    public void UndersizedPaddedTile_IsRejected()
    {
        // A 16x16 uint8 tile is padded to 256 bytes even though the image is 4x4.
        var result = Validate(
            TiledImage(littleEndian: true, width: 4, height: 4, byteCounts: [200], tileLengths: [200]),
            out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-007", GeoStage.Parse, "requires 256");
    }

    [Fact]
    public void CompressedStripOverlappingDirectory_IsRejected()
    {
        // The Deflate strip starts at the directory offset, so its declared range swallows
        // the IFD; compressed segments skip the exact-size check, so only overlap rejects it.
        var builder = new TiffBuilder(littleEndian: true);
        builder.Short(ImageWidthTag, 4);
        builder.Short(ImageLengthTag, 2);
        builder.Short(BitsPerSampleTag, 8);
        builder.Short(CompressionTag, 8);
        builder.Short(PhotometricTag, 1);
        builder.Short(SamplesPerPixelTag, 1);
        builder.Long(RowsPerStripTag, 2);
        builder.Short(PlanarConfigTag, 1);
        builder.Long(StripOffsetsTag, 8);
        builder.Long(StripByteCountsTag, 40);
        builder.AddSegment(new byte[64]);

        var result = Validate(builder.Build(), out var diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "overlaps file metadata");
    }

    [Fact]
    public void MissingFile_IsRejected()
    {
        var diagnostics = new GeoDiagnosticBag();
        var path = Path.Combine(Path.GetTempPath(), $"tiff-preflight-absent-{Guid.NewGuid():N}.tif");

        var result = TiffDirectoryPreflight.TryValidate(path, new GeospatialImportLimits(), diagnostics);

        Assert.False(result);
        AssertError(diagnostics, "GEO-PAR-001", GeoStage.Parse, "could not be read");
    }

    private static bool Validate(
        byte[] bytes,
        out GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null,
        long? extendTo = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tiff-preflight-{Guid.NewGuid():N}.tif");
        File.WriteAllBytes(path, bytes);

        if (extendTo is { } target)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
            stream.SetLength(target);
        }

        diagnostics = new GeoDiagnosticBag();

        try
        {
            return TiffDirectoryPreflight.TryValidate(path, limits ?? new GeospatialImportLimits(), diagnostics);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static GeoDiagnostic AssertError(GeoDiagnosticBag diagnostics, string code, GeoStage stage, string messageContains)
    {
        var error = Assert.Single(diagnostics.Items, static item => item.Severity == GeoSeverity.Error);

        Assert.Equal(code, error.Code);
        Assert.Equal(stage, error.Stage);
        Assert.Contains(messageContains, error.Message);

        return error;
    }

    private static byte[] StrippedImage(
        bool littleEndian,
        int width = 4,
        int height = 2,
        int bands = 1,
        int bits = 8,
        int? rowsPerStrip = null,
        ushort compression = 1,
        ushort planar = 1,
        uint[]? byteCounts = null,
        int[]? segmentLengths = null,
        uint nextIfd = 0)
    {
        return StrippedBuilder(
            littleEndian,
            width,
            height,
            bands,
            bits,
            rowsPerStrip,
            compression,
            planar,
            byteCounts,
            segmentLengths,
            nextIfd).Build();
    }

    private static TiffBuilder StrippedBuilder(
        bool littleEndian,
        int width = 4,
        int height = 2,
        int bands = 1,
        int bits = 8,
        int? rowsPerStrip = null,
        ushort compression = 1,
        ushort planar = 1,
        uint[]? byteCounts = null,
        int[]? segmentLengths = null,
        uint nextIfd = 0,
        bool omitRowsPerStrip = false,
        bool byteCountsAsShorts = false)
    {
        var builder = new TiffBuilder(littleEndian);
        var rows = rowsPerStrip ?? height;
        var samplesPerRow = planar == 2 ? 1 : bands;
        var rowBytes = (width * samplesPerRow * bits + 7) / 8;
        var stripsPerPlane = (height + rows - 1) / rows;
        var segmentCount = stripsPerPlane * (planar == 2 ? bands : 1);

        builder.DimensionTag(ImageWidthTag, width);
        builder.DimensionTag(ImageLengthTag, height);

        if (bands == 1)
        {
            builder.Short(BitsPerSampleTag, (ushort)bits);
        }
        else
        {
            builder.Shorts(BitsPerSampleTag, Enumerable.Repeat((ushort)bits, bands).ToArray());
        }

        builder.Short(CompressionTag, compression);
        builder.Short(PhotometricTag, 1);
        builder.Short(SamplesPerPixelTag, (ushort)bands);

        if (!omitRowsPerStrip)
        {
            builder.Long(RowsPerStripTag, (uint)rows);
        }

        builder.Short(PlanarConfigTag, planar);

        segmentLengths ??= Enumerable.Range(0, segmentCount)
            .Select(index => Math.Min(rows, height - (index % stripsPerPlane) * rows) * rowBytes)
            .ToArray();

        foreach (var length in segmentLengths)
        {
            builder.AddSegment(new byte[length]);
        }

        byteCounts ??= segmentLengths.Select(static length => (uint)length).ToArray();
        builder.LongsFromLayout(
            StripOffsetsTag,
            (uint)segmentCount,
            layout => layout.SegmentOffsets.Select(static offset => (uint)offset).ToArray());

        if (byteCountsAsShorts)
        {
            builder.Shorts(StripByteCountsTag, byteCounts.Select(static value => (ushort)value).ToArray());
        }
        else
        {
            builder.Longs(StripByteCountsTag, byteCounts);
        }

        builder.NextIfdOffset = nextIfd;
        return builder;
    }

    private static byte[] TiledImage(
        bool littleEndian,
        int width = 20,
        int height = 18,
        int tileWidth = 16,
        int tileHeight = 16,
        int bands = 1,
        int bits = 8,
        ushort compression = 1,
        ushort planar = 1,
        uint[]? byteCounts = null,
        int[]? tileLengths = null)
    {
        var builder = new TiffBuilder(littleEndian);
        var samplesPerTileRow = planar == 2 ? 1 : bands;
        var tileRowBytes = (tileWidth * samplesPerTileRow * bits + 7) / 8;
        var paddedTileBytes = tileHeight * tileRowBytes;
        var segmentCount = ((width + tileWidth - 1) / tileWidth) * ((height + tileHeight - 1) / tileHeight) * (planar == 2 ? bands : 1);

        builder.DimensionTag(ImageWidthTag, width);
        builder.DimensionTag(ImageLengthTag, height);
        builder.Short(BitsPerSampleTag, (ushort)bits);
        builder.Short(CompressionTag, compression);
        builder.Short(PhotometricTag, 1);
        builder.Short(SamplesPerPixelTag, (ushort)bands);
        builder.Short(PlanarConfigTag, planar);
        builder.DimensionTag(TileWidthTag, tileWidth);
        builder.DimensionTag(TileLengthTag, tileHeight);

        tileLengths ??= Enumerable.Repeat(paddedTileBytes, segmentCount).ToArray();

        foreach (var length in tileLengths)
        {
            builder.AddSegment(new byte[length]);
        }

        byteCounts ??= tileLengths.Select(static length => (uint)length).ToArray();
        builder.LongsFromLayout(
            TileOffsetsTag,
            (uint)segmentCount,
            layout => layout.SegmentOffsets.Select(static offset => (uint)offset).ToArray());
        builder.Longs(TileByteCountsTag, byteCounts);

        return builder.Build();
    }

    /// <summary>
    /// Handcrafts classic TIFF files: header, one directory, out-of-line arrays, then segment
    /// blobs, in that physical order. Values are resolved during <see cref="Build"/> so strip
    /// offset arrays can reference segment offsets computed from the final layout.
    /// </summary>
    private sealed class TiffBuilder
    {
        private readonly bool _littleEndian;
        private readonly List<Entry> _entries = [];
        private readonly List<byte[]> _segments = [];

        public TiffBuilder(bool littleEndian)
        {
            _littleEndian = littleEndian;
        }

        public uint NextIfdOffset { get; set; }

        public int AddSegment(byte[] data)
        {
            _segments.Add(data);
            return _segments.Count - 1;
        }

        public void Short(ushort tag, ushort value)
        {
            _entries.Add(new Entry(tag, 3, 1)
            {
                InlineField = layout => Inline(EncodeUInt16(value)),
            });
        }

        public void Long(ushort tag, uint value)
        {
            _entries.Add(new Entry(tag, 4, 1)
            {
                InlineField = layout => EncodeUInt32(value),
            });
        }

        public void Shorts(ushort tag, ushort[] values)
        {
            if ((long)values.Length * 2 <= 4)
            {
                _entries.Add(new Entry(tag, 3, (uint)values.Length)
                {
                    InlineField = layout => Inline(Concat(values.Select(EncodeUInt16))),
                });
                return;
            }

            _entries.Add(new Entry(tag, 3, (uint)values.Length)
            {
                Payload = layout => Concat(values.Select(EncodeUInt16)),
            });
        }

        public void Longs(ushort tag, uint[] values)
        {
            if (values.Length == 1)
            {
                _entries.Add(new Entry(tag, 4, 1)
                {
                    InlineField = layout => EncodeUInt32(values[0]),
                });
                return;
            }

            _entries.Add(new Entry(tag, 4, (uint)values.Length)
            {
                Payload = layout => Concat(values.Select(EncodeUInt32)),
            });
        }

        public void LongsFromLayout(ushort tag, uint count, Func<FileLayout, uint[]> resolve)
        {
            if (count == 1)
            {
                _entries.Add(new Entry(tag, 4, 1)
                {
                    InlineField = layout => EncodeUInt32(resolve(layout)[0]),
                });
                return;
            }

            _entries.Add(new Entry(tag, 4, count)
            {
                Payload = layout => Concat(resolve(layout).Select(EncodeUInt32)),
            });
        }

        public void Ascii(ushort tag, string text)
        {
            var payload = System.Text.Encoding.ASCII.GetBytes(text + "\0");

            if (payload.Length <= 4)
            {
                _entries.Add(new Entry(tag, 2, (uint)payload.Length)
                {
                    InlineField = layout => Inline(payload),
                });
                return;
            }

            _entries.Add(new Entry(tag, 2, (uint)payload.Length)
            {
                Payload = layout => payload,
            });
        }

        public void DimensionTag(ushort tag, long value)
        {
            if (value > ushort.MaxValue)
            {
                Long(tag, (uint)value);
            }
            else
            {
                Short(tag, (ushort)value);
            }
        }

        public void RawInline(ushort tag, ushort type, uint count, byte[] field)
        {
            _entries.Add(new Entry(tag, type, count)
            {
                RawField = field,
            });
        }

        public void RawArray(ushort tag, ushort type, uint count, long declaredOffset)
        {
            _entries.Add(new Entry(tag, type, count)
            {
                RawField = EncodeUInt32((uint)declaredOffset),
            });
        }

        public byte[] Build()
        {
            var ifdSize = 2 + _entries.Count * 12 + 4;
            var arrayOffsets = new long[_entries.Count];
            long cursor = 8 + ifdSize;

            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];

                if (entry.Payload is not null)
                {
                    arrayOffsets[index] = cursor;
                    cursor += entry.PayloadSize;
                }
            }

            var segmentOffsets = new long[_segments.Count];

            for (var index = 0; index < _segments.Count; index++)
            {
                segmentOffsets[index] = cursor;
                cursor += _segments[index].Length;
            }

            var layout = new FileLayout(arrayOffsets, segmentOffsets);
            var file = new MemoryStream();
            file.Write(_littleEndian ? new byte[] { 0x49, 0x49 } : new byte[] { 0x4D, 0x4D });
            file.Write(EncodeUInt16(42));
            file.Write(EncodeUInt32(8));
            file.Write(EncodeUInt16((ushort)_entries.Count));

            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                file.Write(EncodeUInt16(entry.Tag));
                file.Write(EncodeUInt16(entry.Type));
                file.Write(EncodeUInt32(entry.Count));
                file.Write(EntryField(entry, index, layout));
            }

            file.Write(EncodeUInt32(NextIfdOffset));

            for (var index = 0; index < _entries.Count; index++)
            {
                var payload = _entries[index].Payload;

                if (payload is not null)
                {
                    file.Write(payload(layout));
                }
            }

            foreach (var segment in _segments)
            {
                file.Write(segment);
            }

            return file.ToArray();
        }

        private byte[] EntryField(Entry entry, int index, FileLayout layout)
        {
            if (entry.RawField is not null)
            {
                return entry.RawField;
            }

            if (entry.Payload is not null)
            {
                return EncodeUInt32((uint)layout.ArrayOffsets[index]);
            }

            return entry.InlineField is not null ? entry.InlineField(layout) : new byte[4];
        }

        private byte[] Inline(byte[] valueBytes)
        {
            var field = new byte[4];
            valueBytes.CopyTo(field, 0);
            return field;
        }

        private byte[] EncodeUInt16(ushort value)
        {
            return _littleEndian ? [(byte)value, (byte)(value >> 8)] : [(byte)(value >> 8), (byte)value];
        }

        private byte[] EncodeUInt32(uint value)
        {
            return _littleEndian
                ? [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)]
                : [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        }

        private static byte[] Concat(IEnumerable<byte[]> parts)
        {
            return [.. parts.SelectMany(static part => part)];
        }

        public sealed record FileLayout(long[] ArrayOffsets, long[] SegmentOffsets);

        private sealed class Entry(ushort tag, ushort type, uint count)
        {
            public ushort Tag { get; } = tag;

            public ushort Type { get; } = type;

            public uint Count { get; } = count;

            public Func<FileLayout, byte[]>? InlineField { get; init; }

            public Func<FileLayout, byte[]>? Payload { get; init; }

            public byte[]? RawField { get; init; }

            public long PayloadSize => (long)Count * TypeSize(Type);

            private static int TypeSize(ushort type)
            {
                return type switch
                {
                    1 or 2 or 6 or 7 => 1,
                    3 or 8 => 2,
                    4 or 11 or 13 => 4,
                    5 or 10 or 12 => 8,
                    _ => 1,
                };
            }
        }
    }
}
