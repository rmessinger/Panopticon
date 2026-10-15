using System.Reflection;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Tests;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// Shared install-free builders for hierarchy tests: an AI player slot on a mocked game and
/// synthetic attack-capable units (the SquadHierarchyTests pattern, shared since WI-M4-02).
/// </summary>
internal static class HierarchyTestPlayers
{
    public static Player CreateAiPlayer(MockedGameTest.TestGame game)
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

    public static GameObject SpawnUnit(MockedGameTest.TestGame game, Player owner, string name, float buildCost)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        SetProperty(definition, nameof(ObjectDefinition.BuildCost), buildCost);
        return game.GameLogic.CreateObject(definition, owner);
    }

    public static GameObject SpawnAircraft(MockedGameTest.TestGame game, Player owner, string name)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Aircraft, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        SetProperty(definition, nameof(ObjectDefinition.BuildCost), 100f);
        return game.GameLogic.CreateObject(definition, owner);
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }
}
