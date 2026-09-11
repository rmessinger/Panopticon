using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenSage.Logic;
using OpenSage.Logic.AI.Hierarchy;
using OpenSage.Logic.Object;
using Xunit;

namespace OpenSage.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-A4-18: squad identity, membership, commander selection, spawn assignment, death
/// promotion, and empty-squad cleanup, all deterministic per the documented rules.
/// </summary>
public sealed class SquadHierarchyTests : MockedGameTest
{
    private static GameObject SpawnUnit(TestGame game, Player owner, string name, float buildCost)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        SetProperty(definition, nameof(ObjectDefinition.BuildCost), buildCost);
        return game.GameLogic.CreateObject(definition, owner);
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static Player CreateAiPlayer(TestGame game)
    {
        var mapPlayers = new[]
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
            new OpenSage.Data.Map.Player
            {
                Name = "ai",
                Faction = "FactionAmerica",
                DisplayName = "AI",
                IsHuman = false,
            },
        };
        game.PlayerManager.OnNewGame(mapPlayers, GameType.Skirmish);
        return game.PlayerManager.Players[2];
    }

    [Fact]
    public void Synchronize_GroupsEligibleUnits_InIdOrder_UpToMaxSquadSize()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        var spawned = new List<GameObject>();
        for (var i = 0; i < 10; i++)
        {
            spawned.Add(SpawnUnit(game, player, $"A4SquadUnit{i}", buildCost: 100f));
        }

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);

        Assert.Equal(2, hierarchy.Squads.Count);
        Assert.Equal(SquadHierarchy.MaxSquadSize, hierarchy.Squads[0].Members.Count);
        Assert.Equal(2, hierarchy.Squads[1].Members.Count);
        Assert.Equal(
            spawned.OrderBy(u => u.Id.Index).Select(u => u.Id),
            hierarchy.Squads.SelectMany(s => s.Members).Select(m => m.Id));
        Assert.All(hierarchy.Squads, s => Assert.NotNull(s.CommanderId));
    }

    [Fact]
    public void Commander_IsHighestBuildCost_TiesBreakToLowerId()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        SpawnUnit(game, player, "A4Cheap", buildCost: 100f);
        var expensive = SpawnUnit(game, player, "A4Expensive", buildCost: 2000f);
        var expensiveTwin = SpawnUnit(game, player, "A4ExpensiveTwin", buildCost: 2000f);
        Assert.True(expensive.Id.Index < expensiveTwin.Id.Index);

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);

        var squad = Assert.Single(hierarchy.Squads);
        Assert.Equal(expensive.Id, squad.CommanderId);
        Assert.NotEqual(expensiveTwin.Id, squad.CommanderId);
    }

    [Fact]
    public void CommanderDeath_PromotesNextBestMember_Deterministically()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        var cheap = SpawnUnit(game, player, "A4Cheap", buildCost: 100f);
        var expensive = SpawnUnit(game, player, "A4Expensive", buildCost: 2000f);
        var mid = SpawnUnit(game, player, "A4Mid", buildCost: 500f);

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);
        var squad = Assert.Single(hierarchy.Squads);
        Assert.Equal(expensive.Id, squad.CommanderId);

        game.GameLogic.DestroyObject(expensive);
        hierarchy.Synchronize(player);

        Assert.Equal(2, squad.Members.Count);
        Assert.Equal(mid.Id, squad.CommanderId);
    }

    [Fact]
    public void EmptySquad_IsRemoved_NewSpawnsGetFreshIdentity()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        SpawnUnit(game, player, "A4First", buildCost: 100f);
        SpawnUnit(game, player, "A4Second", buildCost: 100f);

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);
        var squad = Assert.Single(hierarchy.Squads);
        var removedId = squad.Id;

        foreach (var member in squad.Members.ToList())
        {
            game.GameLogic.DestroyObject(member);
        }
        hierarchy.Synchronize(player);
        Assert.Empty(hierarchy.Squads);

        SpawnUnit(game, player, "A4Late", buildCost: 100f);
        hierarchy.Synchronize(player);
        var late = Assert.Single(hierarchy.Squads);
        Assert.NotEqual(removedId, late.Id);
        Assert.Single(late.Members);
    }

    [Fact]
    public void SpawnAssignment_JoinsExistingOpenSquad_BeforeCreatingNew()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        SpawnUnit(game, player, "A4Alpha", buildCost: 100f);

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);
        Assert.Single(hierarchy.Squads);

        SpawnUnit(game, player, "A4Beta", buildCost: 100f);
        hierarchy.Synchronize(player);

        var squad = Assert.Single(hierarchy.Squads);
        Assert.Equal(2, squad.Members.Count);
    }

    [Fact]
    public void IneligibleUnits_AreNotSquadded()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        var structure = new ObjectDefinition("A4Structure");
        structure.KindOf.Set(ObjectKinds.Structure, true);
        structure.KindOf.Set(ObjectKinds.CanAttack, true);
        game.GameLogic.CreateObject(structure, player);

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);

        Assert.Empty(hierarchy.Squads);
    }
}
