using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.Victory;

public sealed class TimeLimitScoreDecisionTests : MockedGameTest
{
    [Fact]
    public void FrameLimit_DisabledByDefault()
    {
        ConfigurePlayersWithCommandCenters();

        RunVictoryCadence(2);

        Assert.Equal(VictorySystem.NoFrameLimit, Generals.VictorySystem.FrameLimit);
        Assert.Equal(OutcomeStatus.InProgress, Generals.CurrentOutcome.Status);
    }

    [Fact]
    public void FrameLimit_ChoosesHighestScoringPlayer()
    {
        var scenario = ConfigurePlayersWithCommandCenters();
        var extraStructure = CreateGameObject(CreateStructureDefinition(), scenario.FirstPlayer);
        scenario.FirstPlayer.AddOwnedObject(extraStructure);
        Generals.VictorySystem.FrameLimit = (int)VictorySystem.DefaultCheckCadence.Value;

        RunVictoryCadence();

        Assert.False(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.ScoreDecision, Generals.CurrentOutcome.Reason);
        Assert.Equal((int)VictorySystem.DefaultCheckCadence.Value, Generals.CurrentOutcome.DecidedOnFrame);
        Assert.Same(scenario.FirstPlayer, Generals.CurrentOutcome.WinningPlayer);
    }

    [Fact]
    public void FrameLimit_TieProducesSharedVictory()
    {
        var scenario = ConfigurePlayersWithCommandCenters();
        Generals.VictorySystem.FrameLimit = (int)VictorySystem.DefaultCheckCadence.Value;

        RunVictoryCadence();

        Assert.False(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.ScoreDecision, Generals.CurrentOutcome.Reason);
        Assert.Null(Generals.CurrentOutcome.WinningPlayer);
        Assert.Equal(2, Generals.CurrentOutcome.WinningPlayers.Count);
        Assert.Contains(scenario.FirstPlayer, Generals.CurrentOutcome.WinningPlayers);
        Assert.Contains(scenario.SecondPlayer, Generals.CurrentOutcome.WinningPlayers);
    }

    private ScoreDecisionScenario ConfigurePlayersWithCommandCenters()
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

        var firstPlayer = Generals.PlayerManager.Players[2];
        var secondPlayer = Generals.PlayerManager.Players[3];
        var firstCommandCenter = CreateGameObject(CreateCommandCenterDefinition(500), firstPlayer);
        var secondCommandCenter = CreateGameObject(CreateCommandCenterDefinition(500), secondPlayer);
        firstPlayer.AddOwnedObject(firstCommandCenter);
        secondPlayer.AddOwnedObject(secondCommandCenter);

        return new ScoreDecisionScenario(firstPlayer, secondPlayer);
    }

    private static ObjectDefinition CreateCommandCenterDefinition(float buildCost)
    {
        var definition = new ObjectDefinition("ScoreDecisionCommandCenter");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        definition.KindOf.Set(ObjectKinds.CommandCenter, true);
        typeof(ObjectDefinition).GetProperty(nameof(ObjectDefinition.BuildCost))!.SetValue(definition, buildCost);
        return definition;
    }

    private static ObjectDefinition CreateStructureDefinition()
    {
        var definition = new ObjectDefinition("ScoreDecisionStructure");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        return definition;
    }

    private GameObject CreateGameObject(ObjectDefinition definition, Player owner)
    {
        return new GameObject(definition, Generals.GameEngine, owner);
    }

    private void RunVictoryCadence(int count = 1)
    {
        for (var iteration = 0; iteration < count; iteration++)
        {
            for (var i = 0u; i < VictorySystem.DefaultCheckCadence.Value; i++)
            {
                Generals.GameLogic.Update();
            }
        }
    }

    private sealed record ScoreDecisionScenario(Player FirstPlayer, Player SecondPlayer);
}
