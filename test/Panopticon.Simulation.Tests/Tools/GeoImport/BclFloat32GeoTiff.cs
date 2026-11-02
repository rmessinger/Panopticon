using System;
using System.Buffers.Binary;
using System.IO;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Minimal classic little-endian GeoTIFF writer for single-band float32 north-up rasters,
/// written with BCL only (no LibTiff or imaging dependency). The card 24 proof fixture uses
/// it so the Simulation.Tests project carries no cross-test-project reference. The emitted
/// structure stays inside what <c>TiffDirectoryPreflight</c> and <c>GeoTiffRasterReader</c>
/// accept: one uncompressed strip, contiguous single band, TOPLEFT orientation (default),
/// ModelPixelScale plus ModelTiepoint georeferencing, and a GeoKey directory. Tag ScaleY is
/// positive, per GeoTIFF 1.1 (OGC 19-008r4) section 7.3.2: model Y decreases as row
/// increases, so row 0 is the northern edge.
/// </summary>
public static class BclFloat32GeoTiff
{
    private const ushort TiffVersion = 42;

    private const ushort TypeShort = 3;

    private const ushort TypeLong = 4;

    private const ushort TypeDouble = 12;

    private const ushort TagImageWidth = 256;

    private const ushort TagImageLength = 257;

    private const ushort TagBitsPerSample = 258;

    private const ushort TagCompression = 259;

    private const ushort TagPhotometric = 262;

    private const ushort TagStripOffsets = 273;

    private const ushort TagSamplesPerPixel = 277;

    private const ushort TagRowsPerStrip = 278;

    private const ushort TagStripByteCounts = 279;

    private const ushort TagPlanarConfig = 284;

    private const ushort TagSampleFormat = 339;

    private const ushort TagModelPixelScale = 33550;

    private const ushort TagModelTiepoint = 33922;

    private const ushort TagGeoKeyDirectory = 34735;

    private const ushort KeyModelType = 1024;

    private const ushort KeyRasterType = 1025;

    private const ushort KeyProjectedCSType = 3072;

    private const ushort KeyVerticalCSType = 4096;

    /// <summary>
    /// Writes <paramref name="samples"/> (row-major, row 0 northernmost) as a single-strip
    /// uncompressed float32 GeoTIFF. The tiepoint anchors raster (0, 0) at the north-west
    /// corner (<paramref name="originEastingWestCorner"/>, <paramref name="originNorthingNorthCorner"/>).
    /// </summary>
    public static void Write(
        string path,
        int width,
        int height,
        ReadOnlySpan<float> samples,
        double originEastingWestCorner,
        double originNorthingNorthCorner,
        double pixelSizeMeters,
        int projectedEpsg,
        int? verticalEpsg = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSizeMeters, double.Epsilon);

        if (samples.Length != width * height)
        {
            throw new ArgumentException(
                $"Samples length {samples.Length} must equal width x height = {width * height}.");
        }

        foreach (var sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                throw new ArgumentException("Samples must be finite float32 values.");
            }
        }

        var geoKeys = BuildGeoKeyDirectory(projectedEpsg, verticalEpsg);
        const int entryCount = 14;

        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("The writer serializes little-endian TIFF fields explicitly.");
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // Header: little-endian byte order, classic version, IFD directly after.
        writer.Write((byte)0x49);
        writer.Write((byte)0x49);
        writer.Write(TiffVersion);
        writer.Write((uint)8);

        var ifdEnd = 8 + 2 + (entryCount * 12) + 4;
        var pixelScaleOffset = ifdEnd;
        var tiepointOffset = pixelScaleOffset + (sizeof(double) * 3);
        var geoKeyOffset = tiepointOffset + (sizeof(double) * 6);
        var pixelDataOffset = geoKeyOffset + (geoKeys.Length * sizeof(ushort));
        var stripByteCount = (uint)(width * height * sizeof(float));

        writer.Write((ushort)entryCount);

        // Directory entries in ascending tag order; values of at most four bytes are inline.
        WriteEntry(writer, TagImageWidth, TypeLong, 1, (uint)width);
        WriteEntry(writer, TagImageLength, TypeLong, 1, (uint)height);
        WriteEntry(writer, TagBitsPerSample, TypeShort, 1, 32);
        WriteEntry(writer, TagCompression, TypeShort, 1, 1);
        WriteEntry(writer, TagPhotometric, TypeShort, 1, 1);
        WriteEntry(writer, TagStripOffsets, TypeLong, 1, (uint)pixelDataOffset);
        WriteEntry(writer, TagSamplesPerPixel, TypeShort, 1, 1);
        WriteEntry(writer, TagRowsPerStrip, TypeLong, 1, (uint)height);
        WriteEntry(writer, TagStripByteCounts, TypeLong, 1, stripByteCount);
        WriteEntry(writer, TagPlanarConfig, TypeShort, 1, 1);
        WriteEntry(writer, TagSampleFormat, TypeShort, 1, 3);
        WriteEntry(writer, TagModelPixelScale, TypeDouble, 3, (uint)pixelScaleOffset);
        WriteEntry(writer, TagModelTiepoint, TypeDouble, 6, (uint)tiepointOffset);
        WriteEntry(writer, TagGeoKeyDirectory, TypeShort, (uint)geoKeys.Length, (uint)geoKeyOffset);
        writer.Write((uint)0);

        foreach (var value in new[] { pixelSizeMeters, pixelSizeMeters, 0d })
        {
            writer.Write(value);
        }

        foreach (var value in new[] { 0d, 0d, 0d, originEastingWestCorner, originNorthingNorthCorner, 0d })
        {
            writer.Write(value);
        }

        foreach (var value in geoKeys)
        {
            writer.Write(value);
        }

        var row = new byte[width * sizeof(float)];
        for (var rowIndex = 0; rowIndex < height; rowIndex++)
        {
            for (var column = 0; column < width; column++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(row.AsSpan(column * sizeof(float)), samples[(rowIndex * width) + column]);
            }

            writer.Write(row);
        }
    }

    private static ushort[] BuildGeoKeyDirectory(int projectedEpsg, int? verticalEpsg)
    {
        // Header: version 1, revision 1, minor revision 1 when the vertical key is present.
        var keys = new System.Collections.Generic.List<ushort> { 1, 1, verticalEpsg is null ? (ushort)0 : (ushort)1, 0 };

        void AddKey(ushort keyId, ushort value)
        {
            keys.Add(keyId);
            keys.Add(0);
            keys.Add(1);
            keys.Add(value);
        }

        AddKey(KeyModelType, 1);
        AddKey(KeyRasterType, 1);
        AddKey(KeyProjectedCSType, checked((ushort)projectedEpsg));

        if (verticalEpsg is { } vertical)
        {
            AddKey(KeyVerticalCSType, checked((ushort)vertical));
        }

        keys[3] = checked((ushort)((keys.Count - 4) / 4));
        return [.. keys];
    }

    private static void WriteEntry(BinaryWriter writer, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(valueOrOffset);
    }
}
