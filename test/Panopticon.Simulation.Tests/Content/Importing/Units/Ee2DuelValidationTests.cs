using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Data;
using Panopticon.Logic;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-M3-03: every EE2-versus-Generals archetype duel in the shipped validation profile
/// resolves within its authored time-to-kill bounds and the machine-readable report names every
/// result. Requires CNC_GENERALS_PATH and EMPIRE_EARTH_II_PATH; no EE2 asset is committed.
/// </summary>
[Trait("Category", "Interactive")]
[Collection("SerialGameTests")]
public sealed class Ee2DuelValidationTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryDuelResolvesWithinItsAuthoredBounds()
    {
        using var game = new Game(
            InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            preferredBackend: null,
            config: new Configuration { NoAudio = true, SkipStartingBuildings = true },
            window: null);
        game.StartSkirmishOrMultiPlayerGame(
            "maps/Dark Mountain/Dark Mountain.map",
            new EchoConnection(),
            [new(0, "FactionAmerica", new Panopticon.Mathematics.ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
             new(1, "FactionChina", new Panopticon.Mathematics.ColorRgb(0, 255, 0), 1, PlayerOwner.EasyAi, "China")],
            1,
            isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();

        // The shipped profile is the single source of the roster, mapping, and duel bounds.
        var profile = Ee2ValidationProfile.Load(Path.Combine(AppContext.BaseDirectory, "ee2-reference-validation.json"));
        var reporter = new CollectingReporter();
        Assert.True(UnitContentImportRunner.Import(game, profile.ContentImport, reporter), string.Join("\n", reporter.Errors));

        var report = Ee2DuelPhase.RunDuels(game, profile, reporter);

        foreach (var line in reporter.Lines.Where(l => l.StartsWith("[duel]")))
        {
            output.WriteLine(line);
        }

        Assert.True(report.AllPassed, string.Join("; ", report.Duels.Where(d => !d.Passed).Select(d => $"{d.Name}: {d.FailureReason}")));
        Assert.Equal(7, report.Duels.Count);

        foreach (var duel in report.Duels)
        {
            Assert.True(duel.Resolved, $"{duel.Name} did not resolve");
            Assert.NotNull(duel.Winner);
            Assert.NotNull(duel.Seconds);
            output.WriteLine($"{duel.Name}: {duel.Seconds:0.00}s winner={duel.Winner} bounds=[{duel.MinSeconds:0.#},{duel.MaxSeconds:0.#}]");
        }

        // Air duels are part of the matrix (fighter, bomber, helicopter).
        Assert.Contains(report.Duels, d => d.Name == "fighter");
        Assert.Contains(report.Duels, d => d.Name == "bomber");
        Assert.Contains(report.Duels, d => d.Name == "helicopter");
    }

    [Fact]
    public void FailureReasonsAndBoundsAreReportedVerbatim()
    {
        // Synthetic checks of the report shape: no install needed, no game boot.
        var failed = new Ee2DuelResult("stalled", "ee2:Tank", "AmericaTankCrusader", "neither unit died within 9000 frames (300.0s)")
        {
            MinSeconds = 3,
            MaxSeconds = 30,
            Resolved = false,
        };
        var passed = new Ee2DuelResult("infantry", "ee2:HeavyInfantry1UpgradeEpoch15", "GLAInfantryRebel")
        {
            Seconds = 13.233,
            Frames = 397,
            Winner = "GLAInfantryRebel",
            MinSeconds = 4,
            MaxSeconds = 40,
            Resolved = true,
        };

        Assert.False(failed.Passed);
        Assert.True(passed.Passed);
        Assert.Null(passed.FailureReason);
        Assert.True(passed.Resolved);
        Assert.Equal(13.233, passed.Seconds);
    }

    private sealed class CollectingReporter : ITestHarnessReporter
    {
        public List<string> Lines { get; } = [];

        public List<string> Errors { get; } = [];

        public void Info(string message)
        {
            Lines.Add(message);
        }

        public void Error(string message)
        {
            Lines.Add(message);
            Errors.Add(message);
        }

        public void ActionDispatched(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void ActionCompleted(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void MatchOutcomeDecided(OutcomeStatus status, OutcomeReason reason, string? winnerName, uint frame)
        {
        }
    }
}
