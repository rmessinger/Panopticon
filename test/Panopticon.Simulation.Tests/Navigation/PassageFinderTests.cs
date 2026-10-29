// WI-N11-01: the pure passage finder. Pins span detection on synthetic grids (straight gap,
// diagonal gap, L corridor), one-sided walls producing no span, throat overlap for offset
// routes through one gap, and the cross-section cache (rescan casts nothing, a passability
// version change resets it).
//
// Run with: dotnet test src --filter "FullyQualifiedName~PassageFinderTests"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

public class PassageFinderTests
{
    private const int Size = 16;

    // Tile scale of the synthetic height map: tile (x, y) sits at world (x * 10, y * 10).
    private static Vector3 World(int x, int y) => new(x * 10f, y * 10f, 0f);

    private static NavigationClass CreateNavigation(Action<BlendTileData>? markImpassable = null)
    {
        var tileData = BlendTileData.Create(Size, Size);
        markImpassable?.Invoke(tileData);
        var heightMap = new HeightMap(HeightMapData.Create(0, new ushort[Size, Size]));
        return new NavigationClass(tileData, heightMap);
    }

    private static void WallColumn(BlendTileData tileData, int x, int fromY, int toY)
    {
        for (var y = fromY; y <= toY; y++)
        {
            tileData.Impassability[x, y] = true;
        }
    }

    private static void WallRow(BlendTileData tileData, int y, int fromX, int toX)
    {
        for (var x = fromX; x <= toX; x++)
        {
            tileData.Impassability[x, y] = true;
        }
    }

    private static List<Vector3> RowRoute(int y, int fromX, int toX)
    {
        var route = new List<Vector3>();
        for (var x = fromX; x <= toX; x++)
        {
            route.Add(World(x, y));
        }

        return route;
    }

    [Fact]
    public void StraightGap_YieldsOneSpanWithThroatInsideTheGap()
    {
        // Two wall rows with a three-cell corridor between them (rows 6-8); the route runs
        // along the corridor on row 7.
        var navigation = CreateNavigation(tileData =>
        {
            WallRow(tileData, 5, 0, Size - 1);
            WallRow(tileData, 9, 0, Size - 1);
        });
        var cache = new Panopticon.Navigation.CrossSectionCache(0);

        var spans = Panopticon.Navigation.PassageFinder.FindSpans(
            navigation, RowRoute(7, 1, 14), pitch: 20f, cache);

        var span = Assert.Single(spans);
        // Every sample is walled in on both sides: the first sample holds the throat.
        Assert.Equal((1, 7), span.ThroatCell);
        // Corridor rows 6-8 minus nothing: the cross-section width is 3 cells.
        Assert.Equal(3, span.WidthCells);
        Assert.Equal(1, span.Lanes);
        Assert.Equal(new Vector2(1, 0), span.Direction);
        Assert.Contains(span.ThroatCell, span.Corridor);
        Assert.Contains((7, 6), span.Corridor);
        Assert.Contains((7, 8), span.Corridor);
    }

    [Fact]
    public void DiagonalGap_YieldsOneSpan()
    {
        // Solid half-plane walls on both sides of a diagonal route: x+y<=11 and x+y>=17
        // leave the corridor sums 12-16. Perpendicular rays step sum+-2, so from the
        // x+y=14 centerline each ray sees one passable cell before the wall: width 3.
        var navigation = CreateNavigation(tileData =>
        {
            for (var x = 0; x < Size; x++)
            {
                for (var y = 0; y < Size; y++)
                {
                    if (x + y <= 11 || x + y >= 17)
                    {
                        tileData.Impassability[x, y] = true;
                    }
                }
            }
        });
        var cache = new Panopticon.Navigation.CrossSectionCache(0);

        var route = new List<Vector3>();
        for (var x = 2; x <= 12; x++)
        {
            route.Add(World(x, 14 - x));
        }

        var spans = Panopticon.Navigation.PassageFinder.FindSpans(
            navigation, route, pitch: 20f, cache);

        var span = Assert.Single(spans);
        Assert.Equal(3, span.WidthCells);
        Assert.Equal(1, span.Lanes);
        Assert.NotEqual(Vector2.Zero, span.Direction);
    }

    [Fact]
    public void LShapedCorridor_YieldsOneSpanAfterMerging()
    {
        // An L corridor: horizontal leg row 7 between wall rows 5 and 9 (x 1..12), vertical
        // leg column 7 between wall columns 5 and 9 (y 1..12). The route follows the L.
        var navigation = CreateNavigation(tileData =>
        {
            WallRow(tileData, 5, 1, 12);
            WallRow(tileData, 9, 1, 12);
            WallColumn(tileData, 5, 1, 12);
            WallColumn(tileData, 9, 1, 12);
        });
        var cache = new Panopticon.Navigation.CrossSectionCache(0);

        var route = new List<Vector3>();
        for (var x = 1; x <= 7; x++)
        {
            route.Add(World(x, 7));
        }

        for (var y = 6; y >= 1; y--)
        {
            route.Add(World(7, y));
        }

        var spans = Panopticon.Navigation.PassageFinder.FindSpans(
            navigation, route, pitch: 20f, cache);

        Assert.NotEmpty(spans);
        Assert.All(spans, span => Assert.True(span.Lanes >= 1));
    }

    [Fact]
    public void OneSidedWall_YieldsNoSpan()
    {
        // A single wall row on one side: the north ray stops at the wall after one cell,
        // the south ray runs to the map edge, so the width (10 cells) exceeds the lane
        // limit and no span forms.
        var navigation = CreateNavigation(tileData => WallRow(tileData, 5, 0, Size - 1));
        var cache = new Panopticon.Navigation.CrossSectionCache(0);

        var spans = Panopticon.Navigation.PassageFinder.FindSpans(
            navigation, RowRoute(7, 1, 13), pitch: 20f, cache);

        Assert.Empty(spans);
    }

    [Fact]
    public void OffsetRoutesThroughOneGap_ProduceOverlappingThroats()
    {
        // The same straight-corridor map, two parallel routes one row apart: each span's
        // throat lies inside the other's corridor.
        var navigation = CreateNavigation(tileData =>
        {
            WallRow(tileData, 5, 0, Size - 1);
            WallRow(tileData, 9, 0, Size - 1);
        });
        var cache = new Panopticon.Navigation.CrossSectionCache(0);

        var first = Panopticon.Navigation.PassageFinder.FindSpans(
            navigation, RowRoute(6, 1, 13), pitch: 20f, cache).Single();
        var second = Panopticon.Navigation.PassageFinder.FindSpans(
            navigation, RowRoute(8, 1, 13), pitch: 20f, cache).Single();

        Assert.Contains(first.ThroatCell, second.Corridor);
        Assert.Contains(second.ThroatCell, first.Corridor);
    }

    [Fact]
    public void RescanningTheSameRouteMakesNoNewCasts()
    {
        var navigation = CreateNavigation(tileData =>
        {
            WallColumn(tileData, 4, 0, 6);
            WallColumn(tileData, 4, 8, Size - 1);
            WallColumn(tileData, 8, 0, 6);
            WallColumn(tileData, 8, 8, Size - 1);
        });
        var cache = new Panopticon.Navigation.CrossSectionCache(0);
        var route = RowRoute(7, 1, 13);

        Panopticon.Navigation.PassageFinder.ResetCounters();
        var first = Panopticon.Navigation.PassageFinder.FindSpans(navigation, route, 20f, cache);
        var castsAfterFirst = Panopticon.Navigation.PassageFinder.Casts;
        var hitsAfterFirst = Panopticon.Navigation.PassageFinder.CacheHits;
        Assert.True(castsAfterFirst > 0);
        Assert.True(first.Count > 0);

        var second = Panopticon.Navigation.PassageFinder.FindSpans(navigation, route, 20f, cache);

        Assert.Equal(castsAfterFirst, Panopticon.Navigation.PassageFinder.Casts);
        Assert.True(Panopticon.Navigation.PassageFinder.CacheHits > hitsAfterFirst);
        Assert.Equal(first.Count, second.Count);
    }

    [Fact]
    public void PassabilityVersionChange_ResetsTheCache()
    {
        var navigation = CreateNavigation(tileData =>
        {
            WallColumn(tileData, 4, 0, 6);
            WallColumn(tileData, 4, 8, Size - 1);
            WallColumn(tileData, 8, 0, 6);
            WallColumn(tileData, 8, 8, Size - 1);
        });
        var cache = new Panopticon.Navigation.CrossSectionCache(0);
        var route = RowRoute(7, 1, 13);

        Panopticon.Navigation.PassageFinder.ResetCounters();
        _ = Panopticon.Navigation.PassageFinder.FindSpans(navigation, route, 20f, cache);
        var castsAfterFirst = Panopticon.Navigation.PassageFinder.Casts;

        // A passability change bumps the version: the stale widths are discarded and the
        // rays are cast again.
        cache.Reset(1);
        _ = Panopticon.Navigation.PassageFinder.FindSpans(navigation, route, 20f, cache);

        Assert.True(Panopticon.Navigation.PassageFinder.Casts > castsAfterFirst);
    }
}
