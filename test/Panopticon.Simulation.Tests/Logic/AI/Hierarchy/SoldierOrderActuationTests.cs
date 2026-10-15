using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-M4-01 audit evidence: the reference hierarchy emits soldier orders that carry no
/// selection, and the engine applies move and attack orders to the player's current
/// selection, so on a headless battlefield (empty selection) the hierarchy cannot actuate
/// any unit. WI-M4-03 adds the per-squad selection actuation this test pins as missing.
/// </summary>
public sealed class SoldierOrderActuationTests : MockedGameTest
{
    [Fact]
    public void DecideNextOrders_EmitsSoldierOrders_ButNoSelectionOrder()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = CreateAiPlayer(game);
        SpawnUnit(game, player, "M4ActuationUnitA", buildCost: 100f);
        SpawnUnit(game, player, "M4ActuationUnitB", buildCost: 200f);

        var policy = new HierarchicalAIDecisionPolicy(
            new RuleBasedGeneralPolicy(),
            new RuleBasedCommanderDecisionPolicy(),
            new RuleBasedSoldierDecisionPolicy());

        var orders = policy.DecideNextOrders(new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: 1));

        // Soldiers decide (defend posture: move goals to the squad centroid)...
        Assert.Contains(orders, o => o.OrderType is OrderType.MoveTo or OrderType.AttackObject);

        // ...but nothing selects the squads those orders belong to, so OrderProcessor would
        // apply them to whatever is currently selected (nothing, headless).
        Assert.DoesNotContain(orders, o => o.OrderType == OrderType.SetSelection);
    }

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
}
