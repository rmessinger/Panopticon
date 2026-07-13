using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.Victory;

public sealed class SuddenDeathOutcomeTests : MockedGameTest
{
    [Fact]
    public void SimultaneousElimination_TransitionsToSuddenDeath()
    {
        var scenario = ConfigureSuddenDeathScenario();

        scenario.FirstCommandCenter.Destroy();
        scenario.SecondCommandCenter.Destroy();
        RunVictoryCadence();

        Assert.True(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.SuddenDeath, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.None, Generals.CurrentOutcome.Reason);
        Assert.Null(Generals.CurrentOutcome.DecidedOnFrame);
        Assert.Null(Generals.CurrentOutcome.WinningPlayer);
    }

    [Fact]
    public void SuddenDeath_ResolvesOnNextElimination()
    {
        var scenario = ConfigureSuddenDeathScenario();

        scenario.FirstCommandCenter.Destroy();
        scenario.SecondCommandCenter.Destroy();
        RunVictoryCadence();
        scenario.SecondUnit.Destroy();
        RunVictoryCadence();

        Assert.False(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Eliminated, Generals.CurrentOutcome.Reason);
        Assert.Equal((int)(VictorySystem.DefaultCheckCadence.Value * 2u), Generals.CurrentOutcome.DecidedOnFrame);
        Assert.Same(scenario.FirstPlayer, Generals.CurrentOutcome.WinningPlayer);
        Assert.Same(scenario.SecondPlayer, Generals.CurrentOutcome.LosingPlayer);
    }

    private SuddenDeathScenario ConfigureSuddenDeathScenario()
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
        var firstUnit = CreateGameObject(CreateUnitDefinition(), firstPlayer);
        var secondUnit = CreateGameObject(CreateUnitDefinition(), secondPlayer);

        firstPlayer.AddOwnedObject(firstCommandCenter);
        secondPlayer.AddOwnedObject(secondCommandCenter);
        firstPlayer.AddOwnedObject(firstUnit);
        secondPlayer.AddOwnedObject(secondUnit);

        return new SuddenDeathScenario(firstPlayer, secondPlayer, firstCommandCenter, secondCommandCenter, firstUnit, secondUnit);
    }

    private static ObjectDefinition CreateCommandCenterDefinition(float buildCost)
    {
        var definition = new ObjectDefinition("SuddenDeathCommandCenter");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        definition.KindOf.Set(ObjectKinds.CommandCenter, true);
        typeof(ObjectDefinition).GetProperty(nameof(ObjectDefinition.BuildCost))!.SetValue(definition, buildCost);
        return definition;
    }

    private static ObjectDefinition CreateUnitDefinition()
    {
        return new ObjectDefinition("SuddenDeathUnit");
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

    private sealed record SuddenDeathScenario(
        Player FirstPlayer,
        Player SecondPlayer,
        GameObject FirstCommandCenter,
        GameObject SecondCommandCenter,
        GameObject FirstUnit,
        GameObject SecondUnit);
}
