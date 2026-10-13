using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Data;
using Panopticon.Tests.Data;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.TestHarness;

// WI-M1-01: live run of the capacity probe on a scaled roster (38 ground units + 2 Aurora jets per
// side on the deepdivide deployment zones) proving the scripted general attack end to end: ground
// armies engage and take losses, every jet launches through the real taxi/takeoff flow, and a
// recovered jet launches again (the rearm observation). The full 1000 vs 1000 run is the
// TestHarness built-in scenario, not a test. Serial-game collection: real Game instances collide
// on static state with parallel Game-creating classes.

[Trait("Category", "Interactive")]
[Collection("SerialGameTests")]
public class MassBattleCapacityProbeScenarioTests
{
    private readonly ITestOutputHelper _output;

    public MassBattleCapacityProbeScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void ScaledProbe_RunsGeneralAttackWithSortiesAndLosses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mass-battle-capacity-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "profile.json");
        var reportPath = Path.Combine(directory, "report.json");
        File.WriteAllText(profilePath, ScaledProfileJson);

        var config = new TestHarnessConfig
        {
            Headless = true,
            MassBattleCapacityProbeProfilePath = profilePath,
            MassBattleCapacityProbeReportPath = reportPath,
            MassBattleCapacityProbeFrameBudget = 5400,
        };

        int exitCode;
        try
        {
            exitCode = MassBattleCapacityProbeRunner.Run(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                config,
                new OutputReporter(_output));
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        Assert.Equal(0, exitCode);
    }

    [GameFact(SageGame.CncGenerals)]
    public void ScaledProbe_AsyncAnchorRecordsLatencies()
    {
        // WI-B4-06: the async-path scale anchor wiring, proven at scaled size. The full
        // M1-scale anchor run is the TestHarness scenario, not a test.
        var directory = Path.Combine(Path.GetTempPath(), "mass-battle-capacity-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "profile.json");
        var reportPath = Path.Combine(directory, "report.json");
        File.WriteAllText(profilePath, ScaledProfileJson);

        var config = new TestHarnessConfig
        {
            Headless = true,
            MassBattleCapacityProbeProfilePath = profilePath,
            MassBattleCapacityProbeReportPath = reportPath,
            MassBattleCapacityProbeFrameBudget = 5400,
            MassBattleCapacityProbeAsyncAnchorBurstEveryFrames = 300,
            MassBattleCapacityProbeAsyncAnchorRequestsPerBurst = 4,
        };

        int exitCode;
        JsonDocument report;
        try
        {
            exitCode = MassBattleCapacityProbeRunner.Run(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                config,
                new OutputReporter(_output));
            report = JsonDocument.Parse(File.ReadAllText(reportPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        Assert.Equal(0, exitCode);

        var anchor = report.RootElement.GetProperty("asyncPathAnchor");
        Assert.Equal(68, anchor.GetProperty("submitted").GetInt32());
        Assert.True(anchor.GetProperty("applied").GetInt32() > 0, "at least one anchor request should apply");
        Assert.True(anchor.GetProperty("medianWallMs").GetDouble() > 0);
    }

    /// <summary>Forwards runner messages to the test output so failures show their reasons.</summary>
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

    // The embedded 1000-per-side profile scaled to a test-sized roster: same map, zones, and
    // airfield anchors; 38 ground units plus 2 parked jets per side.
    private const string ScaledProfileJson = """
        {
          "map": {
            "name": "deepdivide",
            "path": "maps/deepdivide/deepdivide.map",
            "provenance": "scaled test profile (WI-M1-01 scenario test)"
          },
          "sides": [
            {
              "id": "sideA",
              "side": "FactionAmerica",
              "color": "409FFF",
              "zone": { "x": 1900, "y": 370, "width": 2200, "height": 1400 },
              "facingDegrees": 90,
              "airfieldDefinition": "AmericaAirfield",
              "airfieldAnchors": [ { "x": 2450, "y": 150 }, { "x": 3450, "y": 150 } ]
            },
            {
              "id": "sideB",
              "side": "FactionAmerica",
              "color": "FF5040",
              "zone": { "x": 1900, "y": 2250, "width": 2200, "height": 1400 },
              "facingDegrees": -90,
              "airfieldDefinition": "AmericaAirfield",
              "airfieldAnchors": [ { "x": 2450, "y": 3830 }, { "x": 3450, "y": 3830 } ]
            }
          ],
          "capacity": {
            "unitsPerSide": 40,
            "spacingFactor": 2.0,
            "minPitch": 25,
            "blocks": [
              { "definition": "AmericaInfantryRanger", "count": 20 },
              { "definition": "AmericaTankPaladin", "count": 10 },
              { "definition": "AmericaVehicleTomahawk", "count": 8 }
            ],
            "aircraft": { "definition": "AmericaJetAurora", "count": 2 }
          },
          "reachability": {
            "arrivalRadius": 60,
            "maxSpeedSafetyFactor": 2.0,
            "minTimeoutFrames": 900,
            "maxFrameBudget": 30000
          }
        }
        """;
}
