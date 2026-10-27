using System.Linq;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Panopticon.Terrain.Decals;
using Xunit;

namespace Panopticon.Tests.Terrain;

public sealed class TerrainDecalMesherTests
{
    [Fact]
    public void GridAndAtlasUv_MatchOriginalGeometry()
    {
        var map = MapFile.CreateEmpty(8, 8, 0, 12);
        var heights = new HeightMap(map.HeightMapData);
        var decal = new TerrainDecal(TerrainDecalTemplate.Scorch, new Vector3(30, 30, 900), 10, 4);
        var mesh = TerrainDecalMesher.Build(heights, [decal]);
        Assert.Equal(9, mesh.Vertices.Length);
        Assert.Equal(24, mesh.Indices.Length);
        Assert.Equal(new Vector3(20, 20, heights.GetHeight(2, 2) + heights.VerticalScale / 10f), mesh.Vertices[0].Position);
        Assert.Equal(new Vector2(0.375f), mesh.Vertices[0].UV);
        Assert.Equal(new Vector2(0.5f), mesh.Vertices[4].UV);
        Assert.Equal(new Vector2(0.625f), mesh.Vertices[8].UV);
        Assert.Equal(new uint[] { 1, 3, 0, 1, 4, 3 }, mesh.Indices.Take(6));
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
        var older = new TerrainDecal(TerrainDecalTemplate.Scorch, new Vector3(-10, -10, 0), 10, 0);
        var newer = older with { Cell = 1 };
        var mesh = TerrainDecalMesher.Build(heights, [older, newer]);
        Assert.Equal(8, mesh.Vertices.Length);
        Assert.Equal(-10f, mesh.Vertices[0].Position.X);
        Assert.Equal(heights.GetHeight(0, 0) + heights.VerticalScale / 10f, mesh.Vertices[0].Position.Z);
        Assert.Equal(0.5f, mesh.Vertices[0].UV.X);
        Assert.Equal(0.125f, mesh.Vertices[4].UV.X);
        var offMap = older with { Position = new Vector3(-1000, -1000, 0) };
        Assert.Empty(TerrainDecalMesher.Build(heights, [offMap]).Indices);
    }
}
