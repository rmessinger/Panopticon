using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Logic.Victory;

public sealed class VictoryOptionsTests : MockedGameTest
{
    [Fact]
    public void Default_NoFrameLimitAndBuildingCount()
    {
        Assert.Equal(VictorySystem.NoFrameLimit, VictoryOptions.Default.FrameLimit);
        Assert.Equal(VictoryScoreMetric.BuildingCount, VictoryOptions.Default.ScoreMetric);
    }

    [Fact]
    public void ConfigurationPath_AppliesFrameLimitAndScoreMetric()
    {
        var scenario = ConfigurePlayersWithCommandCenters();

        // Configure through the public Game configuration path before the match ticks.
        Generals.VictoryOptions = new VictoryOptions(
            (int)VictorySystem.DefaultCheckCadence.Value,
            VictoryScoreMetric.UnitCount);

        // Give each player a different unit/building split so the metric is observable.
        var firstExtraUnit = CreateGameObject(CreateUnitDefinition(), scenario.FirstPlayer);
        var secondExtraStructure = CreateGameObject(CreateStructureDefinition(), scenario.SecondPlayer);
        scenario.FirstPlayer.AddOwnedObject(firstExtraUnit);
        scenario.SecondPlayer.AddOwnedObject(secondExtraStructure);

        RunVictoryCadence();

        Assert.False(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.ScoreDecision, Generals.CurrentOutcome.Reason);
        // UnitCount metric: first player has 1 unit (the extra), second player has 0 units.
        Assert.Same(scenario.FirstPlayer, Generals.CurrentOutcome.WinningPlayer);
    }

    [Fact]
    public void ConfigurationPath_RestoringDefaultLeavesNoFrameLimit()
    {
        ConfigurePlayersWithCommandCenters();

        Generals.VictoryOptions = new VictoryOptions(100, VictoryScoreMetric.UnitCount);
        Assert.Equal(100, Generals.VictorySystem.FrameLimit);

        Generals.VictoryOptions = VictoryOptions.Default;
        Assert.Equal(VictorySystem.NoFrameLimit, Generals.VictorySystem.FrameLimit);
    }

    private ScoreDecisionScenario ConfigurePlayersWithCommandCenters()
    {
        Generals.PlayerManager.OnNewGame(
            [
                Panopticon.Data.Map.Player.CreateNeutralPlayer(),
                Panopticon.Data.Map.Player.CreateCivilianPlayer(),
                new Panopticon.Data.Map.Player
                {
                    Name = "player1",
                    Faction = "FactionAmerica",
                    DisplayName = "Player 1",
                    IsHuman = true,
                },
                new Panopticon.Data.Map.Player
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
        var firstCommandCenter = CreateGameObject(CreateCommandCenterDefinition(), firstPlayer);
        var secondCommandCenter = CreateGameObject(CreateCommandCenterDefinition(), secondPlayer);
        firstPlayer.AddOwnedObject(firstCommandCenter);
        secondPlayer.AddOwnedObject(secondCommandCenter);

        return new ScoreDecisionScenario(firstPlayer, secondPlayer);
    }

    private static ObjectDefinition CreateCommandCenterDefinition()
    {
        var definition = new ObjectDefinition("VictoryOptionsCommandCenter");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        definition.KindOf.Set(ObjectKinds.CommandCenter, true);
        return definition;
    }

    private static ObjectDefinition CreateStructureDefinition()
    {
        var definition = new ObjectDefinition("VictoryOptionsStructure");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        return definition;
    }

    private static ObjectDefinition CreateUnitDefinition()
    {
        return new ObjectDefinition("VictoryOptionsUnit");
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

    private sealed record ScoreDecisionScenario(Player FirstPlayer, Player SecondPlayer);
}
