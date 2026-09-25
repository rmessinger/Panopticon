using System.Collections.Generic;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Programmatic authoring of the six P7 regression scenarios. Each scenario is built with the typed
/// P0 command model (no hand-written JSON), then serialized to the checked-in
/// <c>Scenarios/Regression/*.json</c> files. The serialized form is the static input the loader
/// reads back; keeping authoring in code guarantees the JSON stays valid against the converters.
/// </summary>
public static class RegressionScenarioFactory
{
    private const string MapPath = "maps/alpine assault/alpine assault.map";

    /// <summary>The canonical two-player setup: human (USA) vs AI (GLA), competitive indices 2/3.</summary>
    private static List<PlayerSlotConfig> TwoPlayerSlots(List<ActionEntry> humanActions,
        List<ActionEntry> aiActions) =>
    [
        new()
        {
            Name = "P1-Human",
            FactionIndex = RegressionSuite.HumanPlayerIndex,
            Color = 1,
            StartPosition = 1,
            Team = 1,
            Actions = humanActions,
        },
        new()
        {
            Name = "P2-AI",
            FactionIndex = 4, // GLA
            Color = 2,
            StartPosition = 2,
            Team = 2,
            Actions = aiActions,
        },
    ];

    private static ActionEntry EndGameAction(string label, string id, uint frame) => new(
        label: label,
        command: CommandSpec.CreateOpen(OrderType.EndGame),
        position: null,
        timing: new FixedTiming(frame),
        id: id);

    private static ActionEntry AttackGroundAction(string label, string id, uint frame, float x, float y) => new(
        label: label,
        command: CommandSpec.CreateOpen(
            OrderType.ForceAttackGround,
            new PositionCommandArgument(new NormalizedPosition(x, y))),
        position: null,
        timing: new FixedTiming(frame),
        id: id);

    private static ActionEntry BuildAction(string label, string id, uint frame) => new(
        label: label,
        command: CommandSpec.CreateOpen(
            OrderType.BuildObject,
            new IntegerCommandArgument((int)Panopticon.Content.AssetHash.GetHash("AmericaBarracks")),
            new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)),
            new FloatCommandArgument(0f)),
        position: null,
        timing: new FixedTiming(frame),
        id: id);

    public static ReplayScenario Build(EndState endState) => endState switch
    {
        EndState.PlayerEliminated => new ReplayScenario
        {
            MapPath = MapPath,
            Players = TwoPlayerSlots(
                new(),
                new() { AttackGroundAction("AIAttacksHumanBase", "ai-attack", 60, 0.3f, 0.3f) }),
        },
        EndState.PlayerSurrenders => new ReplayScenario
        {
            MapPath = MapPath,
            Players = TwoPlayerSlots(
                new() { EndGameAction("HumanSurrenders", "human-endgame", 30) },
                new()),
        },
        EndState.AISurrenders => new ReplayScenario
        {
            MapPath = MapPath,
            Players = TwoPlayerSlots(
                new(),
                new() { EndGameAction("AISurrenders", "ai-endgame", 30) }),
        },
        EndState.TeamWin => new ReplayScenario
        {
            MapPath = MapPath,
            Players =
            [
                new()
                {
                    Name = "P1-Human",
                    FactionIndex = RegressionSuite.HumanPlayerIndex,
                    Color = 1,
                    StartPosition = 1,
                    Team = 1,
                    Actions = new() { AttackGroundAction("TeamAAttacksB", "team-a-attack", 60, 0.7f, 0.7f) },
                },
                new()
                {
                    Name = "P2-Ally",
                    FactionIndex = 2,
                    Color = 1,
                    StartPosition = 2,
                    Team = 1,
                    Actions = new(),
                },
                new()
                {
                    Name = "P3-EnemyA",
                    FactionIndex = 4,
                    Color = 2,
                    StartPosition = 3,
                    Team = 2,
                    Actions = new(),
                },
                new()
                {
                    Name = "P4-EnemyB",
                    FactionIndex = 4,
                    Color = 2,
                    StartPosition = 4,
                    Team = 2,
                    Actions = new(),
                },
            ],
        },
        EndState.MutualElimination => new ReplayScenario
        {
            MapPath = MapPath,
            Players = TwoPlayerSlots(
                new() { AttackGroundAction("HumanAttacksAI", "human-attack", 60, 0.7f, 0.7f) },
                new() { AttackGroundAction("AIAttacksHuman", "ai-attack", 60, 0.3f, 0.3f) }),
        },
        EndState.ScoreDecision => new ReplayScenario
        {
            MapPath = MapPath,
            Players = TwoPlayerSlots(
                new() { BuildAction("HumanBuildsForScore", "human-build", 10) },
                new()),
        },
        _ => throw new System.ArgumentOutOfRangeException(nameof(endState)),
    };
}
