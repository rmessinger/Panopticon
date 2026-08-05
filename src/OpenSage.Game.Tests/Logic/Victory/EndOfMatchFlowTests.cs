using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Logic.Victory;

public sealed class EndOfMatchFlowTests : MockedGameTest
{
    [Fact]
    public void EliminatedOutcome_StopsLogicAndRecordsDecidedFrame()
    {
        ConfigureTwoPlayerSkirmish();
        var winner = Generals.PlayerManager.Players[2];
        var loser = Generals.PlayerManager.Players[3];
        var commandCenter = CreateGameObject(CreateCommandCenterDefinition(500), loser);
        loser.AddOwnedObject(commandCenter);

        commandCenter.Destroy();
        RunVictoryCadence();

        Assert.False(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Eliminated, Generals.CurrentOutcome.Reason);
        Assert.Equal((int)VictorySystem.DefaultCheckCadence.Value, Generals.CurrentOutcome.DecidedOnFrame);
        Assert.Same(winner, Generals.CurrentOutcome.WinningPlayer);
        Assert.Same(loser, Generals.CurrentOutcome.LosingPlayer);
    }

    [Fact]
    public void EndGameOrder_SurrendersIssuingPlayer()
    {
        ConfigureTwoPlayerSkirmish();
        var orderProcessor = new OrderProcessor(Generals);
        var order = new Order(3, OrderType.EndGame);

        orderProcessor.Process([order]);

        Assert.False(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Surrendered, Generals.CurrentOutcome.Reason);
        Assert.Equal(0, Generals.CurrentOutcome.DecidedOnFrame);
        Assert.Equal(2, Generals.PlayerManager.GetPlayerIndex(Generals.CurrentOutcome.WinningPlayer));
        Assert.Equal(3, Generals.PlayerManager.GetPlayerIndex(Generals.CurrentOutcome.LosingPlayer));
    }

    [Fact]
    public void MatchOutcomeDecided_FiresExactlyOnceOnTransitionToDecided()
    {
        ConfigureTwoPlayerSkirmish();
        var losingPlayer = Generals.PlayerManager.Players[3];
        var losingCommandCenter = CreateGameObject(CreateCommandCenterDefinition(500), losingPlayer);
        losingPlayer.AddOwnedObject(losingCommandCenter);

        var fireCount = 0;
        Generals.MatchOutcomeDecided += (_, _) => fireCount++;

        losingCommandCenter.Destroy();
        RunVictoryCadence();

        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(1, fireCount);

        // A second cadence tick must not re-fire: the match is already decided.
        RunVictoryCadence();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void OutcomeAssertion_WrongExpectedResult_FailsLoudly()
    {
        ConfigureTwoPlayerSkirmish();
        var losingPlayer = Generals.PlayerManager.Players[3];
        Assert.True(Generals.VictorySystem.Surrender(losingPlayer));

        var assertion = new OutcomeAssertion
        {
            Id = "wrong-outcome",
            ExpectedStatus = "Decided",
            ExpectedReason = "Surrendered",
            ExpectedWinningPlayerIndex = 3,
            ExpectedLosingPlayerIndex = 2,
            ExpectedDecidedFrame = 1,
        };

        var result = assertion.Evaluate(Generals, 0);

        Assert.False(result.Passed);
        Assert.Contains("winning player '3'", result.Message);
        Assert.Contains("losing player '2'", result.Message);
        Assert.Contains("decided frame '1'", result.Message);
    }

    private void ConfigureTwoPlayerSkirmish()
    {
        Generals.PlayerManager.OnNewGame(
            [
                OpenSage.Data.Map.Player.CreateNeutralPlayer(),
                OpenSage.Data.Map.Player.CreateCivilianPlayer(),
                new OpenSage.Data.Map.Player
                {
                    Name = "player1",
                    Faction = "FactionAmerica",
                    DisplayName = "Player 1",
                    IsHuman = true,
                },
                new OpenSage.Data.Map.Player
                {
                    Name = "player2",
                    Faction = "FactionGLA",
                    DisplayName = "Player 2",
                    IsHuman = false,
                },
            ],
            GameType.Skirmish,
            [
                new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), -1, PlayerOwner.Player, "player1", true),
                new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 128, 64), -1, PlayerOwner.EasyAi, "player2"),
            ]);

        Generals.IsLogicRunning = true;
    }

    private static ObjectDefinition CreateCommandCenterDefinition(float buildCost)
    {
        var definition = new ObjectDefinition("EndMatchCommandCenter");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        definition.KindOf.Set(ObjectKinds.CommandCenter, true);
        typeof(ObjectDefinition).GetProperty(nameof(ObjectDefinition.BuildCost))!.SetValue(definition, buildCost);
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
}
