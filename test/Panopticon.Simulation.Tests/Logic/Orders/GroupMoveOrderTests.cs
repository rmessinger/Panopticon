// WI-M4-04: squad group orders through the OrderProcessor's move routing.
// Pins that a squadded selection costs one leader path (path requests scale with squads, not
// units) and that a squad-less selection keeps per-unit SetTargetPoint behavior, so player
// selections stay on the pre-existing path.
//
// Run with: dotnet test src --filter "FullyQualifiedName~GroupMoveOrderTests"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Panopticon.IO;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Panopticon.Tests.Logic.AI.Hierarchy;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Logic.Orders;

// Serialized with the other Navigation classes: these tests assert deltas of the
// process-global Navigation.PathRequestCount, which parallel test classes would race.
[Collection("Navigation")]
public class GroupMoveOrderTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public void SquaddedSelection_CostsOneSharedLeaderPath()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var (navigation, _, start, end) = CreateNavigation();

        // Cluster eight units so every formation offset lands on a passable tile at the
        // destination found below; the leader (nearest to the destination) is unambiguous.
        var leader = NewSquadUnit(game, player, start + new Vector3(1, 1, 0), buildCost: 200f);
        var offsets = new[]
        {
            new Vector3(-1, -1, 0),
            new Vector3(-2, 0, 0),
            new Vector3(-1, -2, 0),
            new Vector3(-2, -2, 0),
            new Vector3(0, -1, 0),
            new Vector3(-2, -1, 0),
            new Vector3(-1, 0, 0),
        };
        var followers = offsets
            .Select(offset => NewSquadUnit(game, player, leader.Translation + offset, buildCost: 100f))
            .ToList();
        var members = new List<GameObject> { leader };
        members.AddRange(followers);

        var policy = new HierarchicalAIDecisionPolicy(
            new RuleBasedGeneralPolicy(),
            new RuleBasedCommanderDecisionPolicy(),
            new RuleBasedSoldierDecisionPolicy());
        typeof(Player).GetProperty(nameof(Player.AIPlayer))!
            .SetValue(player, new AIPlayer(player, policy));

        // The first decision tick synchronizes the hierarchy: eight units form one squad.
        _ = policy.DecideNextOrders(new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: 1));
        var squad = Assert.Single(policy.Hierarchy.Squads);
        Assert.Equal(8, squad.Members.Count);

        // GroupMovementTests already proves the far corner reachable with negative-offset
        // landings; assert the precondition so a blocked follower never silently costs a path.
        foreach (var offset in offsets)
        {
            Assert.True(
                navigation.IsPassable(end + offset),
                $"test-map far corner should keep formation landings passable at offset {offset}");
        }

        var before = NavigationClass.PathRequestCount;

        OrderProcessor.MoveUnits(navigation, player, members, end);

        // One leader path serves the whole squad; no follower was blocked (precondition above).
        Assert.Equal(before + 1, NavigationClass.PathRequestCount);
        foreach (var member in members)
        {
            Assert.True(member.AIUpdate!.TargetPoints.Count > 0);
        }
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SquadlessSelection_KeepsPerUnitOrders()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var (navigation, heightMap, start, end) = CreateNavigation();

        var units = new List<GameObject>();
        for (var i = 0; i < 8; i++)
        {
            units.Add(NewSquadUnit(game, player, start + new Vector3(-i, -i, 0), buildCost: 100f));
        }

        var before = NavigationClass.PathRequestCount;

        // No hierarchy is attached to this player, so every unit keeps its own order. The mocked
        // host has no engine navigation, so SetTargetPoint lands as the raw direct target.
        OrderProcessor.MoveUnits(navigation, player, units, end);

        Assert.Equal(before, NavigationClass.PathRequestCount);
        foreach (var unit in units)
        {
            var targets = unit.AIUpdate!.TargetPoints;
            Assert.True(targets.Count > 0);
            Assert.Equal(end, targets[^1]);
        }
    }

    private (NavigationClass Navigation, HeightMap HeightMap, Vector3 Start, Vector3 End) CreateNavigation()
    {
        using var fileSystem = new DiskFileSystem(
            Path.Combine(System.Environment.CurrentDirectory, "Data", "Map", "Assets"));
        var mapFile = Panopticon.Data.Map.MapFile.FromFileSystemEntry(fileSystem.GetFile("BlendTileData_CliffTextures.map"));
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

    private GameObject NewSquadUnit(MockedGameTest.TestGame game, Player owner, Vector3 position, float buildCost)
    {
        var definition = new ObjectDefinition($"M4GroupOrder_{_nextUnitId}");
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        SetProperty(definition, nameof(ObjectDefinition.BuildCost), buildCost);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default, inherited: false));
        definition.Behaviors.Add("ModuleTag_AI",
            new ModuleDataContainer("ModuleTag_AI",
                new AIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = game.GameLogic.CreateObject(definition, owner);
        gameObject.UpdateTransform(position);
        _nextUnitId++;
        return gameObject;
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private int _nextUnitId = 1;
}
