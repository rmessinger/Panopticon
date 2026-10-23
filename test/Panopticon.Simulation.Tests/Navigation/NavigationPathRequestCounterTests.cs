using System;
using System.IO;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

/// <summary>
/// WI-M1-02: the engine path-request counter. It must count every CalculatePath entry
/// monotonically (pollers derive per-tick deltas from it) and must not perturb pathfinding.
/// </summary>
// Serialized with the other Navigation classes: these tests assert deltas of the
// process-global Navigation.PathRequestCount, which parallel test classes would race.
[Collection("Navigation")]
public class NavigationPathRequestCounterTests
{
    [Fact]
    public void CalculatePath_IncrementsTheRequestCounter()
    {
        var (navigation, _, start, end) = CreateNavigation();

        var before = NavigationClass.PathRequestCount;

        navigation.CalculatePath(start, end, out _);
        navigation.CalculatePath(end, start, out _);

        Assert.Equal(before + 2, NavigationClass.PathRequestCount);
    }

    [Fact]
    public void PathRequestCount_IsMonotonicAcrossReads()
    {
        var (navigation, _, start, end) = CreateNavigation();

        var first = NavigationClass.PathRequestCount;
        navigation.CalculatePath(start, end, out _);
        var second = NavigationClass.PathRequestCount;
        navigation.CalculatePath(start, end, out _);
        var third = NavigationClass.PathRequestCount;

        Assert.True(second >= first);
        Assert.True(third >= second);
    }

    // WI-N0-01: per-origin request attribution. The default origin is Unit; explicit origins
    // land in their own buckets; the synchronous origins always sum to the global counter delta.
    [Fact]
    public void CalculatePath_AttributesRequestsByOrigin()
    {
        var (navigation, _, start, end) = CreateNavigation();
        var before = NavigationClass.PathRequestCount;
        var statsBefore = navigation.Statistics;

        navigation.CalculatePath(start, end, out _);
        navigation.CalculatePath(start, end, out _, PathRequestOrigin.SquadLeader);
        navigation.CalculatePath(start, end, out _, PathRequestOrigin.BlockedFollower);
        navigation.CalculatePath(start, end, out _, PathRequestOrigin.Horde);

        var stats = navigation.Statistics;
        Assert.Equal(1, stats.UnitRequests - statsBefore.UnitRequests);
        Assert.Equal(1, stats.SquadLeaderRequests - statsBefore.SquadLeaderRequests);
        Assert.Equal(1, stats.BlockedFollowerRequests - statsBefore.BlockedFollowerRequests);
        Assert.Equal(1, stats.HordeRequests - statsBefore.HordeRequests);
        Assert.Equal(0, stats.AsyncRequests - statsBefore.AsyncRequests);

        var requestDelta = NavigationClass.PathRequestCount - before;
        Assert.Equal(requestDelta, stats.SynchronousRequests - statsBefore.SynchronousRequests);
        Assert.Equal(4, requestDelta);
    }

    // WI-N0-01: a successful search reports the nodes it expanded.
    [Fact]
    public void CalculatePath_CountsNodesExpanded()
    {
        var (navigation, _, start, end) = CreateNavigation();
        var statsBefore = navigation.Statistics;

        var path = navigation.CalculatePath(start, end, out _);

        Assert.True(path.Count > 1);
        Assert.True(navigation.Statistics.NodesExpanded > statsBefore.NodesExpanded);
    }

    // WI-N0-01: an off-graph endpoint fails without expanding any node.
    [Fact]
    public void CalculatePath_CountsFailedSearchForOffGraphEndpoint()
    {
        var (navigation, _, start, _) = CreateNavigation();
        var statsBefore = navigation.Statistics;
        var offGraphEnd = start + new System.Numerics.Vector3(1_000_000, 0, 0);

        var path = navigation.CalculatePath(start, offGraphEnd, out _);

        Assert.Empty(path);
        var stats = navigation.Statistics;
        Assert.Equal(1, stats.FailedSearches - statsBefore.FailedSearches);
        Assert.Equal(0, stats.NodesExpanded - statsBefore.NodesExpanded);
    }

    // WI-N0-01: an impassable endpoint triggers a next-passable-position adjustment.
    [Fact]
    public void CalculatePath_CountsEndpointAdjustmentForImpassableEnd()
    {
        var (navigation, heightMap, start, _) = CreateNavigation();
        var impassableEnd = FindImpassablePosition(navigation, heightMap);
        Assert.NotNull(impassableEnd);

        var statsBefore = navigation.Statistics;
        navigation.CalculatePath(start, impassableEnd.Value, out var endIsPassable);
        var stats = navigation.Statistics;

        Assert.False(endIsPassable);
        Assert.True(stats.EndpointAdjustments > statsBefore.EndpointAdjustments);
    }

    private static System.Numerics.Vector3? FindImpassablePosition(NavigationClass navigation, HeightMap heightMap)
    {
        for (var y = 1; y < heightMap.Height - 1; y++)
        {
            for (var x = 1; x < heightMap.Width - 1; x++)
            {
                var position = heightMap.GetPosition(x, y);
                if (!navigation.IsPassable(position))
                {
                    return position;
                }
            }
        }

        return null;
    }

    private static (NavigationClass Navigation, HeightMap HeightMap, System.Numerics.Vector3 Start, System.Numerics.Vector3 End) CreateNavigation()
    {
        using var fileSystem = new DiskFileSystem(
            Path.Combine(System.Environment.CurrentDirectory, "Data", "Map", "Assets"));
        var mapFile = MapFile.FromFileSystemEntry(fileSystem.GetFile("BlendTileData_CliffTextures.map"));
        Assert.NotNull(mapFile.BlendTileData);
        Assert.NotNull(mapFile.HeightMapData);
        var heightMap = new HeightMap(mapFile.HeightMapData);
        var navigation = new NavigationClass(mapFile.BlendTileData, heightMap);

        // Real tile positions, so both endpoints resolve to graph nodes inside the fixture map.
        var start = heightMap.GetPosition(1, 1);
        var end = heightMap.GetPosition(heightMap.Width - 2, heightMap.Height - 2);
        return (navigation, heightMap, start, end);
    }
}
