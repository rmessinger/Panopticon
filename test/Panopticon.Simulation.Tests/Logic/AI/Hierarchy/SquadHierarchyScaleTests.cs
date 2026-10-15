using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-M4-03: the squad invariant at army scale, install-free: 1000 synthetic units assign into
/// full squads with commanders, mass casualties never leave a live member unassigned or a
/// non-empty squad commanderless, and aircraft stay out of ground squads.
/// </summary>
public sealed class SquadHierarchyScaleTests : MockedGameTest
{
    private const int ArmySize = 1000;

    [Fact]
    public void ThousandUnits_AssignIntoFullSquadsWithCommanders()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var units = new List<GameObject>(ArmySize);
        for (var i = 0; i < ArmySize; i++)
        {
            units.Add(HierarchyTestPlayers.SpawnUnit(game, player, $"M4ScaleUnit{i}", buildCost: 100f + (i % 8)));
        }

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);

        Assert.Equal(ArmySize / SquadHierarchy.MaxSquadSize, hierarchy.Squads.Count);
        Assert.Equal(ArmySize, hierarchy.Squads.Sum(squad => squad.Members.Count));
        Assert.All(
            hierarchy.Squads,
            squad => Assert.InRange(squad.Members.Count, 1, SquadHierarchy.MaxSquadSize));
        Assert.All(
            hierarchy.Squads,
            squad => Assert.NotNull(squad.CommanderId));
        Assert.All(
            units,
            unit => Assert.NotNull(hierarchy.SquadOf(unit)));
    }

    [Fact]
    public void MassCasualties_KeepEverySurvivorAssignedAndCommanded()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var units = new List<GameObject>(ArmySize);
        for (var i = 0; i < ArmySize; i++)
        {
            units.Add(HierarchyTestPlayers.SpawnUnit(game, player, $"M4CasualtyUnit{i}", buildCost: 100f + (i % 8)));
        }

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);

        // Every squad loses its commander, then half of all units die: whole squads disappear
        // and survivors promote in the same synchronize.
        foreach (var squad in hierarchy.Squads)
        {
            game.GameLogic.DestroyObject(squad.Members[0]);
        }

        var stillAlive = units.Where(unit => !unit.IsDestroyed).ToList();
        foreach (var unit in stillAlive.Take(stillAlive.Count / 2))
        {
            game.GameLogic.DestroyObject(unit);
        }

        hierarchy.Synchronize(player);

        var survivors = units.Where(unit => !unit.IsDestroyed).ToList();
        Assert.Equal(survivors.Count, hierarchy.Squads.Sum(squad => squad.Members.Count));
        Assert.All(
            hierarchy.Squads,
            squad => Assert.True(
                squad.Members.Count == 0
                || squad.CommanderId is { } commander && squad.Members.Any(member => member.Id == commander)));
        Assert.All(
            survivors,
            unit => Assert.NotNull(hierarchy.SquadOf(unit)));
    }

    [Fact]
    public void Aircraft_AreExcludedFromGroundSquads()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var groundA = HierarchyTestPlayers.SpawnUnit(game, player, "M4AircraftExclusionGroundA", buildCost: 100f);
        var groundB = HierarchyTestPlayers.SpawnUnit(game, player, "M4AircraftExclusionGroundB", buildCost: 100f);
        var aircraftA = HierarchyTestPlayers.SpawnAircraft(game, player, "M4AircraftExclusionAirA");
        var aircraftB = HierarchyTestPlayers.SpawnAircraft(game, player, "M4AircraftExclusionAirB");

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);

        Assert.Equal(2, hierarchy.Squads.Sum(squad => squad.Members.Count));
        Assert.NotNull(hierarchy.SquadOf(groundA));
        Assert.NotNull(hierarchy.SquadOf(groundB));
        Assert.Null(hierarchy.SquadOf(aircraftA));
        Assert.Null(hierarchy.SquadOf(aircraftB));
    }
}
