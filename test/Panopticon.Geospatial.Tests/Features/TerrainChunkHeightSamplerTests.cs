using System;
using System.Collections.Generic;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Grid;
using Xunit;

namespace Panopticon.Geospatial.Tests.Features;

public class TerrainChunkHeightSamplerTests
{
    [Fact]
    public void ConstructorThrowsOnNullArguments()
    {
        var grid = FeatureMappingTestSupport.CreateGrid();

        Assert.Throws<ArgumentNullException>(() => new TerrainChunkHeightSampler(null!, static (cx, cy) => null));
        Assert.Throws<ArgumentNullException>(() => new TerrainChunkHeightSampler(grid, null!));
        Assert.Throws<ArgumentNullException>(() => new TerrainChunkHeightSampler(2, 2, null!));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void CountsConstructorRejectsNonPositiveCounts(int chunksX, int chunksY)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TerrainChunkHeightSampler(chunksX, chunksY, static (cx, cy) => FeatureMappingTestSupport.FlatChunk(cx, cy)));
    }

    [Fact]
    public void CountsConstructorRejectsCountsThatOverflowTheInt32Lattice()
    {
        var overflow = (int.MaxValue / ChunkLayout.IntervalsPerChunk) + 1;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TerrainChunkHeightSampler(overflow, 1, static (cx, cy) => FeatureMappingTestSupport.FlatChunk(cx, cy)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TerrainChunkHeightSampler(1, overflow, static (cx, cy) => FeatureMappingTestSupport.FlatChunk(cx, cy)));
    }

    [Fact]
    public void HugeGridEdgeQueryAddressesTheFarChunkWithoutWrapping()
    {
        var chunksX = int.MaxValue / ChunkLayout.IntervalsPerChunk;
        var requested = new List<(int X, int Y)>();
        var sampler = new TerrainChunkHeightSampler(
            chunksX,
            1,
            (cx, cy) =>
            {
                requested.Add((cx, cy));
                return FeatureMappingTestSupport.FlatChunk(cx, cy);
            });

        // The inclusive far edge of the largest legal grid: lattice index 2147483520
        // must clamp, not wrap, and resolve exactly the easternmost chunk.
        var width = chunksX * (double)ChunkLayout.ChunkSpanGameUnits;
        Assert.True(sampler.TrySample(width, 640d, out var gameZ));
        Assert.Equal(5d, gameZ);
        Assert.Equal([(chunksX - 1, 0)], requested);

        requested.Clear();
        Assert.True(sampler.TrySample(0d, 0d, out var origin));
        Assert.Equal(5d, origin);
        Assert.Equal([(0, 0)], requested);
    }

    [Fact]
    public void CountsConstructorMatchesTheGridConstructorBehaviour()
    {
        var grid = FeatureMappingTestSupport.CreateGrid(2, 2);
        var byGrid = new TerrainChunkHeightSampler(grid, static (cx, cy) => FeatureMappingTestSupport.BuildChunk(cx, cy));
        var byCounts = new TerrainChunkHeightSampler(2, 2, static (cx, cy) => FeatureMappingTestSupport.BuildChunk(cx, cy));

        foreach (var (x, y) in new[] { (643.75d, 1287.5d), (1280d, 640d), (2560d, 2560d) })
        {
            Assert.True(byGrid.TrySample(x, y, out var viaGrid));
            Assert.True(byCounts.TrySample(x, y, out var viaCounts));
            Assert.Equal(viaGrid, viaCounts);
            Assert.Equal(FeatureMappingTestSupport.SurfaceHeight(x / 10d, y / 10d), viaCounts);
        }
    }

    [Fact]
    public void FractionalPositionsMatchTheAnalyticBilinearSurfaceExactly()
    {
        var sampler = new TerrainChunkHeightSampler(
            FeatureMappingTestSupport.CreateGrid(),
            static (cx, cy) => FeatureMappingTestSupport.BuildChunk(cx, cy));

        // Positions are multiples of 1.25 game units, so lattice fractions stay dyadic
        // and the expected heights are bit-exact.
        foreach (var (x, y) in new[]
                 {
                     (643.75d, 1287.5d),
                     (1.25d, 2.5d),
                     (1278.75d, 641.25d),
                     (1916.25d, 2043.75d),
                     (10d, 10d),
                 })
        {
            Assert.True(sampler.TrySample(x, y, out var gameZ), $"Sampling ({x}, {y}) failed.");
            Assert.Equal(FeatureMappingTestSupport.SurfaceHeight(x / 10d, y / 10d), gameZ);
        }
    }

    [Fact]
    public void SeamsAndFarCornersSampleInclusively()
    {
        var sampler = new TerrainChunkHeightSampler(
            FeatureMappingTestSupport.CreateGrid(),
            static (cx, cy) => FeatureMappingTestSupport.BuildChunk(cx, cy));

        // 2 x 2 chunks: chunk seams at 1280, the closed map edge at 2560. The east and
        // north edges must be sampleable, and duplicated seam samples must agree.
        foreach (var (x, y) in new[]
                 {
                     (1280d, 640d),
                     (640d, 1280d),
                     (1280d, 1280d),
                     (1280d, 1287.5d),
                     (0d, 0d),
                     (2560d, 0d),
                     (0d, 2560d),
                     (2560d, 2560d),
                     (2560d, 643.75d),
                 })
        {
            Assert.True(sampler.TrySample(x, y, out var gameZ), $"Sampling ({x}, {y}) failed.");
            Assert.Equal(FeatureMappingTestSupport.SurfaceHeight(x / 10d, y / 10d), gameZ);
        }
    }

    [Fact]
    public void ResolverIsLazyAndTouchesOnlyTheChunkEachQueryNeeds()
    {
        var requests = new List<(int X, int Y)>();
        var sampler = new TerrainChunkHeightSampler(
            FeatureMappingTestSupport.CreateGrid(),
            (cx, cy) =>
            {
                requests.Add((cx, cy));
                return FeatureMappingTestSupport.BuildChunk(cx, cy);
            });

        Assert.True(sampler.TrySample(640d, 640d, out _));
        Assert.True(sampler.TrySample(1280d, 640d, out _));
        Assert.True(sampler.TrySample(640d, 1280d, out _));
        Assert.True(sampler.TrySample(2560d, 2560d, out _));

        // Each query resolves exactly one chunk: the seam floor selects the eastern or
        // northern neighbour, never both, and no unrelated chunk is ever requested.
        Assert.Equal(
        [
            (0, 0),
            (1, 0),
            (0, 1),
            (1, 1),
        ], requests);
    }

    [Fact]
    public void MissingChunkFailsTheSampleWithoutRequiringOtherChunks()
    {
        var sampler = new TerrainChunkHeightSampler(
            FeatureMappingTestSupport.CreateGrid(),
            static (cx, cy) => cx == 1 && cy == 0 ? FeatureMappingTestSupport.BuildChunk(cx, cy) : null);

        Assert.True(sampler.TrySample(1500d, 500d, out var gameZ));
        Assert.Equal(FeatureMappingTestSupport.SurfaceHeight(150d, 50d), gameZ);

        Assert.False(sampler.TrySample(100d, 100d, out _));
    }

    [Fact]
    public void ResolverChunkWithMismatchedCoordinatesFailsTheSample()
    {
        var sampler = new TerrainChunkHeightSampler(
            FeatureMappingTestSupport.CreateGrid(),
            static (_, _) => FeatureMappingTestSupport.BuildChunk(0, 0));

        Assert.True(sampler.TrySample(100d, 100d, out _));
        Assert.False(sampler.TrySample(1500d, 500d, out _));
    }

    [Theory]
    [InlineData(-0.1d, 10d)]
    [InlineData(2560.1d, 10d)]
    [InlineData(10d, -0.1d)]
    [InlineData(10d, 2560.1d)]
    [InlineData(double.NaN, 10d)]
    [InlineData(10d, double.NaN)]
    [InlineData(double.PositiveInfinity, 0d)]
    [InlineData(0d, double.NegativeInfinity)]
    public void OutsideOrNonFinitePositionsFailToSample(double gameX, double gameY)
    {
        var sampler = new TerrainChunkHeightSampler(
            FeatureMappingTestSupport.CreateGrid(),
            static (cx, cy) => FeatureMappingTestSupport.BuildChunk(cx, cy));

        Assert.False(sampler.TrySample(gameX, gameY, out var gameZ));
        Assert.Equal(0d, gameZ);
    }

    [Fact]
    public void InclusiveEdgeWorksOnASingleChunkMap()
    {
        var sampler = new TerrainChunkHeightSampler(
            FeatureMappingTestSupport.CreateGrid(1, 1),
            static (cx, cy) => FeatureMappingTestSupport.BuildChunk(cx, cy));

        Assert.True(sampler.TrySample(1280d, 1280d, out var corner));
        Assert.Equal(FeatureMappingTestSupport.SurfaceHeight(128d, 128d), corner);
    }
}
