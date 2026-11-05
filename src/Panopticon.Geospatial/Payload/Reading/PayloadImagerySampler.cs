using System;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;

namespace Panopticon.Geospatial.Payload.Reading;

/// <summary>
/// Bilinear imagery sampling over the generated global pixel grid at pixel centers,
/// across chunk seams with outer-boundary clamping; north row 0 of the encoded PNG maps
/// to the southern +Y row indices. All global index arithmetic runs in checked long and
/// double form before any cast; results are CPU bytes only.
/// </summary>
internal sealed class PayloadImagerySampler
{
    private readonly long _totalPixelsX;

    private readonly long _totalPixelsY;

    private readonly int _pixelsPerChunkAxis;

    private readonly double _pixelSpacingGameUnits;

    private readonly double _mapWidthGameUnits;

    private readonly double _mapHeightGameUnits;

    private readonly Func<int, int, GeoDiagnosticBag, CancellationToken, DecodedImagery?> _resolveChunk;

    internal PayloadImagerySampler(
        PayloadGridDefinition grid,
        Func<int, int, GeoDiagnosticBag, CancellationToken, DecodedImagery?> resolveChunk)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(resolveChunk);

        if (grid.ImageryPixelsPerSample is not { } pixelsPerSample)
        {
            throw new ArgumentException("The grid declares no imagery layer.", nameof(grid));
        }

        var side = (long)ChunkLayout.IntervalsPerChunk * pixelsPerSample;
        var squared = side * side;

        if (squared > int.MaxValue / 4L)
        {
            throw new ArgumentOutOfRangeException(nameof(grid), "The imagery chunk exceeds the largest allocatable array.");
        }

        _pixelsPerChunkAxis = checked(ChunkLayout.IntervalsPerChunk * pixelsPerSample);
        _totalPixelsX = checked((long)grid.ChunksX * _pixelsPerChunkAxis);
        _totalPixelsY = checked((long)grid.ChunksY * _pixelsPerChunkAxis);
        _pixelSpacingGameUnits = ChunkLayout.SampleSpacingGameUnits / (double)pixelsPerSample;
        _mapWidthGameUnits = grid.ChunksX * (double)ChunkLayout.ChunkSpanGameUnits;
        _mapHeightGameUnits = grid.ChunksY * (double)ChunkLayout.ChunkSpanGameUnits;
        _resolveChunk = resolveChunk;
    }

    /// <summary>Samples the global pixel grid at game XY; false with a typed diagnostic when the position is outside the map or a chunk read fails.</summary>
    internal bool TrySample(
        double gameX,
        double gameY,
        out RgbaValue value,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        value = default;

        if (!(gameX >= 0d && gameX <= _mapWidthGameUnits && gameY >= 0d && gameY <= _mapHeightGameUnits))
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.SampleOutsideMap,
                $"The imagery sample position ({gameX}, {gameY}) lies outside the map rectangle [0, {_mapWidthGameUnits}] x [0, {_mapHeightGameUnits}].");

            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var column = (gameX / _pixelSpacingGameUnits) - 0.5d;
        var row = (gameY / _pixelSpacingGameUnits) - 0.5d;
        column = Math.Clamp(column, 0d, _totalPixelsX - 1d);
        row = Math.Clamp(row, 0d, _totalPixelsY - 1d);

        var column0 = (long)Math.Floor(column);
        var row0 = (long)Math.Floor(row);
        var column1 = Math.Min(column0 + 1, _totalPixelsX - 1);
        var row1 = Math.Min(row0 + 1, _totalPixelsY - 1);
        var weightColumn = column - column0;
        var weightRow = row - row0;

        if (!TryGetPixel(column0, row0, out var southWest, diagnostics, cancellationToken)
            || !TryGetPixel(column1, row0, out var southEast, diagnostics, cancellationToken)
            || !TryGetPixel(column0, row1, out var northWest, diagnostics, cancellationToken)
            || !TryGetPixel(column1, row1, out var northEast, diagnostics, cancellationToken))
        {
            return false;
        }

        value = new RgbaValue(
            Combine(southWest.R, southEast.R, northWest.R, northEast.R, weightColumn, weightRow),
            Combine(southWest.G, southEast.G, northWest.G, northEast.G, weightColumn, weightRow),
            Combine(southWest.B, southEast.B, northWest.B, northEast.B, weightColumn, weightRow),
            Combine(southWest.A, southEast.A, northWest.A, northEast.A, weightColumn, weightRow));

        return true;
    }

    private bool TryGetPixel(
        long globalColumn,
        long globalRow,
        out Pixel pixel,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        pixel = default;
        var chunkX = Math.DivRem(globalColumn, _pixelsPerChunkAxis, out var localColumn);
        var chunkY = Math.DivRem(globalRow, _pixelsPerChunkAxis, out var localRowSouth);
        var decoded = _resolveChunk((int)chunkX, (int)chunkY, diagnostics, cancellationToken);

        if (decoded is null)
        {
            return false;
        }

        // The encoded PNG stores the north row first; the global row index counts from the south.
        var pngRow = _pixelsPerChunkAxis - 1 - (int)localRowSouth;
        var offset = checked((int)(((long)pngRow * _pixelsPerChunkAxis) + localColumn) * 4);
        var rgba = decoded.Rgba.Span;

        pixel = new Pixel(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);

        return true;
    }

    private static byte Combine(double southWest, double southEast, double northWest, double northEast, double weightColumn, double weightRow)
    {
        var value = (southWest * (1d - weightColumn) * (1d - weightRow))
            + (southEast * weightColumn * (1d - weightRow))
            + (northWest * (1d - weightColumn) * weightRow)
            + (northEast * weightColumn * weightRow);

        return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    }

    private readonly record struct Pixel(byte R, byte G, byte B, byte A);
}
