using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Scripting;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// WI-A5-35: new maps carry the minimal skirmish-playable set (sides from faction templates,
/// one start waypoint per slot) in the engine's waypoint encoding, and it all round-trips
/// through the map format.
/// </summary>
public sealed class NewMapPlayableDefaultsTests : IDisposable
{
    private readonly string _tempRoot = Directory.CreateTempSubdirectory("worldbuilder-playable-").FullName;

    [Fact]
    public void DefaultFactory_ProducesPlayableSidesAndStartWaypoints()
    {
        var map = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = 32,
            PlayableHeight = 32,
            InitialHeight = 10,
        });
        var path = Path.Combine(_tempRoot, "default.map");
        map.SaveAs(path);

        using var stream = File.OpenRead(path);
        var reloaded = MapFile.FromStream(stream);

        Assert.Equal(4, reloaded.SidesList.Players.Count);
        Assert.Equal(string.Empty, reloaded.SidesList.Players[0].Name);
        Assert.Equal("FactionCivilian", reloaded.SidesList.Players[1].Faction);
        Assert.Equal("FactionAmerica", reloaded.SidesList.Players[2].Faction);
        Assert.Equal("FactionGLA", reloaded.SidesList.Players[3].Faction);

        var waypoints = reloaded.ObjectsList.Objects
            .Where(o => o.TypeName == Waypoint.ObjectTypeName)
            .Select(o => new Waypoint(o))
            .ToList();
        Assert.Equal(2, waypoints.Count);
        Assert.Contains(waypoints, w => w.Name == "Player_1_Start" && w.ID == 1);
        Assert.Contains(waypoints, w => w.Name == "Player_2_Start" && w.ID == 2);

        var collection = new WaypointCollection(waypoints, reloaded.WaypointsList.WaypointPaths);
        Assert.True(collection.TryGetPlayerStart(1, out var first));
        Assert.True(collection.TryGetPlayerStart(2, out _));
        Assert.NotEqual(first!.Position, Vector3.Zero);
    }

    [Fact]
    public void CustomFactionTemplates_ProduceMatchingSides()
    {
        var map = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = 32,
            PlayableHeight = 32,
            InitialHeight = 10,
            FactionTemplateNames = ["FactionAmerica", "FactionChina"],
        });
        var path = Path.Combine(_tempRoot, "custom.map");
        map.SaveAs(path);

        using var stream = File.OpenRead(path);
        var reloaded = MapFile.FromStream(stream);

        Assert.Equal(4, reloaded.SidesList.Players.Count);
        Assert.Equal("FactionChina", reloaded.SidesList.Players[3].Faction);
        Assert.Equal(2, reloaded.ObjectsList.Objects.Count(o => o.TypeName == Waypoint.ObjectTypeName));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }
}
