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
/// seed, this runtime; same-runtime comparison only). The parity run is NOT green today: the
/// converted-content side cannot spawn any P6b roster unit because the H5-02/H5-04 retained-legacy
/// re-issue layer stages definitions parsed against an isolated rebuild store whose global
/// references (LocomotorSet, SpecialPowerTemplate, and every other store-bound INI reference)
/// resolve null at object creation. That is a storage-layer gap owned outside this ticket; the
/// parity test below encodes the failure verbatim and flips to the equality assertion once the
/// retained re-issue path binds against the publication store. The stability probe test proves
/// the harness comparison machinery end to end today (same content, same seed, identical
/// assertions through the P0 assertion layer).
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
    public void ParityHarness_ConvertedContent_FailsWithNamedReport_KnownRetainedReferenceGap()
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

            // Original content runs cleanly end to end at the fixed seed.
            var runs = root.GetProperty("runs");
            var originalRun = runs[0];
            Assert.Equal("original", originalRun.GetProperty("side").GetString());
            Assert.True(originalRun.GetProperty("error").ValueKind == JsonValueKind.Null, "original run 1 errored");
            Assert.True(originalRun.GetProperty("finalFrame").GetUInt32() > 0, "original run 1 did not step");

            // Converted content fails at object creation with the retained-reference gap.
            Assert.Equal(5, exitCode);
            var convertedRun = runs[1];
            Assert.Equal("converted", convertedRun.GetProperty("side").GetString());
            var error = convertedRun.GetProperty("error").GetString();
            Assert.NotNull(error);
            Assert.Contains("NullReferenceException", error);
            Assert.Matches(
                ".*(LocomotorSet.Initialize|SpecialPowerModule\\.\\.ctor|Locomotor\\.\\.ctor).*",
                error.ReplaceLineEndings(" "));

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
