using System.IO;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-A4-19: the squad hierarchy persists as a version-gated block of identities and object
/// ids; restored ids resolve lazily against live objects, and members that no longer exist
/// are dropped on the next synchronize.
/// </summary>
public sealed class SquadHierarchyPersistenceTests : MockedGameTest
{
    private static GameObject SpawnUnit(TestGame game, Player owner, string name, float buildCost)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildCost))!
            .SetValue(definition, buildCost);
        return game.GameLogic.CreateObject(definition, owner);
    }

    private static Player CreateAiPlayer(TestGame game)
    {
        var mapPlayers = new[]
        {
            Panopticon.Data.Map.Player.CreateNeutralPlayer(),
            Panopticon.Data.Map.Player.CreateCivilianPlayer(),
            new Panopticon.Data.Map.Player
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
    public void HierarchyState_RoundTripsThroughTheStatePersister()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        SpawnUnit(game, player, "A4PersistCheap", buildCost: 100f);
        SpawnUnit(game, player, "A4PersistCommander", buildCost: 2000f);

        var hierarchy = new SquadHierarchy();
        hierarchy.Synchronize(player);
        var captured = hierarchy.CaptureState();
        Assert.Single(captured.Squads);

        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            using (var writer = new StateWriter(stream, game))
            {
                writer.PersistObject(captured);
            }

            bytes = stream.ToArray();
        }

        SquadHierarchyState reloaded;
        using (var reader = new StateReader(new MemoryStream(bytes), game))
        {
            reloaded = new SquadHierarchyState();
            reader.PersistObject(reloaded);
        }

        var original = captured.Squads.Single();
        var restored = reloaded.Squads.Single();
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.CommanderId, restored.CommanderId);
        Assert.Equal(original.MemberIds, restored.MemberIds);
    }

    [Fact]
    public void Restore_ResolvesMembersOnNextSynchronize_AndDropsDeadOnes()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        var commander = SpawnUnit(game, player, "A4RestoreCommander", buildCost: 2000f);
        var soldier = SpawnUnit(game, player, "A4RestoreSoldier", buildCost: 100f);
        var doomed = SpawnUnit(game, player, "A4RestoreDoomed", buildCost: 50f);

        var original = new SquadHierarchy();
        original.Synchronize(player);
        var state = original.CaptureState();

        // Simulate load into a fresh hierarchy whose owner has one fewer live object.
        game.GameLogic.DestroyObject(doomed);
        var restored = new SquadHierarchy();
        restored.Restore(state);

        // Resolution is lazy: members appear only after the next Synchronize.
        restored.Synchronize(player);

        var squad = Assert.Single(restored.Squads);
        Assert.Equal(2, squad.Members.Count);
        Assert.Contains(squad.Members, m => m.Id == commander.Id);
        Assert.Contains(squad.Members, m => m.Id == soldier.Id);
        Assert.DoesNotContain(squad.Members, m => m.Id == doomed.Id);
        Assert.Equal(commander.Id, squad.CommanderId);
    }

    [Fact]
    public void CaptureRestore_PreservesSquadIdentity()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        SpawnUnit(game, player, "A4IdentityUnit", buildCost: 100f);

        var original = new SquadHierarchy();
        original.Synchronize(player);
        var state = original.CaptureState();

        var restored = new SquadHierarchy();
        restored.Restore(state);
        restored.Synchronize(player);

        Assert.Equal(
            original.Squads.Select(s => s.Id),
            restored.Squads.Select(s => s.Id));
    }
}
