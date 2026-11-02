using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Panopticon.Geospatial;

namespace Panopticon.Geospatial.Readers;

/// <summary>
/// Bounded raw scan of a classic TIFF header and first image file directory, run before LibTiff
/// opens the file. LibTiff allocates from declared array counts on open and repairs undersized
/// uncompressed strip counts on read, so a hostile directory can force a large allocation or
/// pull following metadata into pixel data before decoded-stage guards run. This scan reads
/// only the header, the first directory, and the small dimension, layout, and segment tags;
/// every other tag contributes its declared byte size to a fixed budget and is never
/// materialized. BigTIFF, multi-page files, and sub-IFDs are rejected as unsupported. No
/// LibTiff API is used.
/// </summary>
internal static class TiffDirectoryPreflight
{
    private const int HeaderSize = 8;

    private const int IfdEntrySize = 12;

    private const int IfdOverhead = 6;

    private const int InlineValueLimit = 4;

    private const ushort ClassicTiffVersion = 42;

    private const ushort BigTiffVersion = 43;

    private const ushort NoCompression = 1;

    private const ushort PlanarSeparate = 2;

    private const ushort ShortType = 3;

    private const ushort LongType = 4;

    private const ushort ImageWidthTag = 256;

    private const ushort ImageLengthTag = 257;

    private const ushort BitsPerSampleTag = 258;

    private const ushort CompressionTag = 259;

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

    /// <summary>
    /// Fixed budget for every out-of-line tag payload combined, checked before any payload is
    /// allocated or read. Doubles as the codec-overhead allowance on the encoded-segment caps.
    /// </summary>
    private const long MaxMetadataBytes = 16L * 1024 * 1024;

    /// <summary>
    /// Upper bound on strip or tile segment counts, derived from the metadata budget: each
    /// segment consumes a LONG offset plus a LONG byte count of declared payload, so larger
    /// counts cannot be backed by metadata anyway. Independent of operator output-grid chunk
    /// limits, which must not reject valid multi-strip inputs.
    /// </summary>
    private const long MaxSegments = MaxMetadataBytes / 8;

    // Code values mirror GeoTiffDiagnosticCodes, whose strings are final per its doc comment;
    // literals keep this file compiling independently while that class is in flight.
    private const string ParseFailureCode = "GEO-PAR-001";

    private const string ParseUnsupportedLayoutCode = "GEO-PAR-006";

    private const string ParseRasterDataTruncatedCode = "GEO-PAR-007";

    private const string RasterLimitExceededCode = "GEO-RAS-005";

    // Field type byte sizes for classic TIFF types 1 through 13. Type 0 and anything past 13,
    // including the BigTIFF-only 16 through 18, are invalid in a classic directory.
    private static readonly int[] FieldTypeSizes = [0, 1, 1, 2, 4, 8, 1, 1, 2, 4, 8, 4, 8, 4];

    internal static bool TryValidate(string path, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(diagnostics);

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                8192,
                FileOptions.SequentialScan);

            return TryValidateStream(stream, limits, path, diagnostics);
        }
        catch (OverflowException ex)
        {
            return FailLimit(
                diagnostics,
                path,
                $"TIFF declared dimensions or configured limits overflow the resource arithmetic: {ex.Message}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return FailParse(diagnostics, path, $"TIFF file could not be read: {ex.Message}", ex);
        }
    }

    private static bool TryValidateStream(Stream stream, GeospatialImportLimits limits, string path, GeoDiagnosticBag diagnostics)
    {
        var fileLength = stream.Length;

        if (fileLength < HeaderSize)
        {
            return FailParse(
                diagnostics,
                path,
                $"TIFF file is {Num(fileLength)} bytes, shorter than the {HeaderSize}-byte header.");
        }

        var header = ReadAt(stream, 0, HeaderSize);
        bool littleEndian;

        if (header[0] == 0x49 && header[1] == 0x49)
        {
            littleEndian = true;
        }
        else if (header[0] == 0x4D && header[1] == 0x4D)
        {
            littleEndian = false;
        }
        else
        {
            return FailParse(
                diagnostics,
                path,
                $"TIFF byte-order mark {(char)header[0]}{(char)header[1]} is neither II nor MM.");
        }

        var version = ReadUInt16(header, 2, littleEndian);

        if (version == BigTiffVersion)
        {
            return FailUnsupported(diagnostics, path, "BigTIFF (version 43) files are not supported; only classic TIFF version 42.");
        }

        if (version != ClassicTiffVersion)
        {
            return FailParse(diagnostics, path, $"TIFF version {Num(version)} is not the classic version 42.");
        }

        var ifdOffset = ReadUInt32(header, 4, littleEndian);

        if (ifdOffset < HeaderSize || ifdOffset > fileLength - 2)
        {
            return FailParse(
                diagnostics,
                path,
                $"TIFF first directory offset {Num(ifdOffset)} is outside the {Num(fileLength)}-byte file.");
        }

        var entryCount = ReadUInt16(ReadAt(stream, ifdOffset, 2), 0, littleEndian);
        var ifdEnd = ifdOffset + IfdOverhead + (long)entryCount * IfdEntrySize;

        if (ifdEnd > fileLength)
        {
            return FailParse(
                diagnostics,
                path,
                $"TIFF directory claims {Num(entryCount)} entries ending at {Num(ifdEnd)}, past the {Num(fileLength)}-byte file.");
        }

        var ifd = ReadAt(stream, ifdOffset, (int)(ifdEnd - ifdOffset));
        var nextIfdOffset = ReadUInt32(ifd, 2 + entryCount * IfdEntrySize, littleEndian);

        if (nextIfdOffset != 0)
        {
            return FailUnsupported(
                diagnostics,
                path,
                $"Multi-page TIFF files are not supported: the first directory links to another at offset {Num(nextIfdOffset)}.");
        }

        if (FindEntry(SubIfdsTag) >= 0)
        {
            return FailUnsupported(diagnostics, path, "Sub-IFDs are not supported: tag 330 is present.");
        }

        // Pass one walks the raw entries only: no payload is allocated or read before every
        // declared array has been proven to fit in the file and the combined metadata budget.
        var seenTags = new HashSet<ushort>();
        var regions = new List<(long Start, long End)> { (0, HeaderSize), (ifdOffset, ifdEnd) };
        long metadataBytes = 0;

        for (var index = 0; index < entryCount; index++)
        {
            var entryBase = 2 + index * IfdEntrySize;
            var tag = ReadUInt16(ifd, entryBase, littleEndian);
            var type = ReadUInt16(ifd, entryBase + 2, littleEndian);
            var count = ReadUInt32(ifd, entryBase + 4, littleEndian);

            if (type == 0 || type >= FieldTypeSizes.Length)
            {
                return FailParse(
                    diagnostics,
                    path,
                    $"TIFF tag {Num(tag)} declares field type {Num(type)}, invalid in a classic TIFF.");
            }

            if (!seenTags.Add(tag))
            {
                return FailParse(diagnostics, path, $"TIFF tag {Num(tag)} appears more than once in the directory.");
            }

            var payload = (long)count * FieldTypeSizes[type];

            if (payload <= InlineValueLimit)
            {
                continue;
            }

            var payloadOffset = ReadUInt32(ifd, entryBase + 8, littleEndian);
            var payloadEnd = (long)payloadOffset + payload;

            if (payloadOffset < HeaderSize || payloadEnd > fileLength)
            {
                return FailParse(
                    diagnostics,
                    path,
                    $"TIFF tag {Num(tag)} declares {Num(payload)} bytes at offset {Num(payloadOffset)}, past the end of the {Num(fileLength)}-byte file.");
            }

            metadataBytes += payload;
            regions.Add((payloadOffset, payloadEnd));
        }

        if (metadataBytes > MaxMetadataBytes)
        {
            return FailLimit(
                diagnostics,
                path,
                $"TIFF metadata arrays declare {Num(metadataBytes)} bytes in total, over the {Num(MaxMetadataBytes)}-byte budget.");
        }

        return TryValidateImage(stream, ifd, littleEndian, fileLength, regions, limits, path, diagnostics);

        int FindEntry(ushort tag)
        {
            for (var index = 0; index < entryCount; index++)
            {
                if (ReadUInt16(ifd, 2 + index * IfdEntrySize, littleEndian) == tag)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    private static bool TryValidateImage(
        Stream stream,
        byte[] ifd,
        bool littleEndian,
        long fileLength,
        List<(long Start, long End)> regions,
        GeospatialImportLimits limits,
        string path,
        GeoDiagnosticBag diagnostics)
    {
        var entryCount = (ifd.Length - IfdOverhead) / IfdEntrySize;

        int FindEntry(ushort tag)
        {
            for (var index = 0; index < entryCount; index++)
            {
                if (ReadUInt16(ifd, 2 + index * IfdEntrySize, littleEndian) == tag)
                {
                    return index;
                }
            }

            return -1;
        }

        // Returns 1 for present, 0 for absent, and -1 after adding a failure diagnostic.
        int TryReadScalar(ushort tag, string name, out long value)
        {
            value = 0;
            var entry = FindEntry(tag);

            if (entry < 0)
            {
                return 0;
            }

            var entryBase = 2 + entry * IfdEntrySize;
            var type = ReadUInt16(ifd, entryBase + 2, littleEndian);
            var count = ReadUInt32(ifd, entryBase + 4, littleEndian);

            if (type is not (ShortType or LongType) || count != 1)
            {
                FailParse(diagnostics, path, $"{name} must be a single SHORT or LONG value.");
                return -1;
            }

            value = type == ShortType
                ? ReadUInt16(ifd, entryBase + 8, littleEndian)
                : ReadUInt32(ifd, entryBase + 8, littleEndian);
            return 1;
        }

        int TryReadUniformShorts(ushort tag, string name, out long value)
        {
            value = 0;
            var entry = FindEntry(tag);

            if (entry < 0)
            {
                return 0;
            }

            var entryBase = 2 + entry * IfdEntrySize;
            var type = ReadUInt16(ifd, entryBase + 2, littleEndian);
            var count = ReadUInt32(ifd, entryBase + 4, littleEndian);

            if (type != ShortType || count < 1)
            {
                FailParse(diagnostics, path, $"{name} must store one or more SHORT values.");
                return -1;
            }

            var payload = (int)(count * 2);
            byte[] buffer;
            int bufferOffset;

            if (payload <= InlineValueLimit)
            {
                buffer = ifd;
                bufferOffset = entryBase + 8;
            }
            else
            {
                var payloadOffset = ReadUInt32(ifd, entryBase + 8, littleEndian);
                buffer = ReadAt(stream, payloadOffset, payload);
                bufferOffset = 0;
            }

            var first = ReadUInt16(buffer, bufferOffset, littleEndian);

            for (var index = 1; index < count; index++)
            {
                if (ReadUInt16(buffer, bufferOffset + (int)index * 2, littleEndian) != first)
                {
                    FailParse(diagnostics, path, $"{name} values are not uniform across samples.");
                    return -1;
                }
            }

            value = first;
            return 1;
        }

        var widthResult = TryReadScalar(ImageWidthTag, "ImageWidth", out var width);

        if (widthResult < 0)
        {
            return false;
        }

        if (widthResult == 0)
        {
            return FailParse(diagnostics, path, "TIFF carries no ImageWidth tag.");
        }

        var heightResult = TryReadScalar(ImageLengthTag, "ImageLength", out var height);

        if (heightResult < 0)
        {
            return false;
        }

        if (heightResult == 0)
        {
            return FailParse(diagnostics, path, "TIFF carries no ImageLength tag.");
        }

        if (width < 1 || height < 1)
        {
            return FailParse(diagnostics, path, $"TIFF declares invalid dimensions {Num(width)} x {Num(height)}.");
        }

        if (width > limits.MaxRasterWidth || height > limits.MaxRasterHeight)
        {
            return FailLimit(
                diagnostics,
                path,
                $"TIFF dimensions {Num(width)} x {Num(height)} exceed the limit of {Num(limits.MaxRasterWidth)} x {Num(limits.MaxRasterHeight)}.");
        }

        var bandsResult = TryReadScalar(SamplesPerPixelTag, "SamplesPerPixel", out var bands);

        if (bandsResult < 0)
        {
            return false;
        }

        if (bandsResult == 0)
        {
            bands = 1;
        }

        if (bands < 1)
        {
            return FailParse(diagnostics, path, $"TIFF declares SamplesPerPixel {Num(bands)}, which is invalid.");
        }

        var bitsResult = TryReadUniformShorts(BitsPerSampleTag, "BitsPerSample", out var bits);

        if (bitsResult < 0)
        {
            return false;
        }

        if (bitsResult == 0)
        {
            bits = 1;
        }

        if (bits < 1)
        {
            return FailParse(diagnostics, path, $"TIFF declares BitsPerSample {Num(bits)}, which is invalid.");
        }

        var sampleFormatResult = TryReadUniformShorts(SampleFormatTag, "SampleFormat", out _);

        if (sampleFormatResult < 0)
        {
            return false;
        }

        var planarResult = TryReadScalar(PlanarConfigTag, "PlanarConfiguration", out var planarConfig);

        if (planarResult < 0)
        {
            return false;
        }

        if (planarResult == 0)
        {
            planarConfig = 1;
        }

        if (planarConfig is not (1 or PlanarSeparate))
        {
            return FailParse(
                diagnostics,
                path,
                $"TIFF declares PlanarConfiguration {Num(planarConfig)}; only 1 (contiguous) and 2 (separate) are valid.");
        }

        var compressionResult = TryReadScalar(CompressionTag, "Compression", out var compression);

        if (compressionResult < 0)
        {
            return false;
        }

        if (compressionResult == 0)
        {
            compression = NoCompression;
        }

        // The import allocates one normalized double per sample, so the decoded-size guard
        // mirrors the reader: width times height times bands times eight bytes, checked so
        // generous limits cannot wrap the product past the cap.
        var normalizedBytes = checked(width * height * bands * 8L);

        if (normalizedBytes > limits.MaxRasterDecodedBytes)
        {
            return FailLimit(
                diagnostics,
                path,
                $"TIFF decoded size {Num(width)} x {Num(height)} x {Num(bands)} bands needs {Num(normalizedBytes)} normalized bytes, over the limit of {Num(limits.MaxRasterDecodedBytes)}.");
        }

        var stripOffsetsEntry = FindEntry(StripOffsetsTag) >= 0;
        var stripByteCountsEntry = FindEntry(StripByteCountsTag) >= 0;
        var tileOffsetsEntry = FindEntry(TileOffsetsTag) >= 0;
        var tileByteCountsEntry = FindEntry(TileByteCountsTag) >= 0;
        var hasStripEntries = stripOffsetsEntry || stripByteCountsEntry;
        var hasTileEntries = tileOffsetsEntry || tileByteCountsEntry;

        if (hasStripEntries && hasTileEntries)
        {
            return FailParse(diagnostics, path, "TIFF carries both strip and tile tags; exactly one segment layout is allowed.");
        }

        if (!hasStripEntries && !hasTileEntries)
        {
            return FailParse(diagnostics, path, "TIFF carries neither strip nor tile segment tags.");
        }

        if (stripOffsetsEntry != stripByteCountsEntry)
        {
            return FailParse(diagnostics, path, "TIFF carries StripOffsets and StripByteCounts, but not both.");
        }

        if (tileOffsetsEntry != tileByteCountsEntry)
        {
            return FailParse(diagnostics, path, "TIFF carries TileOffsets and TileByteCounts, but not both.");
        }

        // Encoded bytes should not exceed decoded size by more than codec overhead, so the
        // decoded limit plus the fixed budget bounds each segment and the total; a declared
        // 1 GiB strip behind a 1x1 image under a 64-byte limit is rejected.
        var maxEncodedBytes = checked(limits.MaxRasterDecodedBytes + MaxMetadataBytes);
        var segments = new List<(long Start, long End, long Index)>();
        long totalEncodedBytes = 0;

        bool TryReadSegmentArray(ushort tag, string name, bool allowShort, long expectedCount, out long[] values)
        {
            values = [];
            var entry = FindEntry(tag);

            if (entry < 0)
            {
                return false;
            }

            var entryBase = 2 + entry * IfdEntrySize;
            var type = ReadUInt16(ifd, entryBase + 2, littleEndian);
            var declaredCount = ReadUInt32(ifd, entryBase + 4, littleEndian);

            if (type != LongType && !(allowShort && type == ShortType))
            {
                FailParse(diagnostics, path, allowShort ? $"{name} must store SHORT or LONG values." : $"{name} must store LONG values.");
                return false;
            }

            if (declaredCount != expectedCount)
            {
                FailParse(
                    diagnostics,
                    path,
                    $"{name} declares {Num(declaredCount)} entries but the layout requires {Num(expectedCount)}.");
                return false;
            }

            var payload = (int)((long)declaredCount * (type == ShortType ? 2 : 4));
            byte[] buffer;
            int bufferOffset;

            if (payload <= InlineValueLimit)
            {
                buffer = ifd;
                bufferOffset = entryBase + 8;
            }
            else
            {
                var payloadOffset = ReadUInt32(ifd, entryBase + 8, littleEndian);
                buffer = ReadAt(stream, payloadOffset, payload);
                bufferOffset = 0;
            }

            values = new long[declaredCount];

            for (var index = 0; index < values.Length; index++)
            {
                values[index] = type == ShortType
                    ? ReadUInt16(buffer, bufferOffset + index * 2, littleEndian)
                    : ReadUInt32(buffer, bufferOffset + index * 4, littleEndian);
            }

            return true;
        }

        bool ValidateSegment(string kind, long index, long count, uint offset, uint bytes, long requiredBytes)
        {
            if (offset < HeaderSize || (long)offset + bytes > fileLength)
            {
                FailTruncated(
                    diagnostics,
                    path,
                    $"{kind} {Num(index + 1)} of {Num(count)} declares {Num(bytes)} bytes at offset {Num(offset)}, outside the {Num(fileLength)}-byte file.");
                return false;
            }

            if (compression == NoCompression && bytes != requiredBytes)
            {
                FailTruncated(
                    diagnostics,
                    path,
                    $"{kind} {Num(index + 1)} of {Num(count)} carries {Num(bytes)} bytes but the layout requires {Num(requiredBytes)}.");
                return false;
            }

            if (bytes > maxEncodedBytes)
            {
                FailLimit(
                    diagnostics,
                    path,
                    $"{kind} {Num(index + 1)} of {Num(count)} declares {Num(bytes)} encoded bytes, over the cap of {Num(maxEncodedBytes)}.");
                return false;
            }

            totalEncodedBytes = checked(totalEncodedBytes + bytes);
            segments.Add((offset, (long)offset + bytes, index));
            return true;
        }

        bool SegmentsAvoidMetadata(string kind, long count)
        {
            regions.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            segments.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            var regionIndex = 0;

            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];

                if (segment.Start >= segment.End)
                {
                    continue;
                }

                while (regionIndex < regions.Count && regions[regionIndex].End <= segment.Start)
                {
                    regionIndex++;
                }

                if (regionIndex < regions.Count && regions[regionIndex].Start < segment.End)
                {
                    var region = regions[regionIndex];
                    FailParse(
                        diagnostics,
                        path,
                        $"{kind} {Num(segment.Index + 1)} of {Num(count)} range [{Num(segment.Start)}, {Num(segment.End)}) overlaps file metadata at [{Num(region.Start)}, {Num(region.End)}).");
                    return false;
                }
            }

            return true;
        }

        var samplesPerRow = planarConfig == PlanarSeparate ? 1 : bands;
        var planeCount = planarConfig == PlanarSeparate ? bands : 1;

        if (hasStripEntries)
        {
            var rowsResult = TryReadScalar(RowsPerStripTag, "RowsPerStrip", out var rowsPerStrip);

            if (rowsResult < 0)
            {
                return false;
            }

            if (rowsResult == 0)
            {
                rowsPerStrip = uint.MaxValue;
            }

            if (rowsPerStrip < 1)
            {
                return FailParse(diagnostics, path, $"TIFF declares RowsPerStrip {Num(rowsPerStrip)}, which is invalid.");
            }

            var stripsPerPlane = checked((height + rowsPerStrip - 1) / rowsPerStrip);
            var expectedSegments = checked(stripsPerPlane * planeCount);

            if (expectedSegments > MaxSegments)
            {
                return FailLimit(
                    diagnostics,
                    path,
                    $"TIFF declares {Num(expectedSegments)} strip segments, over the metadata-derived limit of {Num(MaxSegments)}.");
            }

            if (!TryReadSegmentArray(StripOffsetsTag, "StripOffsets", allowShort: false, expectedSegments, out var stripOffsets))
            {
                return false;
            }

            if (!TryReadSegmentArray(StripByteCountsTag, "StripByteCounts", allowShort: true, expectedSegments, out var stripByteCounts))
            {
                return false;
            }

            var rowBytes = checked((width * samplesPerRow * bits + 7) / 8);

            for (var index = 0; index < expectedSegments; index++)
            {
                var row = index % stripsPerPlane;
                var rows = Math.Min(rowsPerStrip, height - row * rowsPerStrip);

                if (!ValidateSegment("Strip", index, expectedSegments, (uint)stripOffsets[index], (uint)stripByteCounts[index], checked(rows * rowBytes)))
                {
                    return false;
                }
            }
        }
        else
        {
            var tileWidthResult = TryReadScalar(TileWidthTag, "TileWidth", out var tileWidth);

            if (tileWidthResult < 0)
            {
                return false;
            }

            if (tileWidthResult == 0)
            {
                return FailParse(diagnostics, path, "TIFF carries no TileWidth tag.");
            }

            var tileLengthResult = TryReadScalar(TileLengthTag, "TileLength", out var tileLength);

            if (tileLengthResult < 0)
            {
                return false;
            }

            if (tileLengthResult == 0)
            {
                return FailParse(diagnostics, path, "TIFF carries no TileLength tag.");
            }

            if (tileWidth < 16 || tileWidth % 16 != 0 || tileLength < 16 || tileLength % 16 != 0)
            {
                return FailParse(
                    diagnostics,
                    path,
                    $"TIFF tile dimensions {Num(tileWidth)} x {Num(tileLength)} are not positive multiples of 16.");
            }

            if (tileWidth > limits.MaxRasterWidth || tileLength > limits.MaxRasterHeight)
            {
                return FailLimit(
                    diagnostics,
                    path,
                    $"TIFF tile dimensions {Num(tileWidth)} x {Num(tileLength)} exceed the raster dimension limit of {Num(limits.MaxRasterWidth)} x {Num(limits.MaxRasterHeight)}.");
            }

            var tilesAcross = (width + tileWidth - 1) / tileWidth;
            var tilesDown = (height + tileLength - 1) / tileLength;
            var expectedSegments = checked(tilesAcross * tilesDown * planeCount);

            if (expectedSegments > MaxSegments)
            {
                return FailLimit(
                    diagnostics,
                    path,
                    $"TIFF declares {Num(expectedSegments)} tile segments, over the metadata-derived limit of {Num(MaxSegments)}.");
            }

            if (!TryReadSegmentArray(TileOffsetsTag, "TileOffsets", allowShort: false, expectedSegments, out var tileOffsets))
            {
                return false;
            }

            if (!TryReadSegmentArray(TileByteCountsTag, "TileByteCounts", allowShort: true, expectedSegments, out var tileByteCounts))
            {
                return false;
            }

            // Tiles are padded to full tile bounds even at image edges, so an uncompressed
            // tile must carry its full padded byte size, never only the used rows and columns.
            var tileRowBytes = checked((tileWidth * samplesPerRow * bits + 7) / 8);
            var paddedTileBytes = checked(tileLength * tileRowBytes);

            if (paddedTileBytes > limits.MaxRasterDecodedBytes)
            {
                return FailLimit(
                    diagnostics,
                    path,
                    $"TIFF padded tile size of {Num(paddedTileBytes)} bytes exceeds the decoded-byte limit of {Num(limits.MaxRasterDecodedBytes)}.");
            }

            for (var index = 0; index < expectedSegments; index++)
            {
                if (!ValidateSegment("Tile", index, expectedSegments, (uint)tileOffsets[index], (uint)tileByteCounts[index], paddedTileBytes))
                {
                    return false;
                }
            }
        }

        if (totalEncodedBytes > maxEncodedBytes)
        {
            return FailLimit(
                diagnostics,
                path,
                $"TIFF pixel segments declare {Num(totalEncodedBytes)} encoded bytes in total, over the cap of {Num(maxEncodedBytes)}.");
        }

        return hasStripEntries
            ? SegmentsAvoidMetadata("Strip", segments.Count)
            : SegmentsAvoidMetadata("Tile", segments.Count);
    }

    private static byte[] ReadAt(Stream stream, long offset, int size)
    {
        var buffer = new byte[size];
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset, bool littleEndian)
    {
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset));
    }

    private static uint ReadUInt32(byte[] buffer, int offset, bool littleEndian)
    {
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset));
    }

    private static string Num(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool FailParse(GeoDiagnosticBag diagnostics, string path, string message, Exception? detail = null)
    {
        return Fail(diagnostics, ParseFailureCode, GeoStage.Parse, path, message, detail ?? new InvalidDataException(message));
    }

    private static bool FailUnsupported(GeoDiagnosticBag diagnostics, string path, string message)
    {
        return Fail(diagnostics, ParseUnsupportedLayoutCode, GeoStage.Parse, path, message, new InvalidDataException(message));
    }

    private static bool FailTruncated(GeoDiagnosticBag diagnostics, string path, string message)
    {
        return Fail(diagnostics, ParseRasterDataTruncatedCode, GeoStage.Parse, path, message, new InvalidDataException(message));
    }

    private static bool FailLimit(GeoDiagnosticBag diagnostics, string path, string message)
    {
        return Fail(diagnostics, RasterLimitExceededCode, GeoStage.Raster, path, message, detail: null);
    }

    private static bool Fail(
        GeoDiagnosticBag diagnostics,
        string code,
        GeoStage stage,
        string path,
        string message,
        Exception? detail)
    {
        diagnostics.Add(new GeoDiagnostic(code, stage, GeoSeverity.Error, message, path, detail));

        return false;
    }
}
