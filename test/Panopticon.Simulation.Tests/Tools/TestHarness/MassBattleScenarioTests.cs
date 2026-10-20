using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Data;
using Panopticon.Tests.Data;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.TestHarness;

// WI-M5-04: the mass battle scenario runs a scaled roster battle headless to a P3 Decided
// outcome on two seeds, and the same seed twice yields an identical report (the report carries
// no timing fields; paths are normalized before comparing). The full 1000 vs 1000 run is the
// TestHarness built-in scenario, not a test. Needs CNC_GENERALS_PATH and EMPIRE_EARTH_II_PATH.

[Trait("Category", "Interactive")]
public sealed class MassBattleScenarioTests
{
    private readonly ITestOutputHelper _output;

    public MassBattleScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void ScaledBattles_ReachDecidedOutcome_AndReproducePerSeed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mass-battle-scenario-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "battle.json");
        var manifestPath = Path.Combine(directory, "manifest.json");

        var profile = MassBattleDeploymentProfile.Load(
            Path.Combine(AppContext.BaseDirectory, MassBattleRosterSpawner.DefaultBattleProfileFileName));
        var manifest = RosterBudgetEvaluator.Load(
            Path.Combine(AppContext.BaseDirectory, RosterBudgetEvaluator.DefaultManifestFileName));

        // Test-sized rosters keep the manifest's shape: every definition fields units, both
        // sides keep aircraft, and one anchor parks the scaled air fleet.
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
            var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);

            var seedOne = RunBattle(directory, profilePath, seed: 1, "seed-1.json");
            var seedTwo = RunBattle(directory, profilePath, seed: 2, "seed-2.json");
            var seedOneRepeat = RunBattle(directory, profilePath, seed: 1, "seed-1-repeat.json");

            foreach (var report in new[] { seedOne, seedTwo, seedOneRepeat })
            {
                Assert.True(report.OutcomeStatus == nameof(Panopticon.Logic.OutcomeStatus.Decided),
                    $"battle should decide, got {report.OutcomeStatus}/{report.OutcomeReason}");
                Assert.NotEmpty(report.WinningSideId!);
                Assert.Empty(report.FailureReasons);
            }

            var normalized = NormalizePaths(seedOne).ToJsonString();
            Assert.Equal(normalized, NormalizePaths(seedOneRepeat).ToJsonString());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private MassBattleReport RunBattle(string directory, string profilePath, int seed, string reportName)
    {
        var config = new TestHarnessConfig
        {
            Headless = true,
            MassBattleProfilePath = profilePath,
            MassBattleSeed = seed,
            MassBattleFrameLimit = 27_000,
            MassBattleReportPath = Path.Combine(directory, reportName),
        };

        var reportPath = config.MassBattleReportPath!;
        var exitCode = MassBattleRunner.Run(
            InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            config,
            new OutputReporter(_output));
        Assert.Equal(0, exitCode);
        return JsonSerializer.Deserialize<MassBattleReport>(File.ReadAllText(reportPath), JsonOptions)!;
    }

    // Path fields differ per report file; everything else must be identical for the same seed.
    private static JsonNode NormalizePaths(MassBattleReport report)
    {
        var node = JsonSerializer.SerializeToNode(report with { RunOutputPath = null, ScreenshotPath = null }, JsonOptions)!;
        node["profilePath"] = null;
        return node!;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

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
