using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Panopticon.Terrain.Decals;
using Xunit;

namespace Panopticon.Tests.Terrain;

public sealed class TerrainDecalMesherTests
{
    // Fixture distances are authored in legacy units; Scale converts them to runtime meters.
    private const float Scale = (float)UnitConversionPolicy.GeneralsFeetToMeters;

    [Fact]
    public void GridAndAtlasUv_MatchOriginalGeometry()
    {
        var map = MapFile.CreateEmpty(8, 8, 0, 12);
        var heights = new HeightMap(map.HeightMapData);
        var decal = new TerrainDecal(TerrainDecalTemplate.Scorch, new Vector3(30 * Scale, 30 * Scale, 900 * Scale), 10 * Scale, 4);
        var mesh = TerrainDecalMesher.Build(heights, [decal]);
        Assert.Equal(9, mesh.Vertices.Length);
        Assert.Equal(24, mesh.Indices.Length);
        Assert.Equal(new Vector3(20 * Scale, 20 * Scale, heights.GetHeight(2, 2) + heights.VerticalScale / 10f), mesh.Vertices[0].Position);
        Assert.Equal(new Vector2(0.375f), mesh.Vertices[0].UV);
        Assert.Equal(new Vector2(0.5f), mesh.Vertices[4].UV);
        Assert.Equal(new Vector2(0.625f), mesh.Vertices[8].UV);
        Assert.Equal(new uint[] { 1, 3, 0, 1, 4, 3 }, mesh.Indices.Take(6));
    }

    [Fact]
    public void TileAlignedRadius_CoversSameTilesAsBaseLayout()
    {
        var heights = new HeightMap(MapFile.CreateEmpty(16, 16, 0, 0).HeightMapData);
        var decal = new TerrainDecal(TerrainDecalTemplate.Scorch, new Vector3(100 * Scale, 100 * Scale, 0), 30 * Scale, 0);
        var mesh = TerrainDecalMesher.Build(heights, [decal]);
        const int minTile = 7;
        const int maxTile = 13;
        const int tilesPerSide = maxTile - minTile + 1;
        Assert.Equal(tilesPerSide * tilesPerSide, mesh.Vertices.Length);
        Assert.Equal((tilesPerSide - 1) * (tilesPerSide - 1) * 6, mesh.Indices.Length);
        Assert.Equal(heights.GetPosition(minTile, minTile).X, mesh.Vertices[0].Position.X);
        Assert.Equal(heights.GetPosition(minTile, minTile).Y, mesh.Vertices[0].Position.Y);
        Assert.Equal(heights.GetPosition(maxTile, maxTile).X, mesh.Vertices[^1].Position.X);
        Assert.Equal(heights.GetPosition(maxTile, maxTile).Y, mesh.Vertices[^1].Position.Y);
    }

    [Fact]
    public void UnflippedTriangles_UseOppositeDiagonal()
    {
        var heights = new HeightMap(MapFile.CreateEmpty(8, 8, 0, 0).HeightMapData);
        var decal = new TerrainDecal(TerrainDecalTemplate.Scorch, new Vector3(25, 25, 0), 5, 0);
        var mesh = TerrainDecalMesher.Build(heights, [decal], (_, _) => false);
        Assert.Equal(4, mesh.Vertices.Length);
        Assert.Equal(new uint[] { 0, 3, 2, 0, 1, 3 }, mesh.Indices);
    }

    [Fact]
    public void BorderClippingSlopeAndNewestFirst_ArePreserved()
    {
        var map = MapFile.CreateEmpty(8, 8, 1, 0);
        map.HeightMapData.Elevations[0, 0] = 10;
        var heights = new HeightMap(map.HeightMapData);
        var older = new TerrainDecal(TerrainDecalTemplate.Scorch, new Vector3(-10 * Scale, -10 * Scale, 0), 10 * Scale, 0);
        var newer = older with { Cell = 1 };
        var mesh = TerrainDecalMesher.Build(heights, [older, newer]);
        Assert.Equal(8, mesh.Vertices.Length);
        Assert.Equal(-10 * Scale, mesh.Vertices[0].Position.X);
        Assert.Equal(heights.GetHeight(0, 0) + heights.VerticalScale / 10f, mesh.Vertices[0].Position.Z);
        Assert.Equal(0.5f, mesh.Vertices[0].UV.X);
        Assert.Equal(0.125f, mesh.Vertices[4].UV.X);
        var offMap = older with { Position = new Vector3(-1000 * Scale, -1000 * Scale, 0) };
        Assert.Empty(TerrainDecalMesher.Build(heights, [offMap]).Indices);
    }
}
