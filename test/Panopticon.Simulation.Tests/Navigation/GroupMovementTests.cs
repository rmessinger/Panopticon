// WI-B4-04: the squad movement service.
// Pins that one leader path serves the whole squad (path requests scale with squad count), that
// followers retrace the leader path at their formation offsets, that blocked followers pay for
// their own path, and that aircraft members fly directly without a path request.
//
// Run with: dotnet test src --filter "FullyQualifiedName~GroupMovementTests"

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using Xunit.Abstractions;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

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

        GroupMovement.MoveSquad(navigation, members, end);

        Assert.Equal(before + 1, NavigationClass.PathRequestCount);

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

        GroupMovement.MoveSquad(navigation, new List<GameObject> { leader, follower }, destination);

        // Leader path plus exactly one fallback request for the blocked follower.
        Assert.Equal(before + 2, NavigationClass.PathRequestCount);

        // The blocked follower paths to the destination itself, not to its formation slot.
        var ai = follower.AIUpdate;
        Assert.True(ai.TargetPoints.Count > 0);
        Assert.Equal(destination, ai.TargetPoints[^1]);
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

    private GameObject NewUnit(Vector3 position, ObjectKinds kind = ObjectKinds.Infantry)
    {
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
        var gameObject = new GameObject(definition, Generals.GameEngine, NewPlayer())
        {
            Id = new ObjectId(_nextObjectId++),
        };
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private Panopticon.Logic.Player NewPlayer() => new(1, null, default, Generals);

    private uint _nextObjectId = 1;
}
