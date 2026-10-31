using System;

namespace Panopticon.Geospatial.Interchange;

/// <summary>
/// Immutable georeferenced raster. Samples are row-major and band-interleaved:
/// sample index = (row * Width + column) * BandCount + band. The constructor
/// copies the sample buffer, so later mutation of the caller's array cannot change the raster.
/// </summary>
public sealed class GeospatialRaster<T>
{
    private readonly T[] _samples;

    public GeospatialRaster(
        int width,
        int height,
        int bandCount,
        AffineTransform transform,
        CoordinateSystemId coordinateSystem,
        ReadOnlyMemory<T> samples,
        T noData = default!,
        bool hasNoData = false,
        RasterSampleFormat sampleFormat = RasterSampleFormat.Unknown,
        string verticalDatum = "unknown")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandCount);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentException.ThrowIfNullOrEmpty(verticalDatum);

        long expectedSampleCount;

        checked
        {
            expectedSampleCount = (long)width * height * bandCount;
        }

        if (expectedSampleCount != samples.Length)
        {
            throw new ArgumentException(
                $"Sample buffer length {samples.Length} does not match width x height x bands "
                + $"= {width} x {height} x {bandCount} = {expectedSampleCount}.",
                nameof(samples));
        }

        Width = width;
        Height = height;
        BandCount = bandCount;
        Transform = transform;
        CoordinateSystem = coordinateSystem;
        HasNoData = hasNoData;
        NoData = noData;
        SampleFormat = sampleFormat;
        VerticalDatum = verticalDatum;
        _samples = samples.ToArray();
        Samples = new ReadOnlyMemory<T>(_samples);
    }

    public int Width { get; }

    public int Height { get; }

    public int BandCount { get; }

    public AffineTransform Transform { get; }

    public CoordinateSystemId CoordinateSystem { get; }

    /// <summary>True when the source declared a no-data sentinel; <see cref="NoData"/> is meaningful only then.</summary>
    public bool HasNoData { get; }

    /// <summary>No-data sentinel in normalized sample units; read only when <see cref="HasNoData"/> is true.</summary>
    public T NoData { get; }

    public RasterSampleFormat SampleFormat { get; }

    /// <summary>Vertical datum of the samples, for example EGM96 or unknown; metadata only, no geoid conversion.</summary>
    public string VerticalDatum { get; }

    public ReadOnlyMemory<T> Samples { get; }

    public T Get(int band, int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(band, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(band, BandCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(row, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Height);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Width);

        return _samples[(row * Width + column) * BandCount + band];
    }
}
