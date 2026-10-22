using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Tests;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-M5-01: ArmyAiSides applies the hierarchical policy to every side setting before game
/// start, resolves each side's player/AI/policy after it, disables AI on non-side players, and
/// the policy's public stats view matches the hierarchy it maintains.
/// </summary>
public sealed class ArmyAiSidesTests : MockedGameTest
{
    private static TestGame CreateTwoSideGame(PlayerSetting[] settings)
    {
        var game = new TestGame(SageGame.CncGenerals);

        // FromMapData only wires a SkirmishAIPlayer when the faction template resolves.
        var template = new PlayerTemplate();
        typeof(PlayerTemplate)
            .GetMethod("SetNameAndInstanceId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(template, ["PlayerTemplate", "FactionAmerica"]);
        game.AssetStore.PlayerTemplates.Add(template);

        var mapPlayers = new[]
        {
            Panopticon.Data.Map.Player.CreateNeutralPlayer(),
            Panopticon.Data.Map.Player.CreateCivilianPlayer(),
            MapPlayer("sideA"),
            MapPlayer("sideB"),
            MapPlayer("bystander"),
        };
        game.PlayerManager.OnNewGame(mapPlayers, GameType.Skirmish, settings);
        return game;
    }

    private static Panopticon.Data.Map.Player MapPlayer(string name) => new()
    {
        Name = name,
        Faction = "FactionAmerica",
        DisplayName = name,
        IsHuman = false,
    };

    private static PlayerSetting[] TwoSideSettings() =>
    [
        new PlayerSetting(0, "FactionAmerica", new Panopticon.Mathematics.ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "sideA"),
        new PlayerSetting(1, "FactionAmerica", new Panopticon.Mathematics.ColorRgb(0, 0, 255), 1, PlayerOwner.EasyAi, "sideB"),
    ];

    [Fact]
    public void ApplyPolicies_MarksEverySideHierarchical()
    {
        var settings = TwoSideSettings();
        ArmyAiSides.ApplyPolicies(settings);
        Assert.All(settings, setting => Assert.Equal(AIDecisionPolicyType.Hierarchical, setting.AIPolicy!.Type));
    }

    [Fact]
    public void Resolve_ReturnsSidesInOrder_AndDisablesNonSidePlayers()
    {
        var settings = TwoSideSettings();
        ArmyAiSides.ApplyPolicies(settings);
        var game = CreateTwoSideGame(settings);
        var players = game.PlayerManager.Players;

        var bystander = players[4];
        Assert.NotNull(bystander.AIPlayer);

        var sides = ArmyAiSides.Resolve(players, ["sideA", "sideB"]);

        Assert.Equal(["sideA", "sideB"], sides.Select(side => side.SideId));
        Assert.Equal([players[2], players[3]], sides.Select(side => side.Player));
        Assert.All(sides, side => Assert.Same(side.Player.AIPlayer, side.Ai));
        Assert.All(sides, side => Assert.Same(side.Ai.DecisionPolicy, side.Policy));
        Assert.All(sides, side => Assert.IsType<HierarchicalAIDecisionPolicy>(side.Policy));

        Assert.Null(bystander.AIPlayer);
        Assert.All(sides, side => Assert.NotNull(side.Player.AIPlayer));
    }

    [Fact]
    public void SidePlayers_ReturnsTheSideSlots_WithoutTouchingAI()
    {
        var settings = TwoSideSettings();
        var game = CreateTwoSideGame(settings);
        var players = game.PlayerManager.Players;
        var bystander = players[4];
        var bystanderAi = bystander.AIPlayer;

        var sidePlayers = ArmyAiSides.SidePlayers(players, sideCount: 2);

        Assert.Equal([players[2], players[3]], sidePlayers);
        Assert.Same(bystanderAi, bystander.AIPlayer);
    }

    [Fact]
    public void GetStats_MatchesTheHierarchy()
    {
        var settings = TwoSideSettings();
        ArmyAiSides.ApplyPolicies(settings);
        var game = CreateTwoSideGame(settings);
        var sides = ArmyAiSides.Resolve(game.PlayerManager.Players, ["sideA", "sideB"]);
        var sideA = sides[0];

        for (var i = 0; i < 10; i++)
        {
            HierarchyTestPlayers.SpawnUnit(game, sideA.Player, $"M5SideUnit{i}", buildCost: 100f);
        }

        var before = sideA.Policy.GetStats(sideA.Player);
        Assert.Equal(0, before.GroundSquadCount);
        Assert.Equal(0, before.GroundMemberCount);
        Assert.Equal(10, before.UnassignedEligibleGroundCount);

        // One decision tick synchronizes the hierarchy (the same path AIPlayer.Update drives).
        sideA.Policy.DecideNextOrders(new AIDecisionContext(sideA.Player, (int)sideA.Player.Id, decisionTickCount: 1));

        var after = sideA.Policy.GetStats(sideA.Player);
        Assert.Equal(2, after.GroundSquadCount);
        Assert.Equal(10, after.GroundMemberCount);
        Assert.Equal(0, after.CommanderlessSquadCount);
        Assert.Equal(0, after.UnassignedEligibleGroundCount);
    }
}
