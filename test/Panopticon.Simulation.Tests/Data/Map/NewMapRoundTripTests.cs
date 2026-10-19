using System.IO;
using Panopticon.Data.Map;
using Xunit;

namespace Panopticon.Tests.Data.Map;

/// <summary>
/// WI-A5-28: a new map built by <see cref="MapFile.CreateEmpty"/> writes and reloads through
/// the real serializer with every required chunk intact, blank and with a placed object.
/// </summary>
public sealed class NewMapRoundTripTests
{
    [Fact]
    public void BlankNewMap_SavesAndReloadsWithAllRequiredChunks()
    {
        var map = MapFile.CreateEmpty(playableWidth: 32, playableHeight: 32, borderWidth: 2, initialHeight: 10);

        // WriteTo closes the stream it writes to, so the bytes are buffered for re-reading.
        byte[] written;
        using (var stream = new MemoryStream())
        {
            map.WriteTo(stream);
            written = stream.ToArray();
        }

        var reloaded = MapFile.FromStream(new MemoryStream(written));

        Assert.NotNull(reloaded.HeightMapData);
        Assert.NotNull(reloaded.BlendTileData);
        Assert.NotNull(reloaded.WorldInfo);
        Assert.NotNull(reloaded.SidesList);
        Assert.NotNull(reloaded.ObjectsList);
        Assert.NotNull(reloaded.GlobalLighting);
        Assert.NotNull(reloaded.WaypointsList);

        Assert.Equal((32 + 2 * 2 + 1) * (32 + 2 * 2 + 1), (long)reloaded.HeightMapData.Area);
        Assert.Equal((32 + 2 * 2 + 1) * (32 + 2 * 2 + 1), (long)reloaded.BlendTileData.NumTiles);
        foreach (var elevation in reloaded.HeightMapData.Elevations)
        {
            Assert.Equal(10, elevation);
        }

        // The seeded neutral player survives reload exactly once.
        var neutral = reloaded.SidesList.Players;
        Assert.Single(neutral);
        Assert.Equal(string.Empty, neutral[0].Name);
    }

    [Fact]
    public void NewMapWithOnePlacedObject_SavesAndReloadsTheObject()
    {
        var map = MapFile.CreateEmpty(playableWidth: 16, playableHeight: 16, borderWidth: 1, initialHeight: 5);
        map.ObjectsList.AddObject(new MapObject(new System.Numerics.Vector3(8, 0, 8), 0f, RoadType.None, "AmericaVehicleDozer"));

        byte[] written;
        using (var stream = new MemoryStream())
        {
            map.WriteTo(stream);
            written = stream.ToArray();
        }

        var reloaded = MapFile.FromStream(new MemoryStream(written));

        var objects = reloaded.ObjectsList.Objects;
        Assert.Single(objects);
        Assert.Equal("AmericaVehicleDozer", objects[0].TypeName);
        Assert.Equal(8, objects[0].Position.X);
        Assert.NotNull(objects[0].Properties);
    }
}
