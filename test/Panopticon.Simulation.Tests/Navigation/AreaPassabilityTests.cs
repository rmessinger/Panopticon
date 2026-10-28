using System;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

// PathOptimizer and Transform live in the imported Panopticon.Navigation namespace; the
// NavigationClass alias keeps the class name clear of this namespace's own tail segment.
[Collection("Navigation")]
public class AreaPassabilityTests
{
    private NavigationClass CreateNavigation()
    {
        var blendTileData = BlendTileData.Create(16, 16);
        var heightMapData = HeightMapData.Create(0, new ushort[16, 16]);
        var heightMap = new HeightMap(heightMapData);
        return new NavigationClass(blendTileData, heightMap);
    }

    private static bool IsMarked(NavigationClass navigation, int x, int y) =>
        !navigation.Graph.GetNode(x, y).IsPassable;

    // Covers exactly the target tile's cell square (node position +/- half a cell); the
    // inset keeps edge-inclusive overlap from spilling into the neighbouring cells.
    private static void BlockCell(NavigationClass navigation, int x, int y)
    {
        const float inset = 0.02f;
        navigation.UpdateAreaPassability(
            new BoxCollider(
                new RectangleF(x * 10f - 5f + inset, y * 10f - 5f + inset, 10f - 2 * inset, 10f - 2 * inset),
                1000f),
            passable: false);
    }

    [Fact]
    public void Marking_CoversEveryCellOverlappingTheCollider()
    {
        var navigation = CreateNavigation();

        // Rect spanning a third of cell 11's square, all of 12, and a sliver of 13:
        // every touched cell is marked, none beyond.
        navigation.UpdateAreaPassability(
            new BoxCollider(new RectangleF(108.5f, 205.02f, 14f, 29.96f), 1000f),
            passable: false);

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var expected = x is 11 or 12 or 13 && y is 21 or 22 or 23;
                Assert.Equal(expected, IsMarked(navigation, x, y));
            }
        }
    }

    [Fact]
    public void Marking_ClosesLatticeGapsForRotatedColliders()
    {
        var navigation = CreateNavigation();

        // A 20x20 square rotated 45 degrees. The rect ctor keeps the box's local origin at
        // its min corner, so the rotated local AABB is [-14.14..14.14] x [0..28.28]; the
        // translation centers that AABB on (100, 100), giving [85.86..114.14]^2. The corner
        // cells (9,9), (11,9), (9,11), (11,11) overlap the AABB while their centers fall
        // outside the rotated box: point containment left them passable; overlap marking
        // must not.
        var collider = new BoxCollider(new RectangleF(0f, 0f, 20f, 20f), 1000f);
        collider.Update(new Transform(
            new Vector3(100f, 100f - 14.1421356f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f)));
        navigation.UpdateAreaPassability(collider, passable: false);

        for (var y = 8; y <= 12; y++)
        {
            for (var x = 8; x <= 12; x++)
            {
                var expected = x is 9 or 10 or 11 && y is 9 or 10 or 11;
                Assert.Equal(expected, IsMarked(navigation, x, y));
            }
        }
    }

    [Fact]
    public void Unmarking_RestoresTheSameCells()
    {
        var navigation = CreateNavigation();
        var collider = new BoxCollider(new RectangleF(105.02f, 105.02f, 9.96f, 9.96f), 1000f);

        navigation.UpdateAreaPassability(collider, passable: false);
        Assert.True(IsMarked(navigation, 11, 11));

        navigation.UpdateAreaPassability(collider, passable: true);
        Assert.False(IsMarked(navigation, 11, 11));
    }

    [Fact]
    public void DirectLine_ClearSegmentIsPassable()
    {
        var navigation = CreateNavigation();

        Assert.True(PathOptimizer.IsDirectLinePassable(
            navigation, new Vector2(80f, 20f), new Vector2(80f, 40f)));
    }

    [Fact]
    public void DirectLine_SegmentCrossingABlockedCellFails()
    {
        var navigation = CreateNavigation();
        BlockCell(navigation, 8, 3);

        Assert.False(PathOptimizer.IsDirectLinePassable(
            navigation, new Vector2(80f, 20f), new Vector2(80f, 40f)));
    }

    [Fact]
    public void DirectLine_BlockedEndpointFails()
    {
        var navigation = CreateNavigation();
        BlockCell(navigation, 8, 4);

        Assert.False(PathOptimizer.IsDirectLinePassable(
            navigation, new Vector2(80f, 20f), new Vector2(80f, 40f)));
    }

    [Fact]
    public void DirectLine_StartCellMayBeImpassable()
    {
        var navigation = CreateNavigation();
        BlockCell(navigation, 8, 2);

        // A unit standing on an impassable cell must still be able to plan its way off it;
        // only the cells ahead of the start are checked.
        Assert.True(PathOptimizer.IsDirectLinePassable(
            navigation, new Vector2(80f, 20f), new Vector2(80f, 40f)));
    }

    [Fact]
    public void DirectLine_OffGraphEndpointFails()
    {
        var navigation = CreateNavigation();

        Assert.False(PathOptimizer.IsDirectLinePassable(
            navigation, new Vector2(80f, 20f), new Vector2(500f, 500f)));
    }
}
