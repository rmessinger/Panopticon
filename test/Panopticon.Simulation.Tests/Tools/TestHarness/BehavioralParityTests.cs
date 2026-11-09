#nullable enable

using System;
using System.IO;
using System.Text.Json;
using Panopticon.Data;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.TestHarness;

/// <summary>
/// Card WI-H5-05: behavioral equivalence harness fixtures. Filter fragment: BehavioralParity.
///
/// Claim discipline: play-identically is proven by a green parity run only (this scenario, this
/// seed, this runtime; same-runtime comparison only). The parity run proves converted map plus
/// converted unit packages run identically to original content: outcomes, survivors, engagement
/// assertions, spawn, kill, and loss tables match across both runs.
/// </summary>
[Collection("SerialGameTests")]
public sealed class BehavioralParityTests
{
    private readonly ITestOutputHelper _output;

    public BehavioralParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void ParityHarness_ConvertedContent_RunsEqualEndStateAcrossBothRuns()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        Assert.NotNull(installation);

        var scratch = NewScratch();
        try
        {
            var scenario = BehavioralParityScenario.Load(
                Path.Combine(AppContext.BaseDirectory, "h5-parity-scenario.json"));
            var reportPath = Path.Combine(scratch, "h5-parity-report.json");
            var options = new BehavioralParityOptions(Path.Combine(scratch, "artifacts"), reportPath);

            var exitCode = BehavioralParityRunner.Run(installation!, scenario, options, new RecordingReporter(_output));

            Assert.True(File.Exists(reportPath), "parity report was not written");
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;

            Assert.Equal(0, exitCode);
            Assert.True(root.GetProperty("allPassed").GetBoolean(), "parity report did not pass");

            // All four runs completed without error.
            var runs = root.GetProperty("runs");
            Assert.Equal(4, runs.GetArrayLength());
            foreach (var run in runs.EnumerateArray())
            {
                Assert.True(run.GetProperty("error").ValueKind == JsonValueKind.Null, $"run {run.GetProperty("side").GetString()}#{run.GetProperty("runIndex").GetInt32()} errored");
                Assert.True(run.GetProperty("finalFrame").GetUInt32() > 0, "run did not step");
            }

            // Parity verdict: original vs converted matches across all comparisons.
            var parity = root.GetProperty("parity");
            Assert.True(parity.GetProperty("allPassed").GetBoolean(), "parity comparisons failed");
            var parityComparisons = parity.GetProperty("comparisons");
            Assert.True(parityComparisons.GetArrayLength() >= 10, $"expected at least 10 parity comparisons, got {parityComparisons.GetArrayLength()}");
            foreach (var comparison in parityComparisons.EnumerateArray())
            {
                Assert.True(comparison.GetProperty("passed").GetBoolean(),
                    $"parity comparison {comparison.GetProperty("name").GetString()} differed: expected={comparison.GetProperty("expected").GetString()} actual={comparison.GetProperty("actual").GetString()}");
            }

            // Original and converted repeats are stable (same-content determinism).
            var originalStability = root.GetProperty("originalStability");
            Assert.True(originalStability.GetProperty("allPassed").GetBoolean(), "original stability differed");
            var convertedStability = root.GetProperty("convertedStability");
            Assert.True(convertedStability.GetProperty("allPassed").GetBoolean(), "converted stability differed");

            // The conversion itself succeeded: the package exists with the full P6b closure.
            var manifestPath = Path.Combine(scratch, "artifacts", "converted-units", "manifest.json");
            Assert.True(File.Exists(manifestPath), "converted unit package manifest missing");
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.True(manifest.RootElement.GetProperty("records").GetArrayLength() > 0);
        }
        finally
        {
            TryDelete(scratch);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void StabilityProbe_SameContent_SameSeed_RepeatsIdentical()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        Assert.NotNull(installation);

        var scratch = NewScratch();
        try
        {
            var scenario = BehavioralParityScenario.Load(
                Path.Combine(AppContext.BaseDirectory, "h5-parity-scenario.json"));
            var reportPath = Path.Combine(scratch, "h5-parity-stability.json");
            var options = new BehavioralParityOptions(Path.Combine(scratch, "artifacts"), reportPath);

            var exitCode = BehavioralParityRunner.RunStabilityProbe(installation!, scenario, options, new RecordingReporter(_output));

            Assert.True(exitCode == 0, "same-content stability probe diverged; see report");
            Assert.True(File.Exists(reportPath), "stability report was not written");
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;

            Assert.True(root.GetProperty("allPassed").GetBoolean());
            var stability = root.GetProperty("originalStability");
            Assert.True(stability.GetProperty("allPassed").GetBoolean(), "original repeat diverged");

            // The full P0 assertion set plus engagement rows compared, all equal.
            var comparisons = stability.GetProperty("comparisons");
            Assert.True(comparisons.GetArrayLength() >= 10, $"expected at least 10 comparisons, got {comparisons.GetArrayLength()}");
            foreach (var comparison in comparisons.EnumerateArray())
            {
                Assert.True(comparison.GetProperty("passed").GetBoolean(),
                    $"comparison {comparison.GetProperty("name").GetString()} differed");
            }

            // Claim discipline travels with the report.
            Assert.Contains("this run only", root.GetProperty("claimStatement").GetString());
        }
        finally
        {
            TryDelete(scratch);
        }
    }

    [Fact]
    public void DiffReport_NamesEachDivergence_WithBothValues()
    {
        var equal = new BehavioralParityComparison("assertion", "player[2].unitCount", "6", "6", true);
        var differing = new BehavioralParityComparison("survivors", "side0:AmericaTankCrusader:survivors", "2", "1", false);
        var set = new BehavioralParityComparisonSet("original#1", "converted#1", false, false, [equal, differing]);
        var report = new BehavioralParityReport(
            "h5-test", "maps/test.map", 1, "default", "map-pkg", "units-pkg",
            [],
            set,
            new BehavioralParityComparisonSet("original#1", "original#2", true, true, [equal]),
            new BehavioralParityComparisonSet("converted#1", "converted#2", true, true, [equal]),
            AllPassed: false,
            BehavioralParityReport.ClaimText);

        var text = BehavioralParityReportIo.WriteText(report);

        // Named diff entries carry both values.
        Assert.Contains("DIFF survivors/side0:AmericaTankCrusader:survivors: left=2 right=1", text);
        Assert.Contains("left=2 right=1", text);
        // Equal comparisons are counted, not enumerated.
        Assert.Contains("1 equal, 1 differing of 2 comparisons", text);
        // Claim discipline is stated in every report.
        Assert.Contains("this run only", text);
        Assert.Contains("never a cross-runtime or cross-unit claim", text);
    }

    private static string NewScratch()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "PanopticonTests", "h5_parity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        return scratch;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Preserve artifacts on lock; temp cleanup is best-effort.
        }
    }

    private sealed class RecordingReporter(ITestOutputHelper output) : ITestHarnessReporter
    {
        public void Info(string message) => output.WriteLine(message);

        public void Error(string message) => output.WriteLine(message);

        public void ActionDispatched(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void ActionCompleted(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void MatchOutcomeDecided(Panopticon.Logic.OutcomeStatus status, Panopticon.Logic.OutcomeReason reason, string? winnerName, uint frame)
        {
        }
    }
}
