using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace Panopticon.Geospatial.Payload.Storage;

/// <summary>
/// Encoding, decoding, and seam validation for standalone payload chunk files
/// (B4b deep dive section 6.1). Data failures add a typed diagnostic and return null or
/// false; cancellation and programming-argument errors intentionally propagate. No
/// whole-grid state is loaded.
/// </summary>
internal static class PayloadChunkCodec
{
    private const int TerrainChunkBytes =
        ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis * sizeof(float);

    private const int CoreChunkBytes =
        ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis;

    /// <summary>Shared terrain overlap samples per seam: previous stored 128..130 vs next 0..2.</summary>
    private const int TerrainSeamSamples = (2 * ChunkLayout.ApronSamplesPerSide) + 1;

    private const byte DefinedMaskBits = (byte)(TerrainMaskFlags.Impassable | TerrainMaskFlags.Unbuildable);

    private const int PngSignatureBytes = 8;

    private const int PngChunkHeaderBytes = 8;

    private const int PngCrcBytes = 4;

    private const int PngIhdrDataBytes = 13;

    private const int MaxPngChunkCount = 4096;

    /// <summary>
    /// Cumulative ancillary byte bound. The exact-length allowlist keeps ancillary bytes
    /// far below this; the check remains as a backstop if the allowlist is ever widened.
    /// </summary>
    private const long MaxPngAncillaryChunkBytes = 16L * 1024 * 1024;

    /// <summary>Minimum concatenated IDAT bytes: the 2-byte zlib header plus the 4-byte Adler-32 trailer.</summary>
    private const int MinimumZlibStreamBytes = 6;

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static readonly DecoderOptions DecodeOptions = new() { SkipMetadata = true };

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Serializes one terrain chunk as explicit little-endian float32 bytes, independent of host order.</summary>
    internal static byte[] EncodeTerrain(TerrainChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        cancellationToken.ThrowIfCancellationRequested();

        var heights = chunk.Heights.Span;
        var encoded = new byte[TerrainChunkBytes];
        var offset = 0;

        for (var row = 0; row < ChunkLayout.StoredPointsPerChunkAxis; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < ChunkLayout.StoredPointsPerChunkAxis; x++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(encoded.AsSpan(offset), heights[(row * ChunkLayout.StoredPointsPerChunkAxis) + x]);
                offset += sizeof(float);
            }
        }

        return encoded;
    }

    /// <summary>
    /// Decodes one terrain chunk from its fixed 68644-byte little-endian float32 buffer.
    /// Returns null with a diagnostic when the length is wrong or any height is not
    /// finite; cancellation is checked before decoding and per row.
    /// </summary>
    internal static TerrainChunk? DecodeTerrain(
        int chunkX,
        int chunkY,
        ReadOnlyMemory<byte> encoded,
        GeoDiagnosticBag? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkX);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkY);
        diagnostics ??= new GeoDiagnosticBag();
        cancellationToken.ThrowIfCancellationRequested();

        if (encoded.Length != TerrainChunkBytes)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.ChunkLengthInvalid,
                $"Chunk ({chunkX}, {chunkY}): the terrain buffer must hold exactly {TerrainChunkBytes} bytes; found {encoded.Length}.");
            return null;
        }

        var span = encoded.Span;
        var heights = new float[ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis];
        var sample = 0;

        for (var row = 0; row < ChunkLayout.StoredPointsPerChunkAxis; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < ChunkLayout.StoredPointsPerChunkAxis; x++)
            {
                var height = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(sample * sizeof(float)));

                if (!float.IsFinite(height))
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.TerrainHeightNotFinite,
                        $"Chunk ({chunkX}, {chunkY}): the terrain height at stored position ({x}, {row}) is {Format(height)}, not a finite float32 value.");
                    return null;
                }

                heights[sample++] = height;
            }
        }

        // The constructor re-checks finiteness, copies defensively, and hashes the decoded samples.
        return new TerrainChunk(chunkX, chunkY, heights);
    }

    /// <summary>Serializes one material chunk's palette index bytes.</summary>
    internal static byte[] EncodeMaterial(MaterialChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        cancellationToken.ThrowIfCancellationRequested();

        var indices = chunk.Indices.Span;
        var encoded = new byte[CoreChunkBytes];
        var offset = 0;

        for (var row = 0; row < ChunkLayout.PointsPerChunkAxis; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            indices.Slice(row * ChunkLayout.PointsPerChunkAxis, ChunkLayout.PointsPerChunkAxis).CopyTo(encoded.AsSpan(offset));
            offset += ChunkLayout.PointsPerChunkAxis;
        }

        return encoded;
    }

    /// <summary>
    /// Decodes one material chunk from its fixed 16641-byte index buffer against the
    /// payload palette. Returns null with a diagnostic when the length is wrong or any
    /// index is outside the palette; cancellation is checked before decoding and per row.
    /// </summary>
    internal static MaterialChunk? DecodeMaterial(
        int chunkX,
        int chunkY,
        ReadOnlyMemory<byte> encoded,
        MaterialPalette palette,
        GeoDiagnosticBag? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkX);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkY);
        ArgumentNullException.ThrowIfNull(palette);
        diagnostics ??= new GeoDiagnosticBag();
        cancellationToken.ThrowIfCancellationRequested();

        if (encoded.Length != CoreChunkBytes)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.ChunkLengthInvalid,
                $"Chunk ({chunkX}, {chunkY}): the material buffer must hold exactly {CoreChunkBytes} bytes; found {encoded.Length}.");
            return null;
        }

        var indices = encoded.ToArray();

        for (var row = 0; row < ChunkLayout.PointsPerChunkAxis; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < ChunkLayout.PointsPerChunkAxis; x++)
            {
                var position = (row * ChunkLayout.PointsPerChunkAxis) + x;
                var index = indices[position];

                if (index >= palette.Count)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.MaterialIndexOutOfPalette,
                        $"Chunk ({chunkX}, {chunkY}): the material index {index} at core position ({x}, {row}) does not address an entry of the {palette.Count}-entry palette.");
                    return null;
                }
            }
        }

        // The constructor re-checks the bounds, copies defensively, and hashes the indices.
        return new MaterialChunk(chunkX, chunkY, palette, indices);
    }

    /// <summary>Serializes one terrain mask chunk's flag bytes.</summary>
    internal static byte[] EncodeMask(TerrainMaskChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        cancellationToken.ThrowIfCancellationRequested();

        var flags = chunk.Flags.Span;
        var encoded = new byte[CoreChunkBytes];
        var offset = 0;

        for (var row = 0; row < ChunkLayout.PointsPerChunkAxis; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            flags.Slice(row * ChunkLayout.PointsPerChunkAxis, ChunkLayout.PointsPerChunkAxis).CopyTo(encoded.AsSpan(offset));
            offset += ChunkLayout.PointsPerChunkAxis;
        }

        return encoded;
    }

    /// <summary>
    /// Decodes one terrain mask chunk from its fixed 16641-byte flag buffer. Returns null
    /// with a diagnostic when the length is wrong or any byte carries undefined bits;
    /// cancellation is checked before decoding and per row.
    /// </summary>
    internal static TerrainMaskChunk? DecodeMask(
        int chunkX,
        int chunkY,
        ReadOnlyMemory<byte> encoded,
        GeoDiagnosticBag? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkX);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkY);
        diagnostics ??= new GeoDiagnosticBag();
        cancellationToken.ThrowIfCancellationRequested();

        if (encoded.Length != CoreChunkBytes)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.ChunkLengthInvalid,
                $"Chunk ({chunkX}, {chunkY}): the mask buffer must hold exactly {CoreChunkBytes} bytes; found {encoded.Length}.");
            return null;
        }

        var flags = encoded.ToArray();

        for (var row = 0; row < ChunkLayout.PointsPerChunkAxis; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < ChunkLayout.PointsPerChunkAxis; x++)
            {
                var position = (row * ChunkLayout.PointsPerChunkAxis) + x;
                var flag = flags[position];

                if ((flag & ~DefinedMaskBits) != 0)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.MaskFlagsUndefined,
                        $"Chunk ({chunkX}, {chunkY}): the mask flag byte {flag} at core position ({x}, {row}) is not a defined flag combination.");
                    return null;
                }
            }
        }

        // The constructor re-checks the bits, copies defensively, and hashes the flags.
        return new TerrainMaskChunk(chunkX, chunkY, flags);
    }

    /// <summary>
    /// Validates the three-sample overlap between horizontally adjacent terrain chunks:
    /// west stored columns 128..130 must equal east stored columns 0..2 bitwise, so a
    /// negative zero in one chunk still mismatches a positive zero in the other. Returns
    /// false with a diagnostic naming the first differing sample.
    /// </summary>
    internal static bool ValidateTerrainHorizontalSeam(TerrainChunk west, TerrainChunk east, GeoDiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(west);
        ArgumentNullException.ThrowIfNull(east);
        ValidateHorizontalAdjacency(west.ChunkX, west.ChunkY, east.ChunkX, east.ChunkY);
        diagnostics ??= new GeoDiagnosticBag();

        return CompareTerrainOverlap(west, east, vertical: false, diagnostics);
    }

    /// <summary>Vertical-neighbour variant of <see cref="ValidateTerrainHorizontalSeam"/>: south stored rows 128..130 vs north stored rows 0..2, bitwise.</summary>
    internal static bool ValidateTerrainVerticalSeam(TerrainChunk south, TerrainChunk north, GeoDiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(south);
        ArgumentNullException.ThrowIfNull(north);
        ValidateVerticalAdjacency(south.ChunkX, south.ChunkY, north.ChunkX, north.ChunkY);
        diagnostics ??= new GeoDiagnosticBag();

        return CompareTerrainOverlap(south, north, vertical: true, diagnostics);
    }

    /// <summary>
    /// Validates the shared core edge between horizontally adjacent material chunks:
    /// west core column 128 must equal east core column 0 for every row. Returns false
    /// with a diagnostic naming the first differing sample.
    /// </summary>
    internal static bool ValidateMaterialHorizontalSeam(MaterialChunk west, MaterialChunk east, GeoDiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(west);
        ArgumentNullException.ThrowIfNull(east);
        ValidateHorizontalAdjacency(west.ChunkX, west.ChunkY, east.ChunkX, east.ChunkY);
        diagnostics ??= new GeoDiagnosticBag();

        return CompareCoreEdges(
            diagnostics,
            vertical: false,
            west.ChunkX,
            west.ChunkY,
            east.ChunkX,
            east.ChunkY,
            (x, y) => west.GetIndex(x, y),
            (x, y) => east.GetIndex(x, y),
            "material");
    }

    /// <summary>Vertical-neighbour variant of <see cref="ValidateMaterialHorizontalSeam"/>: south core row 128 vs north core row 0.</summary>
    internal static bool ValidateMaterialVerticalSeam(MaterialChunk south, MaterialChunk north, GeoDiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(south);
        ArgumentNullException.ThrowIfNull(north);
        ValidateVerticalAdjacency(south.ChunkX, south.ChunkY, north.ChunkX, north.ChunkY);
        diagnostics ??= new GeoDiagnosticBag();

        return CompareCoreEdges(
            diagnostics,
            vertical: true,
            south.ChunkX,
            south.ChunkY,
            north.ChunkX,
            north.ChunkY,
            (x, y) => south.GetIndex(x, y),
            (x, y) => north.GetIndex(x, y),
            "material");
    }

    /// <summary>
    /// Validates the shared core edge between horizontally adjacent mask chunks: west
    /// core column 128 must equal east core column 0 for every row. Returns false with a
    /// diagnostic naming the first differing sample.
    /// </summary>
    internal static bool ValidateMaskHorizontalSeam(TerrainMaskChunk west, TerrainMaskChunk east, GeoDiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(west);
        ArgumentNullException.ThrowIfNull(east);
        ValidateHorizontalAdjacency(west.ChunkX, west.ChunkY, east.ChunkX, east.ChunkY);
        diagnostics ??= new GeoDiagnosticBag();

        return CompareCoreEdges(
            diagnostics,
            vertical: false,
            west.ChunkX,
            west.ChunkY,
            east.ChunkX,
            east.ChunkY,
            (x, y) => (int)west.GetFlags(x, y),
            (x, y) => (int)east.GetFlags(x, y),
            "mask");
    }

    /// <summary>Vertical-neighbour variant of <see cref="ValidateMaskHorizontalSeam"/>: south core row 128 vs north core row 0.</summary>
    internal static bool ValidateMaskVerticalSeam(TerrainMaskChunk south, TerrainMaskChunk north, GeoDiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(south);
        ArgumentNullException.ThrowIfNull(north);
        ValidateVerticalAdjacency(south.ChunkX, south.ChunkY, north.ChunkX, north.ChunkY);
        diagnostics ??= new GeoDiagnosticBag();

        return CompareCoreEdges(
            diagnostics,
            vertical: true,
            south.ChunkX,
            south.ChunkY,
            north.ChunkX,
            north.ChunkY,
            (x, y) => (int)south.GetFlags(x, y),
            (x, y) => (int)north.GetFlags(x, y),
            "mask");
    }

    /// <summary>
    /// Decodes one imagery chunk's PNG into a CPU RGBA buffer. Structure, checksum,
    /// dimension, and budget validation runs over the raw chunk bytes before any
    /// ImageSharp allocation; the concatenated IDAT stream is then inflated independently
    /// and bounded, verifying the exact scanline count, per-row filter bytes, and that no
    /// excess data follows, so truncated or over-long streams are rejected even where the
    /// decoder alone would return a partially filled image. The returned result keeps the
    /// original chunk so the encoded PNG bytes are never re-encoded. Returns null with a
    /// diagnostic on any recoverable failure. Cancellation is checked before preflight,
    /// per chunk, per CRC block, per inflated row, and per copied row.
    /// </summary>
    internal static DecodedImagery? DecodeImagery(
        ImageryChunk chunk,
        int expectedPixelsPerAxis,
        GeospatialImportLimits? limits = null,
        GeoDiagnosticBag? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedPixelsPerAxis);
        limits ??= new GeospatialImportLimits();
        diagnostics ??= new GeoDiagnosticBag();
        cancellationToken.ThrowIfCancellationRequested();

        if (chunk.PixelsPerAxis != expectedPixelsPerAxis)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngFormatMismatch,
                $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): the chunk declares {chunk.PixelsPerAxis} pixels per axis but {expectedPixelsPerAxis} were expected.");
            return null;
        }

        var png = chunk.Png.Span;

        if (!TryPreflightPng(png, expectedPixelsPerAxis, chunk.ChunkX, chunk.ChunkY, limits, diagnostics, cancellationToken, out var width, out var height, out var idatSegments))
        {
            return null;
        }

        var rgbaBytes = checked((long)width * height * 4L);

        // The budget bounds an individual decoded buffer, not the whole decode process:
        // the decoder's image and the copied RGBA output coexist during the row copy,
        // each rgbaBytes in size.
        if (rgbaBytes > limits.MaxRasterDecodedBytes)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngBudgetExceeded,
                $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): decoding {rgbaBytes} RGBA bytes exceeds the maxRasterDecodedBytes budget of {limits.MaxRasterDecodedBytes} bytes.");
            return null;
        }

        if (rgbaBytes > int.MaxValue)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngBudgetExceeded,
                $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): the {rgbaBytes}-byte RGBA buffer exceeds the largest allocatable array.");
            return null;
        }

        // ImageSharp accepts a zlib stream that ends before the last scanline and returns
        // an image whose trailing rows were never filled, so the scanline stream is
        // verified independently and bounded before the decoder runs.
        if (!TryInflateRawScanlines(chunk.Png, idatSegments, width, height, chunk.ChunkX, chunk.ChunkY, diagnostics, cancellationToken))
        {
            return null;
        }

        Image<Rgba32> image;

        try
        {
            // SkipMetadata keeps the decoder from buffering ancillary chunks even if the
            // preflight allowlist were widened later.
            image = Image.Load<Rgba32>(DecodeOptions, png);
        }
        catch (Exception ex) when (ex is ImageFormatException or InvalidImageContentException or InvalidDataException)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngDecodeFailed,
                $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): the PNG image stream failed to decode: {ex.Message}",
                ex);
            return null;
        }

        using (image)
        {
            if (image.Frames.Count != 1)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngStructureInvalid,
                    $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): the PNG carries {image.Frames.Count} frames; exactly one still image is required.");
                return null;
            }

            if (image.Width != expectedPixelsPerAxis || image.Height != expectedPixelsPerAxis)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngFormatMismatch,
                    $"Chunk ({chunk.ChunkX}, {chunk.ChunkY}): the decoded image is {image.Width} x {image.Height} pixels but {expectedPixelsPerAxis} x {expectedPixelsPerAxis} were expected.");
                return null;
            }

            var rgba = new byte[rgbaBytes];
            image.ProcessPixelRows(accessor => CopyRows(accessor, rgba, width, cancellationToken));

            return DecodedImagery.Adopt(chunk, rgba);
        }
    }

    private static void CopyRows(PixelAccessor<Rgba32> accessor, byte[] rgba, int pixelsPerAxis, CancellationToken cancellationToken)
    {
        for (var y = 0; y < pixelsPerAxis; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
            row.CopyTo(rgba.AsSpan(y * pixelsPerAxis * 4));
        }
    }

    /// <summary>
    /// Walks the raw PNG chunk structure and validates everything decidable without
    /// decoding pixels: signature, chunk framing, the CRC-32 of every chunk, chunk count,
    /// cumulative ancillary bytes, the fixed-encoder chunk allowlist (animation chunks,
    /// compressed metadata chunks, and any other or unknown ancillary chunk are rejected
    /// before ImageSharp can inflate them), IHDR-first and IEND-last ordering, consecutive
    /// IDAT runs, no trailing bytes, and the IHDR fields pinning the expected square RGBA8
    /// no-interlace format. Runs entirely before any ImageSharp allocation.
    /// </summary>
    private static bool TryPreflightPng(
        ReadOnlySpan<byte> png,
        int expectedPixelsPerAxis,
        int chunkX,
        int chunkY,
        GeospatialImportLimits limits,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken,
        out int width,
        out int height,
        out (int Offset, int Length)[] idatSegments)
    {
        width = 0;
        height = 0;
        idatSegments = [];

        if (png.Length < PngSignatureBytes || !png[..PngSignatureBytes].SequenceEqual(PngSignature))
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngStructureInvalid,
                $"Chunk ({chunkX}, {chunkY}): the imagery payload does not start with the PNG signature.");
            return false;
        }

        var offset = PngSignatureBytes;
        var chunkCount = 0;
        long ancillaryBytes = 0;
        var ihdrSeen = false;
        var idatSeen = false;
        var idatRunClosed = false;
        var iendSeen = false;
        var segments = new List<(int Offset, int Length)>();

        while (offset < png.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (iendSeen)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngStructureInvalid,
                    $"Chunk ({chunkX}, {chunkY}): {png.Length - offset} bytes of garbage follow IEND.");
                return false;
            }

            var remaining = png.Length - offset;

            if (remaining < PngChunkHeaderBytes + PngCrcBytes)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngStructureInvalid,
                    $"Chunk ({chunkX}, {chunkY}): the PNG ends inside a chunk header; {remaining} bytes remain at offset {offset}.");
                return false;
            }

            var dataLength = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset));

            if (dataLength > int.MaxValue)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngStructureInvalid,
                    $"Chunk ({chunkX}, {chunkY}): the chunk at offset {offset} declares {dataLength} data bytes, beyond any allocatable chunk.");
                return false;
            }

            var type = png.Slice(offset + 4, 4);

            if (!IsChunkTypeText(type))
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngStructureInvalid,
                    $"Chunk ({chunkX}, {chunkY}): the chunk type at offset {offset} is not four ASCII letters.");
                return false;
            }

            var totalChunkBytes = PngChunkHeaderBytes + dataLength + PngCrcBytes;

            if (remaining < totalChunkBytes)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngStructureInvalid,
                    $"Chunk ({chunkX}, {chunkY}): the chunk at offset {offset} declares {dataLength} data bytes but only {remaining} remain in the file.");
                return false;
            }

            var dataStart = offset + PngChunkHeaderBytes;
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(dataStart + (int)dataLength));

            if (ComputeCrc32(png.Slice(offset + 4, (int)(4L + dataLength)), cancellationToken) != storedCrc)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngChecksumInvalid,
                    $"Chunk ({chunkX}, {chunkY}): the CRC of chunk '{TypeText(type)}' at offset {offset} does not match its type and data bytes.");
                return false;
            }

            chunkCount++;

            if (chunkCount > MaxPngChunkCount)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngBudgetExceeded,
                    $"Chunk ({chunkX}, {chunkY}): the PNG holds more than {MaxPngChunkCount} chunks.");
                return false;
            }

            if (!ihdrSeen && !type.SequenceEqual("IHDR"u8))
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngStructureInvalid,
                    $"Chunk ({chunkX}, {chunkY}): the first PNG chunk must be IHDR; found '{TypeText(type)}'.");
                return false;
            }

            if ((type[0] & 0x20) != 0)
            {
                if (!TryGetAncillaryByteLimit(type, out var exactLength))
                {
                    if (IsAnimationChunk(type))
                    {
                        Fail(
                            diagnostics,
                            PayloadChunkDiagnosticCodes.PngStructureInvalid,
                            $"Chunk ({chunkX}, {chunkY}): the animation chunk '{TypeText(type)}' is not allowed; still images only.");
                        return false;
                    }

                    if (IsCompressedMetadataChunk(type))
                    {
                        Fail(
                            diagnostics,
                            PayloadChunkDiagnosticCodes.PngStructureInvalid,
                            $"Chunk ({chunkX}, {chunkY}): the compressed metadata chunk '{TypeText(type)}' at offset {offset} is not allowed; its inflated size is not bounded by the encoded bytes, and the payload accepts only the fixed encoder's PNGs.");
                        return false;
                    }

                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.PngStructureInvalid,
                        $"Chunk ({chunkX}, {chunkY}): the ancillary chunk '{TypeText(type)}' at offset {offset} is not in the fixed encoder's chunk set.");
                    return false;
                }

                if (dataLength != exactLength)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.PngStructureInvalid,
                        $"Chunk ({chunkX}, {chunkY}): the '{TypeText(type)}' chunk must hold exactly {exactLength} data bytes; found {dataLength}.");
                    return false;
                }

                ancillaryBytes += dataLength;

                if (ancillaryBytes > MaxPngAncillaryChunkBytes)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.PngBudgetExceeded,
                        $"Chunk ({chunkX}, {chunkY}): the PNG's ancillary chunks cumulatively hold {ancillaryBytes} bytes, beyond the {MaxPngAncillaryChunkBytes}-byte bound.");
                    return false;
                }

                if (idatSeen)
                {
                    idatRunClosed = true;
                }
            }
            else if (type.SequenceEqual("IHDR"u8))
            {
                if (ihdrSeen)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.PngStructureInvalid,
                        $"Chunk ({chunkX}, {chunkY}): the PNG carries more than one IHDR chunk.");
                    return false;
                }

                if (dataLength != PngIhdrDataBytes)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.PngStructureInvalid,
                        $"Chunk ({chunkX}, {chunkY}): the IHDR chunk must hold {PngIhdrDataBytes} data bytes; found {dataLength}.");
                    return false;
                }

                if (!TryValidateIhdr(png.Slice(dataStart, PngIhdrDataBytes), expectedPixelsPerAxis, limits, chunkX, chunkY, diagnostics, out width, out height))
                {
                    return false;
                }

                ihdrSeen = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (idatRunClosed)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.PngStructureInvalid,
                        $"Chunk ({chunkX}, {chunkY}): the IDAT chunk at offset {offset} does not directly follow the earlier IDAT run.");
                    return false;
                }

                idatSeen = true;
                segments.Add((dataStart, (int)dataLength));
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (dataLength != 0)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.PngStructureInvalid,
                        $"Chunk ({chunkX}, {chunkY}): the IEND chunk must hold no data bytes; found {dataLength}.");
                    return false;
                }

                iendSeen = true;
            }
            else
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngStructureInvalid,
                    $"Chunk ({chunkX}, {chunkY}): the critical chunk '{TypeText(type)}' at offset {offset} is not allowed; a palette also contradicts the required RGBA color type.");
                return false;
            }

            offset += (int)totalChunkBytes;
        }

        if (!ihdrSeen || !idatSeen || !iendSeen)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngStructureInvalid,
                $"Chunk ({chunkX}, {chunkY}): the PNG is truncated; it must carry IHDR, at least one IDAT, and IEND.");
            return false;
        }

        idatSegments = segments.ToArray();
        return true;
    }

    /// <summary>
    /// Inflates the concatenated IDAT stream independently of ImageSharp and verifies the
    /// exact raw scanline payload: height rows of a filter byte (0..4) plus width * 4 RGBA
    /// bytes, nothing more. The zlib header is validated explicitly (deflate method,
    /// window at most 32 KiB, FCHECK divisible by 31, no preset dictionary), the stream
    /// must hold at least the header plus a 4-byte Adler-32 trailer, and the Adler-32
    /// computed over every inflated scanline byte, filter bytes included, must match the
    /// last four concatenated IDAT bytes: .NET's ZLibStream returns every inflated byte
    /// without touching a truncated trailer, so the row count alone cannot reject one.
    /// Reads are bounded to one row at a time plus a single excess probe byte, so an
    /// inflation bomb appended behind valid data is rejected without being inflated; the
    /// IDAT data is streamed in place, never concatenated into a second buffer.
    /// Cancellation is checked per row.
    /// </summary>
    private static bool TryInflateRawScanlines(
        ReadOnlyMemory<byte> png,
        (int Offset, int Length)[] idatSegments,
        int width,
        int height,
        int chunkX,
        int chunkY,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        // Checked arithmetic first; only then are buffers created, and the only buffer is
        // a single scanline.
        var rowBytes = checked((long)width * 4L + 1L);
        var expectedRawBytes = checked(height * rowBytes);

        if (rowBytes > int.MaxValue)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngBudgetExceeded,
                $"Chunk ({chunkX}, {chunkY}): one scanline of {width} RGBA8 pixels needs {rowBytes} bytes, beyond the largest allocatable array.");
            return false;
        }

        long totalIdatBytes = 0;

        foreach (var (_, length) in idatSegments)
        {
            totalIdatBytes = checked(totalIdatBytes + length);
        }

        if (totalIdatBytes < MinimumZlibStreamBytes)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngDecodeFailed,
                $"Chunk ({chunkX}, {chunkY}): the concatenated IDAT data holds {totalIdatBytes} bytes; at least {MinimumZlibStreamBytes} are required for the zlib header and Adler-32 trailer.");
            return false;
        }

        var cmf = IdatByteAt(png, idatSegments, 0);
        var flg = IdatByteAt(png, idatSegments, 1);

        if ((cmf & 0x0F) != 8 || (cmf >> 4) > 7 || (((cmf * 256) + flg) % 31) != 0 || (flg & 0x20) != 0)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngDecodeFailed,
                $"Chunk ({chunkX}, {chunkY}): the IDAT zlib header CMF 0x{cmf:X2} FLG 0x{flg:X2} is not a preset-dictionary-free deflate header with a window of at most 32 KiB and a valid FCHECK.");
            return false;
        }

        var expectedAdler =
            (uint)((IdatByteAt(png, idatSegments, totalIdatBytes - 4) << 24)
                | (IdatByteAt(png, idatSegments, totalIdatBytes - 3) << 16)
                | (IdatByteAt(png, idatSegments, totalIdatBytes - 2) << 8)
                | IdatByteAt(png, idatSegments, totalIdatBytes - 1));

        var row = new byte[(int)rowBytes];
        uint adlerA = 1;
        uint adlerB = 0;

        try
        {
            using var source = new ConcatenatedIdatStream(png, idatSegments);

            using var zlib = new ZLibStream(source, CompressionMode.Decompress);

            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                zlib.ReadAtLeast(row, row.Length, throwOnEndOfStream: true);

                if (row[0] > 4)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.PngDecodeFailed,
                        $"Chunk ({chunkX}, {chunkY}): the inflated scanline {y} declares filter method {row[0]}; only PNG filters 0 through 4 exist.");
                    return false;
                }

                UpdateAdler32(row, ref adlerA, ref adlerB);
            }

            // Exactly one probe byte: anything further is excess data (or a bomb) and is
            // rejected without inflating any more of the stream.
            var probe = new byte[1];

            if (zlib.Read(probe, 0, 1) != 0)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.PngDecodeFailed,
                    $"Chunk ({chunkX}, {chunkY}): the IDAT stream inflates to more raw image data than the {expectedRawBytes} bytes the {height} rows require.");
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngDecodeFailed,
                $"Chunk ({chunkX}, {chunkY}): the IDAT stream is not a complete zlib encoding of {expectedRawBytes} raw bytes: {ex.Message}",
                ex);
            return false;
        }

        var computedAdler = (adlerB << 16) | adlerA;

        if (computedAdler != expectedAdler)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngDecodeFailed,
                $"Chunk ({chunkX}, {chunkY}): the zlib Adler-32 trailer 0x{expectedAdler:X8} does not match the Adler-32 of the inflated scanlines 0x{computedAdler:X8}; the stream is corrupt or its trailer is truncated.");
            return false;
        }

        return true;
    }

    /// <summary>Returns the byte at <paramref name="index"/> of the concatenated IDAT data, read in place.</summary>
    private static byte IdatByteAt(ReadOnlyMemory<byte> png, (int Offset, int Length)[] idatSegments, long index)
    {
        foreach (var (offset, length) in idatSegments)
        {
            if (index < length)
            {
                return png.Span[offset + (int)index];
            }

            index -= length;
        }

        throw new ArgumentOutOfRangeException(nameof(index), index, "The index lies beyond the concatenated IDAT data.");
    }

    /// <summary>
    /// Adler-32 update using zlib's NMAX blocking: 5552 bytes is the largest block that
    /// cannot overflow 32 bits before the 65521 modulus is applied.
    /// </summary>
    private static void UpdateAdler32(ReadOnlySpan<byte> data, ref uint a, ref uint b)
    {
        const int maxBlock = 5552;
        var index = 0;

        while (index < data.Length)
        {
            var blockEnd = index + Math.Min(maxBlock, data.Length - index);

            while (index < blockEnd)
            {
                a += data[index];
                b += a;
                index++;
            }

            a %= 65521;
            b %= 65521;
        }
    }

    /// <summary>
    /// Read-only sequential stream over the already CRC-verified IDAT data ranges of one
    /// PNG, avoiding any concatenated copy of the compressed bytes.
    /// </summary>
    private sealed class ConcatenatedIdatStream : Stream
    {
        private readonly ReadOnlyMemory<byte> _png;

        private readonly (int Offset, int Length)[] _segments;

        private int _segmentIndex;

        private int _segmentOffset;

        public ConcatenatedIdatStream(ReadOnlyMemory<byte> png, (int Offset, int Length)[] segments)
        {
            _png = png;
            _segments = segments;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var total = 0;

            while (total < buffer.Length && _segmentIndex < _segments.Length)
            {
                var (segmentOffset, segmentLength) = _segments[_segmentIndex];
                var remainingInSegment = segmentLength - _segmentOffset;
                var chunk = Math.Min(buffer.Length - total, remainingInSegment);
                _png.Span.Slice(segmentOffset + _segmentOffset, chunk).CopyTo(buffer[total..]);
                total += chunk;
                _segmentOffset += chunk;

                if (_segmentOffset == segmentLength)
                {
                    _segmentIndex++;
                    _segmentOffset = 0;
                }
            }

            return total;
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private static bool TryValidateIhdr(
        ReadOnlySpan<byte> header,
        int expectedPixelsPerAxis,
        GeospatialImportLimits limits,
        int chunkX,
        int chunkY,
        GeoDiagnosticBag diagnostics,
        out int width,
        out int height)
    {
        width = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4));
        var bitDepth = header[8];
        var colorType = header[9];
        var compression = header[10];
        var filterMethod = header[11];
        var interlace = header[12];

        if (width == 0 || height == 0)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngFormatMismatch,
                $"Chunk ({chunkX}, {chunkY}): the IHDR declares a {width} x {height} image; both axes must be positive.");
            return false;
        }

        if (width != expectedPixelsPerAxis || height != expectedPixelsPerAxis)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngFormatMismatch,
                $"Chunk ({chunkX}, {chunkY}): the IHDR declares {width} x {height} pixels but the fixed imagery format is {expectedPixelsPerAxis} x {expectedPixelsPerAxis}.");
            return false;
        }

        if (bitDepth != 8 || colorType != 6)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngFormatMismatch,
                $"Chunk ({chunkX}, {chunkY}): the IHDR declares bit depth {bitDepth} and color type {colorType}; the fixed imagery format is 8-bit RGBA, color type 6, with no palette.");
            return false;
        }

        if (compression != 0 || filterMethod != 0)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngFormatMismatch,
                $"Chunk ({chunkX}, {chunkY}): the IHDR declares compression method {compression} and filter method {filterMethod}; only the PNG-defined value 0 is supported.");
            return false;
        }

        if (interlace != 0)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngFormatMismatch,
                $"Chunk ({chunkX}, {chunkY}): the IHDR declares interlace method {interlace}; the fixed imagery format is non-interlaced.");
            return false;
        }

        if (width > limits.MaxRasterWidth || height > limits.MaxRasterHeight)
        {
            Fail(
                diagnostics,
                PayloadChunkDiagnosticCodes.PngBudgetExceeded,
                $"Chunk ({chunkX}, {chunkY}): the IHDR declares {width} x {height} pixels, beyond the {limits.MaxRasterWidth} x {limits.MaxRasterHeight} raster dimension limits.");
            return false;
        }

        return true;
    }

    private static bool CompareTerrainOverlap(TerrainChunk previous, TerrainChunk next, bool vertical, GeoDiagnosticBag diagnostics)
    {
        var axis = ChunkLayout.StoredPointsPerChunkAxis;
        var overlapStart = axis - TerrainSeamSamples;

        for (var cross = 0; cross < axis; cross++)
        {
            for (var sample = 0; sample < TerrainSeamSamples; sample++)
            {
                var previousValue = vertical ? previous.GetHeight(cross, overlapStart + sample) : previous.GetHeight(overlapStart + sample, cross);
                var nextValue = vertical ? next.GetHeight(cross, sample) : next.GetHeight(sample, cross);
                var previousBits = BitConverter.SingleToInt32Bits(previousValue);
                var nextBits = BitConverter.SingleToInt32Bits(nextValue);

                if (previousBits != nextBits)
                {
                    Fail(
                        diagnostics,
                        PayloadChunkDiagnosticCodes.ChunkSeamMismatch,
                        $"Chunks ({previous.ChunkX}, {previous.ChunkY}) and ({next.ChunkX}, {next.ChunkY}) disagree at the {(vertical ? "vertical" : "horizontal")} terrain seam: sample {sample} of {TerrainSeamSamples}, {(vertical ? "column" : "row")} {cross}: {Format(previousValue)} ({previousBits:X8}) vs {Format(nextValue)} ({nextBits:X8}).");
                    return false;
                }
            }
        }

        return true;
    }

    private static bool CompareCoreEdges(
        GeoDiagnosticBag diagnostics,
        bool vertical,
        int previousX,
        int previousY,
        int nextX,
        int nextY,
        Func<int, int, int> previousValue,
        Func<int, int, int> nextValue,
        string layer)
    {
        var axis = ChunkLayout.PointsPerChunkAxis;
        var edge = axis - 1;

        for (var cross = 0; cross < axis; cross++)
        {
            var previous = vertical ? previousValue(cross, edge) : previousValue(edge, cross);
            var next = vertical ? nextValue(cross, 0) : nextValue(0, cross);

            if (previous != next)
            {
                Fail(
                    diagnostics,
                    PayloadChunkDiagnosticCodes.ChunkSeamMismatch,
                    $"Chunks ({previousX}, {previousY}) and ({nextX}, {nextY}) disagree at the {(vertical ? "vertical" : "horizontal")} {layer} seam: {(vertical ? "column" : "row")} {cross}: {previous} vs {next}.");
                return false;
            }
        }

        return true;
    }

    private static void ValidateHorizontalAdjacency(int westX, int westY, int eastX, int eastY)
    {
        if (eastX != westX + 1 || eastY != westY)
        {
            throw new ArgumentException($"Horizontal seam chunks must be east neighbours: ({westX}, {westY}) and ({eastX}, {eastY}).");
        }
    }

    private static void ValidateVerticalAdjacency(int southX, int southY, int northX, int northY)
    {
        if (northY != southY + 1 || northX != southX)
        {
            throw new ArgumentException($"Vertical seam chunks must be north neighbours: ({southX}, {southY}) and ({northX}, {northY}).");
        }
    }

    /// <summary>
    /// The bounded ancillary set the fixed generator encoder emits, each pinned to its
    /// exact data length. Compressed metadata chunks (iCCP, zTXt, iTXt), other standard
    /// metadata chunks, and unknown ancillary chunks are rejected before ImageSharp sees
    /// the file; their inflated size is not bounded by the encoded byte length.
    /// </summary>
    private static bool TryGetAncillaryByteLimit(ReadOnlySpan<byte> type, out uint exactLength)
    {
        if (type.SequenceEqual("pHYs"u8))
        {
            exactLength = 9;
            return true;
        }

        if (type.SequenceEqual("gAMA"u8))
        {
            exactLength = 4;
            return true;
        }

        if (type.SequenceEqual("sRGB"u8))
        {
            exactLength = 1;
            return true;
        }

        exactLength = 0;
        return false;
    }

    private static bool IsCompressedMetadataChunk(ReadOnlySpan<byte> type)
    {
        return type.SequenceEqual("iCCP"u8) || type.SequenceEqual("zTXt"u8) || type.SequenceEqual("iTXt"u8);
    }

    private static bool IsAnimationChunk(ReadOnlySpan<byte> type)
    {
        return type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8);
    }

    private static bool IsChunkTypeText(ReadOnlySpan<byte> type)
    {
        foreach (var letter in type)
        {
            if (letter < (byte)'A' || (letter > (byte)'Z' && letter < (byte)'a') || letter > (byte)'z')
            {
                return false;
            }
        }

        return true;
    }

    private static string TypeText(ReadOnlySpan<byte> type)
    {
        return System.Text.Encoding.ASCII.GetString(type);
    }

    private static void Fail(GeoDiagnosticBag diagnostics, string code, string message, Exception? detail = null)
    {
        diagnostics.Add(new GeoDiagnostic(code, GeoStage.Validate, GeoSeverity.Error, message, detail: detail));
    }

    private static string Format(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];

        for (var n = 0; n < 256; n++)
        {
            var crc = (uint)n;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }

            table[n] = crc;
        }

        return table;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        var crc = 0xFFFFFFFFu;
        var index = 0;

        while (index < data.Length)
        {
            // One cancellation check per bounded 64 KiB block: a multi-hundred-MiB chunk
            // must not loop uncancellable, and per-byte checks would add pure overhead.
            var blockEnd = Math.Min(index + (64 * 1024), data.Length);

            while (index < blockEnd)
            {
                crc = Crc32Table[(crc ^ data[index]) & 0xFF] ^ (crc >> 8);
                index++;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        return crc ^ 0xFFFFFFFFu;
    }
}

/// <summary>
/// One decoded imagery chunk: the original chunk (PNG bytes never re-encoded) plus its
/// decoded RGBA8 pixels, row-major from the north row. The visible constructor copies
/// the buffer; only the codec's private ownership-taking path adopts without copying.
/// </summary>
internal sealed class DecodedImagery
{
    private readonly byte[] _rgba;

    /// <summary>Stores a private copy of <paramref name="rgba"/>; external callers can never mutate the stored pixels.</summary>
    public DecodedImagery(ImageryChunk chunk, byte[] rgba)
    {
        ValidateBuffer(chunk, rgba);
        Chunk = chunk;
        _rgba = (byte[])rgba.Clone();
        Rgba = new ReadOnlyMemory<byte>(_rgba);
    }

    /// <summary>Ownership-taking path for the trusted decode pipeline only; the buffer must be freshly allocated and never handed out.</summary>
    private DecodedImagery(ImageryChunk chunk, byte[] rgba, Ownership ownership)
    {
        ValidateBuffer(chunk, rgba);
        Chunk = chunk;
        _rgba = rgba;
        Rgba = new ReadOnlyMemory<byte>(_rgba);
    }

    internal static DecodedImagery Adopt(ImageryChunk chunk, byte[] rgba)
    {
        return new DecodedImagery(chunk, rgba, Ownership.Adopted);
    }

    /// <summary>The original chunk; its PNG bytes are the exact encoded source.</summary>
    public ImageryChunk Chunk { get; }

    public int PixelsPerAxis => Chunk.PixelsPerAxis;
    /// <summary>Decoded RGBA8 bytes, row-major from the north row; a private copy owned by this instance.</summary>
    public ReadOnlyMemory<byte> Rgba { get; }

    private static void ValidateBuffer(ImageryChunk chunk, byte[]? rgba)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunk.PixelsPerAxis);

        var expected = (long)chunk.PixelsPerAxis * chunk.PixelsPerAxis * 4L;

        if (rgba.Length != expected)
        {
            throw new ArgumentException($"The RGBA buffer must hold {expected} bytes; found {rgba.Length}.", nameof(rgba));
        }
    }

    private enum Ownership
    {
        Adopted,
    }
}
