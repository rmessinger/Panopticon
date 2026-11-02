using System;
using System.Globalization;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;

namespace Panopticon.Geospatial.Features;

/// <summary>
/// Bilinear terrain height sampler over generated chunks (B4b deep dive section 5.1a).
/// Each query resolves at most one chunk through the lazy resolver, so callers may stage
/// a bounded chunk cache instead of holding every chunk; the chunk-count constructor also
/// serves payload readers that have counts but no grid. The map rectangle is closed: the
/// east and north edges sample inclusively.
/// </summary>
public sealed class TerrainChunkHeightSampler : ITerrainHeightSampler
{
    private readonly Func<int, int, TerrainChunk?> _resolver;

    private readonly double _widthGameUnits;

    private readonly double _heightGameUnits;

    private readonly int _maxSampleX;

    private readonly int _maxSampleY;

    public TerrainChunkHeightSampler(LocalMapGrid grid, Func<int, int, TerrainChunk?> resolver)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _resolver = Validate(grid.ChunksX, grid.ChunksY, resolver, out _maxSampleX, out _maxSampleY);
        _widthGameUnits = grid.ChunksX * (double)ChunkLayout.ChunkSpanGameUnits;
        _heightGameUnits = grid.ChunksY * (double)ChunkLayout.ChunkSpanGameUnits;
    }

    public TerrainChunkHeightSampler(int chunksX, int chunksY, Func<int, int, TerrainChunk?> resolver)
    {
        _resolver = Validate(chunksX, chunksY, resolver, out _maxSampleX, out _maxSampleY);
        _widthGameUnits = chunksX * (double)ChunkLayout.ChunkSpanGameUnits;
        _heightGameUnits = chunksY * (double)ChunkLayout.ChunkSpanGameUnits;
    }

    public bool TrySample(double gameX, double gameY, out double gameZ)
    {
        gameZ = 0d;

        // The conjunction form also rejects NaN inputs.
        if (!(gameX >= 0d) || !(gameX <= _widthGameUnits) || !(gameY >= 0d) || !(gameY <= _heightGameUnits))
        {
            return false;
        }

        var latticeX = gameX / ChunkLayout.SampleSpacingGameUnits;
        var latticeY = gameY / ChunkLayout.SampleSpacingGameUnits;

        int sampleX = (int)Math.Floor(latticeX);
        int sampleY = (int)Math.Floor(latticeY);
        double weightX;
        double weightY;
        if (sampleX >= _maxSampleX)
        {
            // The inclusive east edge interpolates the last cell to its eastern sample.
            sampleX = _maxSampleX - 1;
            weightX = 1d;
        }
        else
        {
            weightX = latticeX - sampleX;
        }

        if (sampleY >= _maxSampleY)
        {
            sampleY = _maxSampleY - 1;
            weightY = 1d;
        }
        else
        {
            weightY = latticeY - sampleY;
        }

        // The 2x2 stencil always fits one chunk's core: at a seam the floor selects the
        // eastern or northern chunk with weight 0, whose duplicated seam sample equals
        // the neighbour's, so the selection is deterministic and value-identical.
        var chunkX = Math.DivRem(sampleX, ChunkLayout.IntervalsPerChunk, out var localX);
        var chunkY = Math.DivRem(sampleY, ChunkLayout.IntervalsPerChunk, out var localY);
        var chunk = _resolver(chunkX, chunkY);
        if (chunk is null || chunk.ChunkX != chunkX || chunk.ChunkY != chunkY)
        {
            return false;
        }

        var southWest = (double)chunk.GetHeight(localX + 1, localY + 1);
        var southEast = (double)chunk.GetHeight(localX + 2, localY + 1);
        var northWest = (double)chunk.GetHeight(localX + 1, localY + 2);
        var northEast = (double)chunk.GetHeight(localX + 2, localY + 2);

        gameZ = (southWest * (1d - weightX) * (1d - weightY))
            + (southEast * weightX * (1d - weightY))
            + (northWest * (1d - weightX) * weightY)
            + (northEast * weightX * weightY);

        return double.IsFinite(gameZ);
    }

    private static Func<int, int, TerrainChunk?> Validate(
        int chunksX,
        int chunksY,
        Func<int, int, TerrainChunk?>? resolver,
        out int maxSampleX,
        out int maxSampleY)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunksX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunksY, 1);

        // The lattice index arithmetic assumes sample indices fit int32; reject counts
        // that overflow before any query can misaddress or wrap.
        if ((long)chunksX * ChunkLayout.IntervalsPerChunk > int.MaxValue
            || (long)chunksY * ChunkLayout.IntervalsPerChunk > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                $"{nameof(chunksX)}, {nameof(chunksY)}",
                $"{chunksX.ToString(CultureInfo.InvariantCulture)} x {chunksY.ToString(CultureInfo.InvariantCulture)}",
                "The chunk counts overflow the int32 lattice index range.");
        }

        maxSampleX = checked(chunksX * ChunkLayout.IntervalsPerChunk);
        maxSampleY = checked(chunksY * ChunkLayout.IntervalsPerChunk);
        return resolver;
    }
}
