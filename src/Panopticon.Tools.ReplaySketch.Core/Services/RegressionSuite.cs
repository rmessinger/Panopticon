using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// The P7 regression suite: the six end-state scenario descriptors, the universal invariant set,
/// and the checked-in scenario-file loader. <see cref="BuildMatrix"/> returns the canonical
/// single-axis (endState) matrix; each cell carries its outcome assertion and the three universal
/// invariants.
/// </summary>
public static class RegressionSuite
{
    /// <summary>Directory holding the checked-in scenario JSON files, relative to the Core project.</summary>
    public const string ScenarioDirectory = "Scenarios/Regression";

    // Competitive-player indices in the standard P7 two-/four-player skirmish setup
    // (0 = neutral, 1 = civilian, >= 2 = competitive). Used by the scenario descriptors so the
    // outcome assertion names the correct winner/loser without magic numbers in tests.
    public const int HumanPlayerIndex = 2;
    public const int AiPlayerIndex = 3;

    public static readonly IReadOnlyList<RegressionScenario> Scenarios =
    [
        new RegressionScenario
        {
            EndState = EndState.PlayerEliminated,
            ScenarioFileName = "player-eliminated.json",
            ExpectedStatus = "Decided",
            ExpectedReason = "Eliminated",
            ExpectedWinningPlayerIndex = AiPlayerIndex,
            ExpectedLosingPlayerIndex = HumanPlayerIndex,
        },
        new RegressionScenario
        {
            EndState = EndState.PlayerSurrenders,
            ScenarioFileName = "player-surrenders.json",
            ExpectedStatus = "Decided",
            ExpectedReason = "Surrendered",
            ExpectedWinningPlayerIndex = AiPlayerIndex,
            ExpectedLosingPlayerIndex = HumanPlayerIndex,
        },
        new RegressionScenario
        {
            EndState = EndState.AISurrenders,
            ScenarioFileName = "ai-surrenders.json",
            ExpectedStatus = "Decided",
            ExpectedReason = "Surrendered",
            ExpectedWinningPlayerIndex = HumanPlayerIndex,
            ExpectedLosingPlayerIndex = AiPlayerIndex,
        },
        new RegressionScenario
        {
            EndState = EndState.TeamWin,
            ScenarioFileName = "team-win.json",
            ExpectedStatus = "Decided",
            ExpectedReason = "Eliminated",
            // Team win resolves multiple WinningPlayers; WinningTeam needs DefaultTeam wiring that
            // only save/load populates, so the assertion checks status+reason and the test verifies
            // the human is among WinningPlayers directly.
        },
        new RegressionScenario
        {
            EndState = EndState.MutualElimination,
            ScenarioFileName = "mutual-elimination.json",
            ExpectedStatus = "Decided",
            ExpectedReason = "Eliminated",
            ExpectedWinningPlayerIndex = AiPlayerIndex,
            ExpectedLosingPlayerIndex = HumanPlayerIndex,
        },
        new RegressionScenario
        {
            EndState = EndState.ScoreDecision,
            ScenarioFileName = "score-decision.json",
            ExpectedStatus = "Decided",
            ExpectedReason = "ScoreDecision",
            ExpectedWinningPlayerIndex = HumanPlayerIndex,
        },
    ];

    public static RegressionScenario Get(EndState endState) =>
        Scenarios.First(s => s.EndState == endState);

    /// <summary>The single-axis (endState) matrix with the six terminal cells.</summary>
    public static ScenarioMatrix BuildMatrix()
    {
        var endStateValues = Scenarios.Select(s => s.EndState.ToString()).ToList();
        return new ScenarioMatrix
        {
            Axes =
            [
                new SweepAxis { Name = "endState", Values = endStateValues },
            ],
        };
    }

    /// <summary>
    /// Loads a checked-in scenario JSON file embedded alongside the Core assembly. Returns the
    /// deserialized <see cref="ReplayScenario"/>; throws if the file is missing or invalid so a
    /// regression on the static input is a loud test failure.
    /// </summary>
    public static ReplayScenario LoadScenario(EndState endState)
    {
        var descriptor = Get(endState);
        var resourceName = $"{ScenarioDirectory}/{descriptor.ScenarioFileName}";
        var assembly = Assembly.GetExecutingAssembly();
        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName.Replace('/', '.'), StringComparison.Ordinal)
                || n.EndsWith(descriptor.ScenarioFileName, StringComparison.Ordinal));

        if (fullName is null)
        {
            throw new FileNotFoundException(
                $"P7 regression scenario resource '{resourceName}' was not found. "
                + "Ensure it is embedded in Panopticon.Tools.ReplaySketch.Core.", resourceName);
        }

        using var stream = assembly.GetManifestResourceStream(fullName)!;
        var scenario = JsonSerializer.Deserialize<ReplayScenario>(stream)
            ?? throw new InvalidDataException($"Scenario '{descriptor.ScenarioFileName}' deserialized to null.");
        return scenario;
    }
}
