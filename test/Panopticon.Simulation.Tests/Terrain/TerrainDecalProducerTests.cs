using System.Numerics;
using Panopticon.Client;
using Panopticon.Data.Map;
using Panopticon.FX;
using Panopticon.Terrain.Decals;
using Xunit;

namespace Panopticon.Tests.Terrain;

public sealed class TerrainDecalProducerTests : MockedGameTest
{
    [Fact]
    public void MapScorch_UsesTypedPropertyRatherThanRoadFlagsAndDoesNotDuplicateOnReload()
    {
        var store = new TerrainDecalStore();
        var mapObject = new MapObject(new Vector3(10, 20, 100), 0, RoadType.None, "");
        mapObject.Properties.AddInteger("scorchType", 3);
        mapObject.Properties.AddReal("objectRadius", 24.5f);
        Assert.True(TerrainDecalMapLoader.TryAdd(store, mapObject));
        Assert.True(TerrainDecalMapLoader.TryAdd(store, mapObject));
        var decal = Assert.Single(store.Decals);
        Assert.Equal(mapObject.Position, decal.Position);
        Assert.Equal(24.5f, decal.Radius);
        Assert.Equal(3, decal.Cell);
        store.Clear();
        TerrainDecalMapLoader.TryAdd(store, mapObject);
        Assert.Single(store.Decals);
    }

    [Fact]
    public void OrdinaryObjectAndIncorrectlyTypedProperty_AreNotScorches()
    {
        var store = new TerrainDecalStore();
        var mapObject = new MapObject(Vector3.Zero, 0, RoadType.Angled, "AmericaTankCrusader");
        Assert.False(TerrainDecalMapLoader.TryAdd(store, mapObject));
        mapObject.Properties.AddAsciiString("scorchType", "0");
        Assert.False(TerrainDecalMapLoader.TryAdd(store, mapObject));
        Assert.Empty(store.Decals);
    }

    [Fact]
    public void ClientWithoutTerrain_SkipsScorches()
    {
        using var client = new GameClient(Generals);
        client.AddScorch(Vector3.Zero, 10, TerrainScorchType.Random);
    }
}
