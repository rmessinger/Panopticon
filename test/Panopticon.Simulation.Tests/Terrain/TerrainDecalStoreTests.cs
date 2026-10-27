using System.Numerics;
using Panopticon.Terrain.Decals;
using Xunit;

namespace Panopticon.Tests.Terrain;

public sealed class TerrainDecalStoreTests
{
    [Fact]
    public void Add_EvictsOldestAndTracksChanges()
    {
        var store = new TerrainDecalStore(2);
        Assert.True(store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, 10, 0));
        Assert.True(store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, 10, 1));
        Assert.Equal(2, store.Version);
        Assert.True(store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, 10, 2));
        Assert.Collection(store.Decals, decal => Assert.Equal(1, decal.Cell), decal => Assert.Equal(2, decal.Cell));
        Assert.Equal(4, store.Version);
        store.Limit = 1;
        Assert.Equal(2, Assert.Single(store.Decals).Cell);
        store.Clear();
        Assert.Empty(store.Decals);
        Assert.Equal(6, store.Version);
        store.Clear();
        Assert.Equal(6, store.Version);
    }

    [Fact]
    public void DuplicateRule_UsesStrictAxisAndRadiusTolerancePerTemplateAndCell()
    {
        var store = new TerrainDecalStore();
        store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, 20, 0);
        Assert.False(store.Add(TerrainDecalTemplate.Scorch, new Vector3(4, 4, 100), 20, 0));
        Assert.Equal(1, store.Version);
        Assert.True(store.Add(TerrainDecalTemplate.Scorch, new Vector3(5, 0, 0), 20, 0));
        Assert.True(store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, 20, 1));
        Assert.True(store.Add(new TerrainDecalTemplate("other.tga", 3, 1.5f), Vector3.Zero, 20, 0));
        Assert.False(store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, 24, 0));
    }

    [Fact]
    public void FullStore_EvictsBeforeRejectingDuplicateLikeOriginal()
    {
        var store = new TerrainDecalStore(2);
        store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, 10, 0);
        store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, 10, 1);
        Assert.False(store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, 10, 1));
        Assert.Equal(1, Assert.Single(store.Decals).Cell);
        Assert.Equal(3, store.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidRadius_DoesNotChangeStore(float radius)
    {
        var store = new TerrainDecalStore();
        Assert.False(store.Add(TerrainDecalTemplate.Scorch, Vector3.Zero, radius, 0));
        Assert.Equal(0, store.Version);
    }
}
