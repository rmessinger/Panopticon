// WI-B4-04: the squad movement service.
// Pins that one leader path serves the whole squad (path requests scale with squad count), that
// followers retrace the leader path at their formation offsets, that blocked followers pay for
// their own path, and that aircraft members fly directly without a path request.
//
// Run with: dotnet test src --filter "FullyQualifiedName~GroupMovementTests"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using Xunit.Abstractions;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

// Serialized with the other Navigation classes: these tests assert deltas of the
// process-global Navigation.PathRequestCount, which parallel test classes would race.
[Collection("Navigation")]
public class GroupMovementTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public void SquadMove_IssuesOneRequestAndAssignsFormationOffsets()
    {
        var (navigation, _, start, end) = CreateNavigation();
        // Nearest member to the destination becomes the leader; placed so that choice is unique.
        var leader = NewUnit(start + new Vector3(1, 1, 0));
        var followers = new[]
        {
            NewUnit(start),
            NewUnit(start + new Vector3(-1, 1, 0)),
            NewUnit(start + new Vector3(0, -1, 0)),
            NewUnit(start + new Vector3(-1, -1, 0)),
        };
        var members = new List<GameObject> { leader };
        members.AddRange(followers);

        var before = NavigationClass.PathRequestCount;
        var statsBefore = navigation.Statistics;

        GroupMovement.MoveSquad(navigation, members, end);

        Assert.Equal(before + 1, NavigationClass.PathRequestCount);

        // WI-N0-01: the squad's single request is attributed to its leader.
        var stats = navigation.Statistics;
        Assert.Equal(1, stats.SquadLeaderRequests - statsBefore.SquadLeaderRequests);
        Assert.Equal(0, stats.BlockedFollowerRequests - statsBefore.BlockedFollowerRequests);

        // The leader retraces the shared path exactly; each follower walks it at its formation
        // offset and lands on the offset destination.
        var path = navigation.CalculatePath(leader.Translation, end, out var endIsPassable);
        Assert.True(path.Count > 1);
        Assert.True(endIsPassable);

        Assert.Equal(path.Count - 1, leader.AIUpdate.TargetPoints.Count);
        Assert.Equal(end, leader.AIUpdate.TargetPoints[^1]);

        foreach (var follower in followers)
        {
            var offset = follower.Translation - leader.Translation;
            var ai = follower.AIUpdate;
            Assert.Equal(path.Count - 1, ai.TargetPoints.Count);
            Assert.Equal(end + new Vector3(offset.X, offset.Y, 0), ai.TargetPoints[^1]);
            var first = ai.TargetPoints[0] - new Vector3(offset.X, offset.Y, 0);
            Assert.Equal(path[1].X, first.X, 3);
            Assert.Equal(path[1].Y, first.Y, 3);
        }
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SquadMove_BlockedFollowerFallsBackToItsOwnRequest()
    {
        var (navigation, heightMap, start, _) = CreateNavigation();
        // WI-N2-01: disable the ring landing so the full-search fallback is exercised in
        // isolation; the landing path has its own test.
        navigation.Options.MaxLandingRadiusCells = 0;
        // Cluster the squad so the leader (nearer the map middle) is unambiguous once a
        // destination is found below.
        var leader = NewUnit(start + new Vector3(1, 1, 0));
        var follower = NewUnit(start);

        // Find a passable destination whose offset landing (the follower's formation slot),
        // computed exactly the way the service computes it, sits on an impassable tile. The
        // float round trip (landing - offset) + offset can snap to a neighbouring node, so the
        // scan must use the same expression as the service.
        var offset = follower.Translation - leader.Translation;
        var offset2D = new Vector3(offset.X, offset.Y, 0);
        Vector3? blockedLanding = null;
        var destination = Vector3.Zero;
        for (var y = 2; blockedLanding is null && y < heightMap.Height - 1; y++)
        {
            for (var x = 2; blockedLanding is null && x < heightMap.Width - 1; x++)
            {
                var candidate = heightMap.GetPosition(x, y);
                var landing = candidate + offset2D;
                if (!navigation.IsPassable(candidate) || navigation.IsPassable(landing))
                {
                    continue;
                }

                blockedLanding = landing;
                destination = candidate;
            }
        }

        Assert.NotNull(blockedLanding);

        var before = NavigationClass.PathRequestCount;
        var statsBefore = navigation.Statistics;

        GroupMovement.MoveSquad(navigation, new List<GameObject> { leader, follower }, destination);

        // Leader path plus exactly one fallback request for the blocked follower.
        Assert.Equal(before + 2, NavigationClass.PathRequestCount);

        // WI-N0-01: the leader request and the blocked follower fallback land in their own buckets.
        var stats = navigation.Statistics;
        Assert.Equal(1, stats.SquadLeaderRequests - statsBefore.SquadLeaderRequests);
        Assert.Equal(1, stats.BlockedFollowerRequests - statsBefore.BlockedFollowerRequests);

        // WI-N2-01: the blocked-follower split records the fallback.
        Assert.Equal(0, stats.LandedFollowers - statsBefore.LandedFollowers);
        Assert.Equal(1, stats.FallbackFollowers - statsBefore.FallbackFollowers);

        // The blocked follower paths to the destination itself, not to its formation slot.
        var ai = follower.AIUpdate;
        Assert.True(ai.TargetPoints.Count > 0);
        Assert.Equal(destination, ai.TargetPoints[^1]);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SquadMove_BlockedFollowerLandsOnTheNearestPassableCell()
    {
        var (navigation, heightMap, start, _) = CreateNavigation();

        // A one-tile diagonal formation offset: the follower sits exactly one tile diagonally
        // from the leader, so its landing slot is the tile diagonally adjacent to the
        // destination. The offset points away from the map's passable interior so the follower
        // stays farther from matching destinations than the leader, keeping the leader
        // unambiguous. The scan finds a destination whose slot is impassable while the
        // destination itself stays passable.
        var leader = NewUnit(start + new Vector3(1, 1, 0));
        var follower = NewUnit(leader.Translation + new Vector3(-10, -10, 0));
        var offset2D = new Vector3(-10, -10, 0);

        Vector3? blockedLanding = null;
        var destination = Vector3.Zero;
        for (var y = 2; blockedLanding is null && y < heightMap.Height - 1; y++)
        {
            for (var x = 2; blockedLanding is null && x < heightMap.Width - 1; x++)
            {
                var candidate = heightMap.GetPosition(x, y);
                var landing = candidate + offset2D;
                if (!navigation.IsPassable(candidate) || navigation.IsPassable(landing))
                {
                    continue;
                }

                var leaderDelta = leader.Translation - candidate;
                var followerDelta = follower.Translation - candidate;
                var leaderDistance = leaderDelta.X * leaderDelta.X + leaderDelta.Y * leaderDelta.Y;
                var followerDistance = followerDelta.X * followerDelta.X + followerDelta.Y * followerDelta.Y;
                if (followerDistance > leaderDistance)
                {
                    blockedLanding = landing;
                    destination = candidate;
                }
            }
        }

        Assert.NotNull(blockedLanding);

        var before = NavigationClass.PathRequestCount;
        var statsBefore = navigation.Statistics;
        FollowerLanding.ResetLandingRings();

        GroupMovement.MoveSquad(navigation, new List<GameObject> { leader, follower }, destination);

        // The landed follower issues no search: the leader's request is the only one.
        Assert.Equal(before + 1, NavigationClass.PathRequestCount);

        var stats = navigation.Statistics;
        Assert.Equal(1, stats.LandedFollowers - statsBefore.LandedFollowers);
        Assert.Equal(0, stats.FallbackFollowers - statsBefore.FallbackFollowers);
        Assert.Equal(0, stats.BlockedFollowerRequests - statsBefore.BlockedFollowerRequests);

        // One landing recorded in the ring histogram.
        Assert.Equal(1L, FollowerLanding.LandingRingHistogram.Sum());

        // The follower retraces the leader's path length and ends on a passable cell that is
        // not its blocked formation slot.
        var ai = follower.AIUpdate;
        Assert.Equal(leader.AIUpdate.TargetPoints.Count, ai.TargetPoints.Count);
        Assert.True(navigation.IsPassable(ai.TargetPoints[^1]));
        Assert.NotEqual(blockedLanding.Value, ai.TargetPoints[^1]);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SquadMove_SingletonSquadOrdersLikeASingleUnit()
    {
        var (navigation, _, start, end) = CreateNavigation();
        var unit = NewUnit(start);

        var before = NavigationClass.PathRequestCount;

        GroupMovement.MoveSquad(navigation, new List<GameObject> { unit }, end);

        Assert.Equal(before + 1, NavigationClass.PathRequestCount);
        Assert.True(unit.AIUpdate.TargetPoints.Count > 0);
        Assert.Equal(end, unit.AIUpdate.TargetPoints[^1]);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SquadMove_AircraftMembersFlyDirectlyWithoutPathRequests()
    {
        var (navigation, _, start, end) = CreateNavigation();
        var leader = NewUnit(start);
        var jet = NewUnit(start + new Vector3(5, 5, 0), ObjectKinds.Aircraft);

        var before = NavigationClass.PathRequestCount;

        GroupMovement.MoveSquad(navigation, new List<GameObject> { leader, jet }, end);

        // The squad's single request serves the ground leader; the aircraft gets the raw point.
        Assert.Equal(before + 1, NavigationClass.PathRequestCount);
        Assert.Equal(end, Assert.Single(jet.AIUpdate.TargetPoints));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void MoveUnits_GroupMovesOffKeepsPerUnitRequests()
    {
        var (navigation, _, start, end) = CreateNavigation();
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var player = NewPlayer();
        var units = new List<GameObject>
        {
            NewUnit(start, game: game),
            NewUnit(start + new Vector3(3, 0, 0), game: game),
            NewUnit(start + new Vector3(0, 3, 0), game: game),
        };

        var before = NavigationClass.PathRequestCount;
        var statsBefore = navigation.Statistics;

        OrderProcessor.MoveUnits(navigation, player, units, end);

        // WI-N2-02: option off keeps the pre-existing per-unit behavior exactly.
        var stats = navigation.Statistics;
        Assert.Equal(before + 3, NavigationClass.PathRequestCount);
        Assert.Equal(3, stats.UnitRequests - statsBefore.UnitRequests);
        Assert.Equal(0, stats.SquadLeaderRequests - statsBefore.SquadLeaderRequests);
        foreach (var unit in units)
        {
            Assert.True(unit.AIUpdate.TargetPoints.Count > 0);
        }
    }

    [Fact, Trait("Category", "Roadmap")]
    public void MoveUnits_GroupMovesOnSharesOneSearchPerCluster()
    {
        var (navigation, heightMap, start, end) = CreateNavigation();
        navigation.Options.GroupSelectionMoves = true;
        navigation.Options.SelectionClusterCells = 20;

        // Cell size 20 splits the fixture into distinct buckets; leader placed nearer the
        // destination so each cluster's leader choice is unique.
        var nearCluster = start;
        var farCluster = heightMap.GetPosition(5, 5);
        var loner = heightMap.GetPosition(9, 9);

        var player = NewPlayer();
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var nearLeader = NewUnit(nearCluster + new Vector3(1, 1, 0), game: game);
        var nearFollower = NewUnit(nearCluster, game: game);
        var farLeader = NewUnit(farCluster + new Vector3(1, 1, 0), game: game);
        var farFollower = NewUnit(farCluster, game: game);
        var singleton = NewUnit(loner, game: game);
        var jet = NewUnit(nearCluster + new Vector3(2, 2, 0), ObjectKinds.Aircraft, game: game);
        var units = new List<GameObject> { nearLeader, nearFollower, farLeader, farFollower, singleton, jet };

        var before = NavigationClass.PathRequestCount;
        var statsBefore = navigation.Statistics;

        OrderProcessor.MoveUnits(navigation, player, units, end);

        // Two clusters pay one leader search each; the loner keeps its own request and the
        // aircraft flies directly without a search.
        var stats = navigation.Statistics;
        Assert.Equal(before + 3, NavigationClass.PathRequestCount);
        Assert.Equal(2, stats.SquadLeaderRequests - statsBefore.SquadLeaderRequests);
        Assert.Equal(1, stats.UnitRequests - statsBefore.UnitRequests);
        Assert.Equal(0, stats.BlockedFollowerRequests - statsBefore.BlockedFollowerRequests);

        // Followers retrace their cluster's shared path.
        Assert.Equal(nearLeader.AIUpdate.TargetPoints.Count, nearFollower.AIUpdate.TargetPoints.Count);
        Assert.Equal(farLeader.AIUpdate.TargetPoints.Count, farFollower.AIUpdate.TargetPoints.Count);
        Assert.Equal(end, jet.AIUpdate.TargetPoints[^1]);
    }

    private (NavigationClass Navigation, HeightMap HeightMap, Vector3 Start, Vector3 End) CreateNavigation()
    {
        using var fileSystem = new DiskFileSystem(
            Path.Combine(System.Environment.CurrentDirectory, "Data", "Map", "Assets"));
        var mapFile = MapFile.FromFileSystemEntry(fileSystem.GetFile("BlendTileData_CliffTextures.map"));
        Assert.NotNull(mapFile.BlendTileData);
        Assert.NotNull(mapFile.HeightMapData);
        var heightMap = new HeightMap(mapFile.HeightMapData);
        var navigation = new NavigationClass(mapFile.BlendTileData, heightMap);

        var start = heightMap.GetPosition(1, 1);
        var end = heightMap.GetPosition(heightMap.Width - 2, heightMap.Height - 2);
        Assert.True(navigation.IsPassable(start));
        Assert.True(navigation.IsPassable(end));
        return (navigation, heightMap, start, end);
    }

    private GameObject NewUnit(Vector3 position, ObjectKinds kind = ObjectKinds.Infantry, TestGame? game = null)
    {
        var host = game ?? Generals;
        var definition = new ObjectDefinition($"GroupMovement_{kind}_{_nextObjectId}");
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
