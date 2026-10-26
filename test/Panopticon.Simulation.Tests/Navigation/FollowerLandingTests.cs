// WI-N2-01: the blocked-follower landing ring search.
// Pins ring order (row-major, deterministic), the destination-region constraint, the radius
// bound, and off-graph rejection on a synthetic grid where every cell is placed by the test.
//
// Run with: dotnet test src --filter "FullyQualifiedName~FollowerLandingTests"

using System;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

public class FollowerLandingTests
{
    private const int Size = 16;

    // Tile scale of the synthetic height map: tile (x, y) sits at world (x*10, y*10).
    private static Vector3 World(int x, int y) => new(x * 10f, y * 10f, 0f);

    private static NavigationClass CreateNavigation(Action<BlendTileData>? markImpassable = null)
    {
        var tileData = BlendTileData.Create(Size, Size);
        markImpassable?.Invoke(tileData);
        var heightMap = new HeightMap(HeightMapData.Create(0, new ushort[Size, Size]));
        return new NavigationClass(tileData, heightMap);
    }

    private static void SetImpassable(BlendTileData tileData, int x, int y) => tileData.Impassability[x, y] = true;

    [Fact, Trait("Category", "Roadmap")]
    public void TryLand_PicksTheFirstPassableCellInRowMajorRingOrder()
    {
        // Ring 1 around (8, 8) starts at (7, 7); marking it impassable hands the win to the
        // next row-major candidate (8, 7).
        var navigation = CreateNavigation(tileData =>
        {
            SetImpassable(tileData, 8, 8);
            SetImpassable(tileData, 7, 7);
        });
        var destinationNode = navigation.GetNodeAt(World(0, 0));

        var landed = FollowerLanding.TryLand(
            navigation, World(8, 8), destinationNode, 4, out var landing, out var ring);

        Assert.True(landed);
        Assert.Equal(1, ring);
        Assert.Equal(World(8, 7), landing);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void TryLand_StaysInsideTheDestinationRegion()
    {
        // A full wall column splits the grid; landing from a wall cell must stay on the
        // destination's side of it.
        var navigation = CreateNavigation(tileData =>
        {
            for (var y = 0; y < Size; y++)
            {
                SetImpassable(tileData, 8, y);
            }
        });

        var landedRight = FollowerLanding.TryLand(
            navigation, World(8, 8), navigation.GetNodeAt(World(10, 10)), 4, out var rightLanding, out _);
        Assert.True(landedRight);
        Assert.Equal(90f, rightLanding.X);

        var landedLeft = FollowerLanding.TryLand(
            navigation, World(8, 8), navigation.GetNodeAt(World(0, 0)), 4, out var leftLanding, out _);
        Assert.True(landedLeft);
        Assert.Equal(70f, leftLanding.X);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void TryLand_ReturnsFalseWhenTheBoundIsTooSmall()
    {
        // A 3x3 impassable block: ring 1 has no passable cell, ring 2 does.
        var navigation = CreateNavigation(tileData =>
        {
            for (var y = 7; y <= 9; y++)
            {
                for (var x = 7; x <= 9; x++)
                {
                    SetImpassable(tileData, x, y);
                }
            }
        });
        var destinationNode = navigation.GetNodeAt(World(0, 0));

        Assert.False(FollowerLanding.TryLand(
            navigation, World(8, 8), destinationNode, 1, out var landing, out var ring));
        Assert.Equal(default, landing);
        Assert.Equal(0, ring);

        Assert.True(FollowerLanding.TryLand(
            navigation, World(8, 8), destinationNode, 2, out landing, out ring));
        Assert.Equal(2, ring);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void TryLand_ReturnsFalseForOffGraphOffsets()
    {
        var navigation = CreateNavigation();
        var destinationNode = navigation.GetNodeAt(World(0, 0));

        Assert.False(FollowerLanding.TryLand(
            navigation, World(8, 8) + new Vector3(5000, 0, 0), destinationNode, 4, out var landing, out _));
        Assert.Equal(default, landing);
    }
}
