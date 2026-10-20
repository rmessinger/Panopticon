using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Logic.Victory;

/// <summary>
/// WI-M5-03: battles without production structures reach a P3 outcome. A side is eliminated
/// when it owns no living non-structure attacker (an airfield alone is defeated, parked
/// aircraft survive), mutual elimination goes to sudden death, and the frame limit with
/// unit-count scoring decides stalled battles.
/// </summary>
public sealed class ArmyEliminatedVictoryConditionRuleTests : MockedGameTest
{
    [Fact]
    public void EliminatedSide_DecidesWithSurvivorAsWinner()
    {
        var scenario = ConfigureArmyPlayers();
        Generals.VictorySystem.ConditionRule = new ArmyEliminatedVictoryConditionRule();
        Generals.IsLogicRunning = true;

        var survivorUnit = CreateGameObject(CreateCombatUnitDefinition(), scenario.Winner);
        scenario.Winner.AddOwnedObject(survivorUnit);
        Generals.VictorySystem.QueuePlayerForEvaluation(scenario.Loser);

        RunVictoryCadence();

        Assert.False(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Eliminated, Generals.CurrentOutcome.Reason);
        Assert.Same(scenario.Winner, Generals.CurrentOutcome.WinningPlayer);
        Assert.Same(scenario.Loser, Generals.CurrentOutcome.LosingPlayer);
    }

    [Fact]
    public void MutualEliminationInOneCheck_GoesToSuddenDeath()
    {
        var scenario = ConfigureArmyPlayers();
        Generals.VictorySystem.ConditionRule = new ArmyEliminatedVictoryConditionRule();
        Generals.IsLogicRunning = true;

        Generals.VictorySystem.QueuePlayerForEvaluation(scenario.Winner);
        Generals.VictorySystem.QueuePlayerForEvaluation(scenario.Loser);

        RunVictoryCadence();

        Assert.True(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.SuddenDeath, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.None, Generals.CurrentOutcome.Reason);
        Assert.Null(Generals.CurrentOutcome.WinningPlayer);
    }

    [Fact]
    public void SideHoldingOnlyAnAirfield_IsDefeated()
    {
        var scenario = ConfigureArmyPlayers();
        Generals.VictorySystem.ConditionRule = new ArmyEliminatedVictoryConditionRule();
        Generals.IsLogicRunning = true;

        var survivorUnit = CreateGameObject(CreateCombatUnitDefinition(), scenario.Winner);
        scenario.Winner.AddOwnedObject(survivorUnit);
        var airfield = CreateGameObject(CreateAirfieldDefinition(), scenario.Loser);
        scenario.Loser.AddOwnedObject(airfield);
        Generals.VictorySystem.QueuePlayerForEvaluation(scenario.Loser);

        RunVictoryCadence();

        Assert.True(Generals.VictorySystem.IsPlayerDefeated(scenario.Loser));
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Eliminated, Generals.CurrentOutcome.Reason);
        Assert.Same(scenario.Winner, Generals.CurrentOutcome.WinningPlayer);
    }

    [Fact]
    public void ParkedAircraft_KeepTheSideAlive()
    {
        var scenario = ConfigureArmyPlayers();
        Generals.VictorySystem.ConditionRule = new ArmyEliminatedVictoryConditionRule();
        Generals.IsLogicRunning = true;

        var winnerUnit = CreateGameObject(CreateCombatUnitDefinition(), scenario.Winner);
        scenario.Winner.AddOwnedObject(winnerUnit);
        var parkedJet = CreateGameObject(CreateAircraftDefinition(), scenario.Loser);
        scenario.Loser.AddOwnedObject(parkedJet);
        Generals.VictorySystem.QueuePlayerForEvaluation(scenario.Loser);

        RunVictoryCadence();

        Assert.False(Generals.VictorySystem.IsPlayerDefeated(scenario.Loser));
        Assert.Equal(OutcomeStatus.InProgress, Generals.CurrentOutcome.Status);
    }

    [Fact]
    public void OptionsPath_AppliesArmyEliminatedRule_AndFrameLimitScoresByUnitCount()
    {
        var scenario = ConfigureArmyPlayers();
        Generals.IsLogicRunning = true;

        // The public configuration path selects the rule (WI-M5-03) the same way it selects
        // the frame limit; both sides field living attackers so the elimination rule holds.
        Generals.VictoryOptions = new VictoryOptions(
            (int)VictorySystem.DefaultCheckCadence.Value,
            VictoryScoreMetric.UnitCount,
            VictoryConditionKind.ArmyEliminated);
        for (var i = 0; i < 3; i++)
        {
            scenario.Winner.AddOwnedObject(CreateGameObject(CreateCombatUnitDefinition(), scenario.Winner));
        }

        var loserUnit = CreateGameObject(CreateCombatUnitDefinition(), scenario.Loser);
        scenario.Loser.AddOwnedObject(loserUnit);

        RunVictoryCadence();

        Assert.False(Generals.IsLogicRunning);
        Assert.Equal(OutcomeStatus.Decided, Generals.CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.ScoreDecision, Generals.CurrentOutcome.Reason);
        Assert.Same(scenario.Winner, Generals.CurrentOutcome.WinningPlayer);
    }

    private ArmyScenario ConfigureArmyPlayers()
    {
        Generals.PlayerManager.OnNewGame(
            [
                Panopticon.Data.Map.Player.CreateNeutralPlayer(),
                Panopticon.Data.Map.Player.CreateCivilianPlayer(),
                new Panopticon.Data.Map.Player
                {
                    Name = "armyA",
                    Faction = "FactionAmerica",
                    DisplayName = "Army A",
                    IsHuman = true,
                },
                new Panopticon.Data.Map.Player
                {
                    Name = "armyB",
                    Faction = "FactionGLA",
                    DisplayName = "Army B",
                    IsHuman = false,
                },
            ],
            GameType.Skirmish,
            [
                new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), -1, PlayerOwner.Player, "armyA", true),
                new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 128, 64), -1, PlayerOwner.EasyAi, "armyB"),
            ]);

        return new ArmyScenario(Generals.PlayerManager.Players[2], Generals.PlayerManager.Players[3]);
    }

    private static ObjectDefinition CreateCombatUnitDefinition()
    {
        var definition = new ObjectDefinition("ArmyEliminatedCombatUnit");
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        return definition;
    }

    private static ObjectDefinition CreateAircraftDefinition()
    {
        var definition = new ObjectDefinition("ArmyEliminatedAircraft");
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        definition.KindOf.Set(ObjectKinds.Aircraft, true);
        return definition;
    }

    private static ObjectDefinition CreateAirfieldDefinition()
    {
        var definition = new ObjectDefinition("ArmyEliminatedAirfield");
        definition.KindOf.Set(ObjectKinds.Structure, true);
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

    private sealed record ArmyScenario(Player Winner, Player Loser);
}
