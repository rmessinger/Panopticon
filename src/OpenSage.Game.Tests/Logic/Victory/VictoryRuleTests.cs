using System.Collections.Generic;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.Victory;

public sealed class VictoryRuleTests : MockedGameTest
{
    [Fact]
    public void DefaultRule_DefeatsPlayerWithinCheckCadence()
    {
        var players = ConfigureSkirmishPlayers(-1, -1);
        var winner = players[0];
        var loser = players[1];
        var commandCenter = CreateGameObject(CreateCommandCenterDefinition(500), loser);
        loser.AddOwnedObject(commandCenter);

        commandCenter.Destroy();
        RunVictoryCadence();

        Assert.True(Generals.VictorySystem.IsPlayerDefeated(loser));
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Eliminated, Generals.CurrentOutcome.Reason);
        Assert.Same(winner, Generals.CurrentOutcome.WinningPlayer);
        Assert.Same(loser, Generals.CurrentOutcome.LosingPlayer);
    }

    [Fact]
    public void DefaultRule_PreservesPlayerWithBuilderAndResources()
    {
        var players = ConfigureSkirmishPlayers(-1, -1);
        var player = players[1];
        player.BankAccount.Money = 500;

        var commandCenter = CreateGameObject(CreateCommandCenterDefinition(500), player);
        var builder = CreateGameObject(CreateDozerDefinition(), player);
        player.AddOwnedObject(commandCenter);
        player.AddOwnedObject(builder);

        commandCenter.Destroy();
        RunVictoryCadence();

        Assert.False(Generals.VictorySystem.IsPlayerDefeated(player));
        Assert.Equal(OutcomeStatus.InProgress, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.None, Generals.CurrentOutcome.Reason);
    }

    [Fact]
    public void AllNonAlliesDefeated_DeclaresWinningSide()
    {
        var players = ConfigureSkirmishPlayers(1, 1, 2);
        var firstWinner = players[0];
        var secondWinner = players[1];
        var loser = players[2];
        var commandCenter = CreateGameObject(CreateCommandCenterDefinition(500), loser);
        loser.AddOwnedObject(commandCenter);

        commandCenter.Destroy();
        RunVictoryCadence();

        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Eliminated, Generals.CurrentOutcome.Reason);
        Assert.Null(Generals.CurrentOutcome.WinningPlayer);
        Assert.Equal(2, Generals.CurrentOutcome.WinningPlayers.Count);
        Assert.Contains(firstWinner, Generals.CurrentOutcome.WinningPlayers);
        Assert.Contains(secondWinner, Generals.CurrentOutcome.WinningPlayers);
        Assert.Same(loser, Generals.CurrentOutcome.LosingPlayer);
    }

    [Fact]
    public void VictorySystem_UsesSelectedConditionRule()
    {
        var players = ConfigureSkirmishPlayers(-1, -1);
        var winner = players[0];
        var loser = players[1];
        Generals.VictorySystem.ConditionRule = new AlwaysDefeatedVictoryConditionRule(loser);

        Generals.VictorySystem.QueuePlayerForEvaluation(loser);
        RunVictoryCadence();

        Assert.True(Generals.VictorySystem.IsPlayerDefeated(loser));
        Assert.Same(winner, Generals.CurrentOutcome.WinningPlayer);
        Assert.Equal(OutcomeReason.Eliminated, Generals.CurrentOutcome.Reason);
    }

    private List<Player> ConfigureSkirmishPlayers(params int[] teams)
    {
        var mapPlayers = new List<OpenSage.Data.Map.Player>
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
        };
        var playerSettings = new PlayerSetting[teams.Length];

        for (var i = 0; i < teams.Length; i++)
        {
            mapPlayers.Add(new OpenSage.Data.Map.Player
            {
                Name = $"player{i + 1}",
                Faction = i % 2 == 0 ? "FactionAmerica" : "FactionGLA",
                DisplayName = $"Player {i + 1}",
                IsHuman = i == 0,
            });

            playerSettings[i] = new PlayerSetting(
                i + 1,
                i % 2 == 0 ? "FactionAmerica" : "FactionGLA",
                new ColorRgb((byte)(255 - (i * 20)), (byte)(200 - (i * 20)), (byte)(150 - (i * 10))),
                teams[i],
                i == 0 ? PlayerOwner.Player : PlayerOwner.EasyAi,
                $"player{i + 1}",
                i == 0);
        }

        Generals.PlayerManager.OnNewGame(mapPlayers.ToArray(), GameType.Skirmish, playerSettings);

        var result = new List<Player>(teams.Length);
        for (var i = 0; i < teams.Length; i++)
        {
            result.Add(Generals.PlayerManager.Players[i + 2]);
        }

        return result;
    }

    private static ObjectDefinition CreateCommandCenterDefinition(float buildCost)
    {
        var definition = new ObjectDefinition("VictoryRuleCommandCenter");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        definition.KindOf.Set(ObjectKinds.CommandCenter, true);
        typeof(ObjectDefinition).GetProperty(nameof(ObjectDefinition.BuildCost))!.SetValue(definition, buildCost);
        return definition;
    }

    private static ObjectDefinition CreateDozerDefinition()
    {
        var definition = new ObjectDefinition("VictoryRuleDozer");
        definition.KindOf.Set(ObjectKinds.Dozer, true);
        return definition;
    }

    private GameObject CreateGameObject(ObjectDefinition definition, Player owner)
    {
        return new GameObject(definition, Generals.GameEngine, owner);
    }

    private void RunVictoryCadence()
    {
        for (var i = 0u; i < VictorySystem.DefaultCheckCadence.Value; i++)
        {
            Generals.GameLogic.Update();
        }
    }

    private sealed class AlwaysDefeatedVictoryConditionRule(Player targetPlayer) : VictoryConditionRule
    {
        internal override bool IsPlayerDefeated(Player player, VictorySystem victorySystem)
        {
            return player == targetPlayer;
        }
    }
}
