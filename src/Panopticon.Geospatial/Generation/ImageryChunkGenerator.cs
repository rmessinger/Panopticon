using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Sampling;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Generates imagery chunks by inverse-warping each output pixel center into the imagery
/// raster once; every stored channel is resampled with the Auto kernel (bilinear
/// upsampling, area-weighted decimation). Pixels tile the chunk's 128-interval rectangle
/// with no apron, north row first, and global integer indices are computed before any
/// double position so geometry is stable across chunks. Only uint8 RGB(A) sources are
/// accepted: 3-band sources emit opaque alpha, 4-band sources store straight alpha.
/// </summary>
public sealed class ImageryChunkGenerator
{
    private const byte OpaqueAlpha = 255;

    private readonly RasterSampler _sampler;

    private readonly ICoordinateTransformer _transformer;

    public ImageryChunkGenerator(ICoordinateTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);

        _transformer = transformer;
        _sampler = new RasterSampler(transformer);
    }

    /// <summary>
    /// Generates one chunk as an immutable RGBA8 PNG (north row first, 128 *
    /// <paramref name="imageryPixelsPerSample"/> pixels per axis) plus the SHA-256 of the
    /// encoded bytes. Failures add typed diagnostics and return null; fill events count on
    /// <paramref name="stats"/> only. Dimensions and byte budget are checked against
    /// <paramref name="limits"/> before allocation; cancellation is checked per row and
    /// before encoding.
    /// </summary>
    public ImageryChunk? Generate(
        LocalMapGrid grid,
        GeospatialRaster<double> imagery,
        SamplingOptions policy,
        int cx,
        int cy,
        int imageryPixelsPerSample = 10,
        SamplingStats? stats = null,
        GeoDiagnosticBag? diagnostics = null,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(imagery);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfLessThan(cx, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cx, grid.ChunksX);
        ArgumentOutOfRangeException.ThrowIfLessThan(cy, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cy, grid.ChunksY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageryPixelsPerSample);

        diagnostics ??= new GeoDiagnosticBag();

        if (imagery.SampleFormat != RasterSampleFormat.UInt8 || imagery.BandCount is not (3 or 4))
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.RasterFailure,
                GeoStage.Raster,
                GeoSeverity.Error,
                $"Chunk ({cx}, {cy}): the imagery source must be a uint8 RGB(A) raster; found "
                + $"{imagery.SampleFormat} samples in {imagery.BandCount} bands."));

            return null;
        }

        limits ??= new GeospatialImportLimits();

        int pixels;
        long rgbaBytes;
        int firstGlobalX;
        int firstGlobalY;
        int lastGlobalY;

        try
        {
            pixels = checked(ChunkLayout.IntervalsPerChunk * imageryPixelsPerSample);
            rgbaBytes = checked((long)pixels * pixels * 4L);
            firstGlobalX = checked(cx * ChunkLayout.IntervalsPerChunk * imageryPixelsPerSample);
            firstGlobalY = checked(cy * ChunkLayout.IntervalsPerChunk * imageryPixelsPerSample);
            _ = checked(firstGlobalX + pixels - 1);
            lastGlobalY = checked(firstGlobalY + pixels - 1);
        }
        catch (OverflowException ex)
        {
            return Fail(
                $"Chunk ({cx}, {cy}): imageryPixelsPerSample {imageryPixelsPerSample} overflows the chunk's global "
                + "output pixel indices; nothing was allocated.",
                ex);
        }

        if (pixels > limits.MaxRasterWidth || pixels > limits.MaxRasterHeight)
        {
            return Fail(
                $"Chunk ({cx}, {cy}): imageryPixelsPerSample {imageryPixelsPerSample} produces {pixels} output "
                + $"pixels per axis, beyond the raster dimension limits {limits.MaxRasterWidth} x {limits.MaxRasterHeight}.");
        }

        if (rgbaBytes > limits.MaxRasterDecodedBytes)
        {
            return Fail(
                $"Chunk ({cx}, {cy}): the {rgbaBytes}-byte RGBA output buffer exceeds the maxRasterDecodedBytes "
                + $"budget of {limits.MaxRasterDecodedBytes} bytes.");
        }

        if (rgbaBytes > int.MaxValue)
        {
            return Fail(
                $"Chunk ({cx}, {cy}): the {rgbaBytes}-byte RGBA output buffer exceeds the largest allocatable array.");
        }

        var mapTransform = grid.Transform;
        var origin = mapTransform.OriginEastingNorthing;
        var pixelSpacingMeters = (ChunkLayout.SampleSpacingGameUnits * mapTransform.MetersPerGameUnit) / imageryPixelsPerSample;
        var halfSpacingMeters = pixelSpacingMeters / 2d;
        var target = grid.ProjectedCoordinateSystem;
        var source = imagery.CoordinateSystem;
        var bandCount = imagery.BandCount;
        var rgba = new byte[(int)rgbaBytes];
        Span<byte> channels = stackalloc byte[4];

        for (var py = 0; py < pixels; py++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var globalY = lastGlobalY - py;
            var centerY = origin.Y + ((globalY + 0.5d) * pixelSpacingMeters);
            var rowOffset = py * pixels * 4;

            for (var px = 0; px < pixels; px++)
            {
                var globalX = firstGlobalX + px;
                var centerX = origin.X + ((globalX + 0.5d) * pixelSpacingMeters);

                Vector2d center;

                try
                {
                    center = _transformer.Transform(new Vector2d(centerX, centerY), target, source);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
                {
                    return Fail(
                        $"Chunk ({cx}, {cy}): inverse transform of output pixel (global {globalX}, {globalY}) from the "
                        + "target CRS to the imagery raster CRS failed.",
                        ex);
                }

                if (!double.IsFinite(center.X) || !double.IsFinite(center.Y))
                {
                    return Fail(
                        $"Chunk ({cx}, {cy}): inverse transform of output pixel (global {globalX}, {globalY}) produced "
                        + "a non-finite raster coordinate.");
                }

                var footprint = new SampleFootprint(
                    new ProjectedBounds(
                        centerX - halfSpacingMeters,
                        centerY - halfSpacingMeters,
                        centerX + halfSpacingMeters,
                        centerY + halfSpacingMeters),
                    target);

                for (var band = 0; band < bandCount; band++)
                {
                    var result = _sampler.Sample(imagery, band, center.X, center.Y, footprint, SampleKernel.Auto, policy, stats);

                    if (!result.IsSampled)
                    {
                        foreach (var diagnostic in result.Diagnostics)
                        {
                            diagnostics.Add(diagnostic);
                        }

                        return Fail(
                            $"Chunk ({cx}, {cy}): output pixel (global {globalX}, {globalY}) band {band} failed "
                            + "against the imagery raster.");
                    }

                    var value = result.Value;

                    if (!double.IsFinite(value) || value < 0d || value > 255d)
                    {
                        diagnostics.Add(new GeoDiagnostic(
                            GeoDiagnosticCodes.RasterFailure,
                            GeoStage.Raster,
                            GeoSeverity.Error,
                            $"Chunk ({cx}, {cy}): output pixel (global {globalX}, {globalY}) band {band} sampled "
                            + $"{Format(value)}, outside the uint8 channel range 0..255; the interchange payload is "
                            + "not a valid uint8 imagery source."));

                        return null;
                    }

                    channels[band] = ToByte(value);
                }

                var offset = rowOffset + (px * 4);
                rgba[offset] = channels[0];
                rgba[offset + 1] = channels[1];
                rgba[offset + 2] = channels[2];
                rgba[offset + 3] = bandCount == 4 ? channels[3] : OpaqueAlpha;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var image = new Image<Rgba32>(pixels, pixels);

        image.ProcessPixelRows(accessor => CopyRows(rgba, pixels, accessor));

        using var stream = new MemoryStream();
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

        return new ImageryChunk(cx, cy, pixels, stream.ToArray());

        ImageryChunk? Fail(string message, Exception? detail = null)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.GenerateFailure,
                GeoStage.Generate,
                GeoSeverity.Error,
                message,
                detail: detail));

            return null;
        }
    }

    /// <summary>
    /// Lazily streams every chunk southwest row-major without retaining earlier chunks; a
    /// rejected chunk adds its typed error to <paramref name="diagnostics"/> and ends the
    /// enumeration.
    /// </summary>
    public IEnumerable<ImageryChunk> GenerateAll(
        LocalMapGrid grid,
        GeospatialRaster<double> imagery,
        SamplingOptions policy,
        int imageryPixelsPerSample = 10,
        SamplingStats? stats = null,
        GeoDiagnosticBag? diagnostics = null,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(imagery);
        ArgumentNullException.ThrowIfNull(policy);

        return GenerateAllCore(grid, imagery, policy, imageryPixelsPerSample, stats, diagnostics, limits, cancellationToken);
    }

    private IEnumerable<ImageryChunk> GenerateAllCore(
        LocalMapGrid grid,
        GeospatialRaster<double> imagery,
        SamplingOptions policy,
        int imageryPixelsPerSample,
        SamplingStats? stats,
        GeoDiagnosticBag? diagnostics,
        GeospatialImportLimits? limits,
        CancellationToken cancellationToken)
    {
        for (var cy = 0; cy < grid.ChunksY; cy++)
        {
            for (var cx = 0; cx < grid.ChunksX; cx++)
            {
                var chunk = Generate(grid, imagery, policy, cx, cy, imageryPixelsPerSample, stats, diagnostics, limits, cancellationToken);

                if (chunk is null)
                {
                    yield break;
                }

                yield return chunk;
            }
        }
    }

    private static void CopyRows(byte[] rgba, int pixels, PixelAccessor<Rgba32> accessor)
    {
        for (var py = 0; py < pixels; py++)
        {
            var row = accessor.GetRowSpan(py);
            var rowOffset = py * pixels * 4;

            for (var px = 0; px < pixels; px++)
            {
                var offset = rowOffset + (px * 4);
                row[px] = new Rgba32(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
            }
        }
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(Math.Clamp(value, 0d, 255d), MidpointRounding.AwayFromZero);
    }

    private static string Format(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
