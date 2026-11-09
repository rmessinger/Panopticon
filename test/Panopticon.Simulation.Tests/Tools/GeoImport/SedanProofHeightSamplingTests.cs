using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Tools.GeoImport;

public sealed class SedanProofHeightSamplingTests
{
    // Metric cutover scale: runtime lattice coordinates and heights are in meters.
    private const float S = (float)UnitConversionPolicy.GeneralsFeetToMeters;
    private const float Quantum = 0.0390625f * S;

    [Fact]
    public void SampleContinuousEngineHeight_CellCentreAndNonHalfFractional_MatchesBilinearAndDiffersFromIntegerTileLookup()
    {
        var heightMap = BuildQuadHeightMap();

        var f00 = 457 * Quantum;
        var f10 = 463 * Quantum;
        var f01 = 463 * Quantum;
        var f11 = 465 * Quantum;

        var tile = heightMap.GetTilePosition(new Vector2(15f * S, 25f * S))!.Value;
        var swNodeHeight = heightMap.GetHeight(tile.X, tile.Y);
        Assert.Equal(f00, swNodeHeight);

        var cellCentre = 0.25f * (f00 + f10 + f01 + f11);
        Assert.Equal(cellCentre, SedanProofHeightSampling.SampleContinuousEngineHeight(heightMap, 15.0 * S, 25.0 * S));
        Assert.Equal(0.1953125f * S, cellCentre - swNodeHeight, 1e-5f);

        const float wx = 0.25f;
        const float wy = 0.75f;
        var expectedNonHalf = ((f10 * wx + f00 * (1f - wx)) * (1f - wy)) + ((f11 * wx + f01 * (1f - wx)) * wy);
        var sampledNonHalf = SedanProofHeightSampling.SampleContinuousEngineHeight(heightMap, 12.5 * S, 27.5 * S);

        Assert.Equal(expectedNonHalf, sampledNonHalf);
        Assert.NotEqual(swNodeHeight, sampledNonHalf);
    }

    [Fact]
    public void SampleContinuousEngineHeight_IntegerLatticeNode_CoincidesWithIntegerNodeLookup()
    {
        var heightMap = BuildQuadHeightMap();

        var tile = heightMap.GetTilePosition(new Vector2(10f * S, 20f * S))!.Value;

        Assert.Equal(heightMap.GetHeight(tile.X, tile.Y), SedanProofHeightSampling.SampleContinuousEngineHeight(heightMap, 10.0 * S, 20.0 * S));
    }

    [Fact]
    public void SampleContinuousEngineHeight_NonZeroBorderWidth_TranslatesWorldCoordinatesByBorderWidth()
    {
        var elevations = BuildElevations(7);
        SetQuad(elevations, 1, 2, 900, 910, 920, 930);
        SetQuad(elevations, 3, 4, 457, 463, 463, 465);
        var heightMap = new HeightMap(HeightMapData.Create16Bit(2, elevations));

        var expected = 0.25f * (457 + 463 + 463 + 465) * Quantum;

        Assert.Equal(expected, SedanProofHeightSampling.SampleContinuousEngineHeight(heightMap, 15.0 * S, 25.0 * S));
    }

    [Theory]
    [InlineData(-10.0, 5.0)]
    [InlineData(5.0, -10.0)]
    [InlineData(50.0, 5.0)]
    [InlineData(5.0, 50.0)]
    public void SampleContinuousEngineHeight_OutOfBounds_ReturnsNull(double engineX, double engineY)
    {
        var heightMap = BuildQuadHeightMap();

        Assert.Null(SedanProofHeightSampling.SampleContinuousEngineHeight(heightMap, engineX * S, engineY * S));
    }

    [Theory]
    [InlineData(3, 3, 0d, 0d, 1920d, 1920d)]
    [InlineData(2, 1, 1280d, 0d, 0d, 640d)]
    public void PlayableCentreEngineXy_WholeMapAndCropped_ReturnsExpectedEngineCentre(
        int chunksX,
        int chunksY,
        double cropOriginGameX,
        double cropOriginGameY,
        double expectedX,
        double expectedY)
    {
        var (aimX, aimY) = SedanProofHeightSampling.PlayableCentreEngineXy(chunksX, chunksY, cropOriginGameX, cropOriginGameY);

        Assert.Equal(expectedX, aimX);
        Assert.Equal(expectedY, aimY);
    }

    private static HeightMap BuildQuadHeightMap()
    {
        var elevations = BuildElevations(5);
        SetQuad(elevations, 1, 2, 457, 463, 463, 465);

        return new HeightMap(HeightMapData.Create16Bit(0, elevations));
    }

    private static ushort[,] BuildElevations(int size)
    {
        var elevations = new ushort[size, size];

        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                elevations[x, y] = (ushort)(400 + (13 * x) + (29 * y));
            }
        }

        return elevations;
    }

    private static void SetQuad(ushort[,] elevations, int x, int y, ushort q00, ushort q10, ushort q01, ushort q11)
    {
        elevations[x, y] = q00;
        elevations[x + 1, y] = q10;
        elevations[x, y + 1] = q01;
        elevations[x + 1, y + 1] = q11;
    }
}
