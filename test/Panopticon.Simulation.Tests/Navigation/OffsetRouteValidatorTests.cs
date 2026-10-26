// WI-N7-02: offset retrace validation behind NavigationOptions.ValidateOffsetRoutes.
// Pins the segment walk against a wall, the landing-on-leader-route reroute, the option-off
// retrace that still crosses, and the fallback when landing is disabled.
//
// Run with: dotnet test src --filter "FullyQualifiedName~OffsetRouteValidatorTests"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Logic.Object;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

// Serialized with the other Navigation classes: the MoveSquad tests assert deltas of the
// process-global Navigation.PathRequestCount.
[Collection("Navigation")]
public class OffsetRouteValidatorTests : MockedGameTest
{
    private const int Size = 16;

    // Tile scale of the synthetic height map: tile (x, y) sits at world (x*10, y*10).
    private static Vector3 World(int x, int y) => new(x * 10f, y * 10f, 0f);

    // Wall column x=5 over rows 4..12 splits the lower half into left and right regions while
    // rows 0..3 and 13..15 stay open, so around-the-wall routes exist.
    private static NavigationClass CreateNavigation()
    {
        var tileData = BlendTileData.Create(Size, Size);
        for (var y = 4; y <= 12; y++)
        {
            tileData.Impassability[5, y] = true;
        }

        var heightMap = new HeightMap(HeightMapData.Create(0, new ushort[Size, Size]));
        return new NavigationClass(tileData, heightMap);
    }

    // Straight west route along row 2 from (8, 2) to (1, 2); all clear.
    private static readonly Vector3[] ClearRoute =
    [
        World(8, 2), World(7, 2), World(6, 2), World(5, 2), World(4, 2), World(3, 2), World(2, 2), World(1, 2),
    ];

    [Fact, Trait("Category", "Roadmap")]
    public void Crosses_ReturnsFalseForClearOffsetRoutes()
    {
        var navigation = CreateNavigation();

        Assert.False(OffsetRouteValidator.Crosses(navigation, ClearRoute, Vector2.Zero));
        Assert.False(OffsetRouteValidator.Crosses(navigation, ClearRoute, new Vector2(0, -10)));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Crosses_ReturnsTrueWhenTheOffsetRouteCrossesAWall()
    {
        var navigation = CreateNavigation();

        // Shifted four tiles south the same route runs along row 6, straight into the wall
        // column at (5, 6), even though both endpoints stay passable.
        Assert.True(navigation.IsPassable(World(8, 6)));
        Assert.True(navigation.IsPassable(World(1, 6)));
        Assert.True(OffsetRouteValidator.Crosses(navigation, ClearRoute, new Vector2(0, 40)));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Crosses_ReturnsTrueForOffGraphOffsets()
    {
        var navigation = CreateNavigation();

        Assert.True(OffsetRouteValidator.Crosses(navigation, ClearRoute, new Vector2(0, 200)));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void MoveSquad_ValidateOffsetRoutesLandsCrossingFollowerOnTheLeaderRoute()
    {
        var navigation = CreateNavigation();
        navigation.Options.ValidateOffsetRoutes = true;
        var game = new TestGame(SageGame.CncGenerals, navigation);

        // The follower sits four tiles south of the leader: its offset destination is
        // passable, but the offset retrace crosses the wall column.
        var leader = NewUnit(World(8, 2), game: game);
        var follower = NewUnit(World(8, 6), game: game);

        var before = NavigationClass.PathRequestCount;
        var statsBefore = navigation.Statistics;

        GroupMovement.MoveSquad(navigation, [leader, follower], World(1, 2));

        var stats = navigation.Statistics;
        Assert.Equal(before + 1, NavigationClass.PathRequestCount);
        Assert.Equal(1, stats.SquadLeaderRequests - statsBefore.SquadLeaderRequests);
        Assert.Equal(0, stats.BlockedFollowerRequests - statsBefore.BlockedFollowerRequests);
        Assert.Equal(1, stats.ReroutedFollowers - statsBefore.ReroutedFollowers);
        Assert.Equal(1, stats.LandedFollowers - statsBefore.LandedFollowers);
        Assert.Equal(0, stats.FallbackFollowers - statsBefore.FallbackFollowers);

        // The rerouted follower rides the leader's exact route and lands on it; no retrace
        // point crosses the wall.
        Assert.True(follower.AIUpdate!.TargetPoints.Count > 0);
        Assert.Equal(World(1, 2), follower.AIUpdate.TargetPoints[^1]);
        Assert.All(follower.AIUpdate.TargetPoints, point => Assert.True(navigation.IsPassable(point)));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void MoveSquad_WithoutValidationTheOffsetRetraceStillCrosses()
    {
        var navigation = CreateNavigation();
        var game = new TestGame(SageGame.CncGenerals, navigation);

        var leader = NewUnit(World(8, 2), game: game);
        var follower = NewUnit(World(8, 6), game: game);

        GroupMovement.MoveSquad(navigation, [leader, follower], World(1, 2));

        // Option off changes nothing: the follower keeps the formation-offset retrace, which
        // crosses the wall column (row 6) on its way to the offset destination, and no reroute
        // is counted.
        Assert.Equal(0, navigation.Statistics.ReroutedFollowers);
        var retrace = follower.AIUpdate!.TargetPoints;
        Assert.True(retrace.Count > 0);
        Assert.Equal(World(1, 6), retrace[^1]);
        var leaderRoute = new List<Vector3>(leader.AIUpdate!.TargetPoints);
        leaderRoute.Insert(0, leader.Translation);
        Assert.True(OffsetRouteValidator.Crosses(navigation, leaderRoute, new Vector2(0, 40)));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Crosses_ReturnsTrueWhenAnOffsetWaypointLandsOnAnImpassableCell()
    {
        var navigation = CreateNavigation();

        // The mid-route waypoint lands on the wall at (5, 6); the Bresenham segment walk skips
        // segment endpoints, so only the waypoint check catches this route.
        var route = new List<Vector3> { World(8, 2), World(5, 6), World(1, 2) };
        Assert.False(navigation.Graph.GetNode(5, 6).IsPassable);
        Assert.True(OffsetRouteValidator.Crosses(navigation, route, Vector2.Zero));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void MoveSquad_CrossingFollowerWithoutLandingFallsBackToItsOwnSearch()
    {
        var navigation = CreateNavigation();
        navigation.Options.ValidateOffsetRoutes = true;
        navigation.Options.MaxLandingRadiusCells = 0;
        var game = new TestGame(SageGame.CncGenerals, navigation);

        var leader = NewUnit(World(8, 2), game: game);
        var follower = NewUnit(World(8, 6), game: game);

        var before = NavigationClass.PathRequestCount;
        var statsBefore = navigation.Statistics;

        GroupMovement.MoveSquad(navigation, [leader, follower], World(1, 2));

        var stats = navigation.Statistics;
        Assert.Equal(before + 2, NavigationClass.PathRequestCount);
        Assert.Equal(1, stats.ReroutedFollowers - statsBefore.ReroutedFollowers);
        Assert.Equal(1, stats.FallbackFollowers - statsBefore.FallbackFollowers);
        Assert.Equal(1, stats.BlockedFollowerRequests - statsBefore.BlockedFollowerRequests);

        // The fallback route exists around the wall and never crosses it.
        Assert.True(follower.AIUpdate!.TargetPoints.Count > 0);
        Assert.All(follower.AIUpdate.TargetPoints, point => Assert.True(navigation.IsPassable(point)));
    }

    private GameObject NewUnit(Vector3 position, ObjectKinds kind = ObjectKinds.Infantry, TestGame? game = null)
    {
        var host = game ?? Generals;
        var definition = new ObjectDefinition($"OffsetRoute_{kind}_{_nextObjectId}");
        definition.KindOf.Set(kind, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default, inherited: false));
        definition.Behaviors.Add("ModuleTag_AI",
            new ModuleDataContainer("ModuleTag_AI",
                new AIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = new GameObject(definition, host.GameEngine, NewPlayer())
        {
            Id = new ObjectId(_nextObjectId++),
        };
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private Panopticon.Logic.Player NewPlayer() => new(1, null, default, Generals);

    private uint _nextObjectId = 1;
}
