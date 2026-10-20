using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Data;
using Panopticon.Tests.Data;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.TestHarness;

// WI-M5-06: a 3-seed scaled batch produces an aggregate whose counts reconcile with its
// per-run reports: wins plus draws plus failures equal runs, per-side intervals bracket the
// win rates, and the summed kill/loss tables equal the per-run sums. Needs CNC_GENERALS_PATH
// and EMPIRE_EARTH_II_PATH.

[Trait("Category", "Interactive")]
public sealed class MassBattleBatchTests
{
    private readonly ITestOutputHelper _output;

    public MassBattleBatchTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void ScaledBatch_AggregatesPerRunReports()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mass-battle-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "battle.json");
        var manifestPath = Path.Combine(directory, "manifest.json");

        var profile = MassBattleDeploymentProfile.Load(
            Path.Combine(AppContext.BaseDirectory, MassBattleRosterSpawner.DefaultBattleProfileFileName));
        var manifest = RosterBudgetEvaluator.Load(
            Path.Combine(AppContext.BaseDirectory, RosterBudgetEvaluator.DefaultManifestFileName));
        foreach (var side in manifest.Sides)
        {
            foreach (var entry in side.Entries)
            {
                entry.Count = entry.Aircraft ? 1 : 2;
            }
        }

        profile = profile with
        {
            RosterManifestPath = manifestPath,
            Sides = profile.Sides
                .Select(side => side with { AirfieldAnchors = [side.AirfieldAnchors[0]] })
                .ToList(),
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, RosterBudgetEvaluator.Options));
        File.WriteAllText(profilePath, JsonSerializer.Serialize(profile, MassBattleDeploymentProfile.JsonOptions));

        try
        {
            var batchReportPath = Path.Combine(directory, "batch.json");
            var config = new TestHarnessConfig
            {
                Headless = true,
                MassBattleProfilePath = profilePath,
                MassBattleBatchSeedStart = 1,
                MassBattleBatchRuns = 3,
                MassBattleFrameLimit = 27_000,
                MassBattleBatchReportPath = batchReportPath,
            };

            var exitCode = MassBattleBatchRunner.Run(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                config,
                new OutputReporter(_output));
            Assert.Equal(0, exitCode);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var batch = JsonSerializer.Deserialize<MassBattleBatchReport>(File.ReadAllText(batchReportPath), options)!;
            var runReports = Directory.GetFiles(Path.Combine(directory, "batch-runs"), "seed-*.json")
                .Where(path => System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(path), @"^seed-\d+\.json$"))
                .Select(path => JsonSerializer.Deserialize<MassBattleReport>(File.ReadAllText(path), options)!)
                .ToList();

            Assert.Equal(3, runReports.Count);
            Assert.Equal(3, batch.Runs);
            Assert.Equal(0, batch.Failures);
            Assert.All(runReports, report => Assert.Equal(nameof(Panopticon.Logic.OutcomeStatus.Decided), report.OutcomeStatus));
            Assert.True(batch.AllRunsReachedOutcome);

            var accounted = batch.Sides.Sum(side => side.Wins) + batch.Draws + batch.Failures;
            Assert.Equal(batch.Runs, accounted);
            foreach (var side in batch.Sides)
            {
                var expectedWins = runReports.Count(report => report.WinningSideId == side.SideId);
                Assert.Equal(expectedWins, side.Wins);
                Assert.True(side.WilsonLow <= side.WinRate && side.WinRate <= side.WilsonHigh,
                    $"{side.SideId} win rate {side.WinRate} should sit inside [{side.WilsonLow}, {side.WilsonHigh}]");
            }

            foreach (var row in batch.LossesSummed)
            {
                var perRunSum = runReports.Sum(report => report.Losses
                    .Where(loss => loss.SideId == row.SideId && loss.Definition == row.Definition)
                    .Sum(loss => loss.Count));
                Assert.Equal(perRunSum, row.Count);
            }

            Assert.Equal(
                runReports.Sum(report => report.Kills.Sum(row => row.Count)),
                batch.KillsSummed.Sum(row => row.Count));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ApplyVariant_MirrorAndSwapRewriteRostersOnly()
    {
        var profile = new MassBattleDeploymentProfile
        {
            Sides =
            {
                new MassBattleSideProfile { Id = "sideA", Roster = "generals-led", AirfieldAnchors = [new MassBattlePoint { X = 1, Y = 2 }] },
                new MassBattleSideProfile { Id = "sideB", Roster = "ee2-led", AirfieldAnchors = [new MassBattlePoint { X = 3, Y = 4 }] },
            },
        };

        var mirror = MassBattleBatchRunner.ApplyVariant(profile, MassBattleBatchVariant.Mirror);
        Assert.Equal(["generals-led", "generals-led"], mirror.Sides.Select(side => side.Roster));
        Assert.Equal(["sideA", "sideB"], mirror.Sides.Select(side => side.Id));
        Assert.All(mirror.Sides, (side, index) => Assert.Equal(profile.Sides[index].AirfieldAnchors, side.AirfieldAnchors));

        var swap = MassBattleBatchRunner.ApplyVariant(profile, MassBattleBatchVariant.Swap);
        Assert.Equal(["ee2-led", "generals-led"], swap.Sides.Select(side => side.Roster));
        Assert.All(swap.Sides, (side, index) => Assert.Equal(profile.Sides[index].AirfieldAnchors, side.AirfieldAnchors));
    }

    [GameFact(SageGame.CncGenerals)]
    public void ScaledMirrorBatch_DecidesAndKeepsRostersOnlyRewrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mass-battle-mirror-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "battle.json");
        var manifestPath = Path.Combine(directory, "manifest.json");

        var profile = MassBattleDeploymentProfile.Load(
            Path.Combine(AppContext.BaseDirectory, MassBattleRosterSpawner.DefaultBattleProfileFileName));
        var manifest = RosterBudgetEvaluator.Load(
            Path.Combine(AppContext.BaseDirectory, RosterBudgetEvaluator.DefaultManifestFileName));
        foreach (var side in manifest.Sides)
        {
            foreach (var entry in side.Entries)
            {
                entry.Count = entry.Aircraft ? 1 : 2;
            }
        }

        profile = profile with
        {
            RosterManifestPath = manifestPath,
            Sides = profile.Sides
                .Select(side => side with { AirfieldAnchors = [side.AirfieldAnchors[0]] })
                .ToList(),
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, RosterBudgetEvaluator.Options));
        File.WriteAllText(profilePath, JsonSerializer.Serialize(profile, MassBattleDeploymentProfile.JsonOptions));

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            foreach (var variant in new[] { "Mirror", "Swap" })
            {
                var batchReportPath = Path.Combine(directory, $"batch-{variant.ToLowerInvariant()}.json");
                var config = new TestHarnessConfig
                {
                    Headless = true,
                    MassBattleProfilePath = profilePath,
                    MassBattleBatchSeedStart = 1,
                    MassBattleBatchRuns = 1,
                    MassBattleFrameLimit = 27_000,
                    MassBattleBatchReportPath = batchReportPath,
                    MassBattleBatchVariant = variant,
                };

                var exitCode = MassBattleBatchRunner.Run(
                    InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                    config,
                    new OutputReporter(_output));
                Assert.Equal(0, exitCode);

                var batch = JsonSerializer.Deserialize<MassBattleBatchReport>(File.ReadAllText(batchReportPath), options)!;
                Assert.Equal(1, batch.Runs);
                Assert.Equal(0, batch.Failures);
                Assert.True(batch.AllRunsReachedOutcome);

                // The variant rewrites rosters only: the batch sides are the profile's sides.
                Assert.Equal(2, batch.Sides.Count);

                var variantProfile = MassBattleDeploymentProfile.Load(
                    Path.Combine(directory, $"batch-{variant.ToLowerInvariant()}-runs", $"variant-{variant.ToLowerInvariant()}.json"));
                Assert.Equal(
                    variant == "Mirror" ? ["generals-led", "generals-led"] : ["ee2-led", "generals-led"],
                    variantProfile.Sides.Select(side => side.Roster));
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class OutputReporter(ITestOutputHelper output) : ITestHarnessReporter
    {
        public void Info(string message) => output.WriteLine(message);

        public void Error(string message) => output.WriteLine($"ERROR: {message}");

        public void ActionDispatched(Panopticon.Tools.ReplaySketch.Model.ActionType actionType, string? playerName, uint frame) =>
            output.WriteLine($"  [dispatched] {actionType} player: {playerName ?? "?"} frame: {frame}");

        public void ActionCompleted(Panopticon.Tools.ReplaySketch.Model.ActionType actionType, string? playerName, uint frame) =>
            output.WriteLine($"  [completed] {actionType} player: {playerName ?? "?"} frame: {frame}");

        public void MatchOutcomeDecided(Panopticon.Logic.OutcomeStatus status, Panopticon.Logic.OutcomeReason reason, string? winnerName, uint frame) =>
            output.WriteLine($"  [match-decided] status: {status} reason: {reason} winner: {winnerName ?? "(shared)"} frame: {frame}");
    }
}
