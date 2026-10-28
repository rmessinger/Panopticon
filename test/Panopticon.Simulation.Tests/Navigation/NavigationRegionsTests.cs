using System;
using System.IO;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

/// <summary>
/// WI-N1-02: connected-region labels. Labels must split exactly where search cannot connect,
/// must refresh after UpdateAreaPassability, and must let ComputePath reject cross-region
/// requests without touching Graph.Search.
/// </summary>
[Collection("Navigation")]
public class NavigationRegionsTests
{
    private static (NavigationClass Navigation, HeightMap HeightMap) CreateNavigation()
    {
        using var fileSystem = new DiskFileSystem(
            Path.Combine(System.Environment.CurrentDirectory, "Data", "Map", "Assets"));
        var mapFile = MapFile.FromFileSystemEntry(fileSystem.GetFile("BlendTileData_CliffTextures.map"));
        var heightMap = new HeightMap(mapFile.HeightMapData);

        return (new NavigationClass(mapFile.BlendTileData, heightMap), heightMap);
    }

    [Fact]
    public void SameRegion_TracksStructureWalls()
    {
        var graph = new Graph(30, 30);
        var left = graph.GetNode(3, 15);
        var right = graph.GetNode(27, 15);
        var regions = new NavigationRegions(graph);

        Assert.True(regions.SameRegion(left, right));

        for (var y = 0; y < graph.Height; y++)
        {
            graph.GetNode(15, y).Passability = Passability.Impassable;
        }

        // Labels are lazy: without MarkDirty the stale labels still report one region.
        Assert.True(regions.SameRegion(left, right));

        regions.MarkDirty();
        Assert.False(regions.SameRegion(left, right));
        Assert.True(regions.SameRegion(left, graph.GetNode(3, 3)));
    }

    [Fact]
    public void SameRegion_ImpassableCellsNeverShareARegion()
    {
        var graph = new Graph(10, 10);
        graph.GetNode(5, 5).Passability = Passability.Impassable;
        var regions = new NavigationRegions(graph);

        Assert.False(regions.SameRegion(graph.GetNode(5, 5), graph.GetNode(5, 5)));
        Assert.True(regions.SameRegion(graph.GetNode(1, 1), graph.GetNode(8, 8)));
    }

    // AC: unreachable requests never call Graph.Search (nodes expanded stays flat) and labels
    // update after UpdateAreaPassability. The fixture map's graph extends past its heightmap
    // on every edge, so border rows can never be marked from world coordinates and a full
    // wall is impossible. Instead the structure carves a one-cell pocket: its cell forms a
    // region of its own, and a request out of it must reject without searching.
    [Fact]
    public void CalculatePath_RejectsCrossRegionRequestsWithoutSearching()
    {
        var (navigation, heightMap) = CreateNavigation();
        var statsBefore = navigation.Statistics;

        var pocket = CarveSingleCellPocket(navigation);
        // The pocket scan starts at the first interior tile, so the far corner is guaranteed
        // outside the ring.
        var outside = heightMap.GetPosition(heightMap.Width - 2, heightMap.Height - 2);

        var path = navigation.CalculatePath(pocket, outside, out _);

        var statsAfter = navigation.Statistics;
        Assert.Empty(path);
        Assert.Equal(statsBefore.NodesExpanded, statsAfter.NodesExpanded);
        Assert.Equal(statsBefore.FailedSearches + 1, statsAfter.FailedSearches);
    }

    [Fact]
    public void CalculatePath_SameRegionRequestsStillSearch()
    {
        var (navigation, heightMap) = CreateNavigation();

        var start = heightMap.GetPosition(1, 1);
        var end = heightMap.GetPosition(heightMap.Width - 2, heightMap.Height - 2);
        var path = navigation.CalculatePath(start, end, out _);

        Assert.NotEmpty(path);
    }

    // Marks the eight neighbours of a fully passable interior 3x3 block, leaving the center
    // passable but isolated. Returns the pocket cell's world position.
    private static System.Numerics.Vector3 CarveSingleCellPocket(NavigationClass navigation)
    {
        var graph = navigation.CloneGraphForWorker();

        int cx = 0, cy = 0;
        for (var y = 1; y < graph.Height - 1 && cx == 0; y++)
        {
            for (var x = 1; x < graph.Width - 1; x++)
            {
                if (IsPassableBlock(graph, x, y))
                {
                    cx = x;
                    cy = y;
                    break;
                }
            }
        }

        Assert.NotEqual(0, cx);

        MarkTiles(navigation, graph, cx - 1, cx + 1, cy - 1, cy - 1);
        MarkTiles(navigation, graph, cx - 1, cx + 1, cy + 1, cy + 1);
        MarkTiles(navigation, graph, cx - 1, cx - 1, cy, cy);
        MarkTiles(navigation, graph, cx + 1, cx + 1, cy, cy);

        var center = navigation.GetNodePosition(graph.GetNode(cx, cy));
        Assert.True(navigation.IsPassable(ToWorld(center)));
        Assert.False(navigation.IsPassable(ToWorld(navigation.GetNodePosition(graph.GetNode(cx - 1, cy)))));
        Assert.False(navigation.IsPassable(ToWorld(navigation.GetNodePosition(graph.GetNode(cx + 1, cy)))));
        Assert.False(navigation.IsPassable(ToWorld(navigation.GetNodePosition(graph.GetNode(cx, cy - 1)))));
        Assert.False(navigation.IsPassable(ToWorld(navigation.GetNodePosition(graph.GetNode(cx, cy + 1)))));

        return ToWorld(center);
    }

    private static bool IsPassableBlock(Panopticon.Navigation.Graph graph, int cx, int cy)
    {
        for (var y = cy - 1; y <= cy + 1; y++)
        {
            for (var x = cx - 1; x <= cx + 1; x++)
            {
                if (!graph.GetNode(x, y).IsPassable)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // A world rect covering exactly the tile range [x0..x1] x [y0..y1]: each node owns the
    // cell square around its lattice position, and the 0.02 inset keeps edge-inclusive
    // overlap from spilling into the neighbouring cells.
    private static void MarkTiles(NavigationClass navigation, Panopticon.Navigation.Graph graph, int x0, int x1, int y0, int y1)
    {
        var origin = navigation.GetNodePosition(graph.GetNode(x0, y0));
        var tile = navigation.GetNodePosition(graph.GetNode(x0 + 1, y0)).X - origin.X;
        const float inset = 0.02f;

        navigation.UpdateAreaPassability(
            new BoxCollider(
                new RectangleF(
                    origin.X - tile / 2 + inset,
                    origin.Y - tile / 2 + inset,
                    (x1 - x0) * tile + tile - 2 * inset,
                    (y1 - y0) * tile + tile - 2 * inset),
                1000.0f),
            passable: false);
    }

    private static Vector3 ToWorld(Vector2 position) => new(position.X, position.Y, 0);
}
