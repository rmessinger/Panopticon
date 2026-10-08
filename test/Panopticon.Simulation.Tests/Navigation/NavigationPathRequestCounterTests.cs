using System;
using System.IO;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

/// <summary>
/// WI-M1-02: the engine path-request counter. It must count every CalculatePath entry
/// monotonically (pollers derive per-tick deltas from it) and must not perturb pathfinding.
/// </summary>
public class NavigationPathRequestCounterTests
{
    [Fact]
    public void CalculatePath_IncrementsTheRequestCounter()
    {
        var (navigation, start, end) = CreateNavigation();

        var before = NavigationClass.PathRequestCount;

        navigation.CalculatePath(start, end, out _);
        navigation.CalculatePath(end, start, out _);

        Assert.Equal(before + 2, NavigationClass.PathRequestCount);
    }

    [Fact]
    public void PathRequestCount_IsMonotonicAcrossReads()
    {
        var (navigation, start, end) = CreateNavigation();

        var first = NavigationClass.PathRequestCount;
        navigation.CalculatePath(start, end, out _);
        var second = NavigationClass.PathRequestCount;
        navigation.CalculatePath(start, end, out _);
        var third = NavigationClass.PathRequestCount;

        Assert.True(second >= first);
        Assert.True(third >= second);
    }

    private static (NavigationClass Navigation, System.Numerics.Vector3 Start, System.Numerics.Vector3 End) CreateNavigation()
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
        return (navigation, start, end);
    }
}
