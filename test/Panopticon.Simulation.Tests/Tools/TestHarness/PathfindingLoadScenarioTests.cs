using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Tests.Data;
using Panopticon.Tools.TestHarness;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.TestHarness;

// WI-N0-02: live 10-unit run of the pathfinding-load scenario proving the three case modes end
// to end: squad-less selections issue per-unit requests, army-AI squads issue leader requests,
// and the unreachable destination produces failed searches. The full 1/10/100/1000 run is the
// TestHarness built-in scenario, not a test. Serial-game collection: real Game instances collide
// on static state with parallel Game-creating classes.

[Trait("Category", "Interactive")]
[Collection("SerialGameTests")]
public class PathfindingLoadScenarioTests
{
    private readonly ITestOutputHelper _output;

    public PathfindingLoadScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void Run_MeasuresSquadlessSquadAndImpassableCases()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pathfinding-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var profilePath = Path.Combine(directory, "profile.json");
        var reportPath = Path.Combine(directory, "report.json");
        var benchmarkPath = Path.Combine(directory, "report-benchmark.json");
        File.WriteAllText(profilePath, ProfileJson);

        var config = new TestHarnessConfig
        {
            Headless = true,
            PathfindingLoadProfilePath = profilePath,
            PathfindingLoadReportPath = reportPath,
            PathfindingLoadSeed = 1,
            PathfindingLoadSelectionSizes = [10],
        };

        int exitCode;
        JsonDocument report;
        JsonDocument benchmark;
        try
        {
            exitCode = PathfindingLoadRunner.Run(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                config,
                new OutputReporter(_output));
            report = JsonDocument.Parse(File.ReadAllText(reportPath));
            benchmark = JsonDocument.Parse(File.ReadAllText(benchmarkPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        Assert.Equal(0, exitCode);

        var root = report.RootElement;
        Assert.True(root.GetProperty("gridWidth").GetInt32() > 0, "the navigation grid dimensions should be reported");
        Assert.True(root.GetProperty("gridHeight").GetInt32() > 0, "the navigation grid dimensions should be reported");
        var cases = root.GetProperty("cases");
        Assert.Equal(3, cases.GetArrayLength());

        var squadless = cases[0];
        Assert.Equal("squadless-10", squadless.GetProperty("name").GetString());
        Assert.Equal(0, squadless.GetProperty("squadsAtOrder").GetInt32());
        Assert.Equal(10, squadless.GetProperty("spawnedSize").GetInt32());
        Assert.Equal(10, squadless.GetProperty("requests").GetProperty("unit").GetInt64());
        Assert.Equal(0, squadless.GetProperty("requests").GetProperty("squadLeader").GetInt64());
        Assert.Equal(10, squadless.GetProperty("syncRequests").GetInt64());
        Assert.True(squadless.GetProperty("nodesExpanded").GetInt64() > 0, "squad-less order should expand search nodes");
        Assert.True(squadless.GetProperty("orderTickPathSpanMs").GetDouble() > 0, "the order tick should carry path span");
        Assert.True(squadless.GetProperty("meanSearchMs").GetDouble() > 0, "per-search mean should be positive");

        var squads = cases[1];
        Assert.Equal("armyai-squads-10", squads.GetProperty("name").GetString());
        var squadsAtOrder = squads.GetProperty("squadsAtOrder").GetInt32();
        Assert.True(squadsAtOrder > 0, "army-AI mode should order through squads");
        Assert.Equal(squadsAtOrder, squads.GetProperty("requests").GetProperty("squadLeader").GetInt64());
        Assert.True(squads.GetProperty("meanSearchMs").GetDouble() > 0, "per-search mean should be positive");

        var impassable = cases[2];
        Assert.Equal("squadless-10-impassable", impassable.GetProperty("name").GetString());
        Assert.True(
            impassable.GetProperty("failedSearches").GetInt64() >= impassable.GetProperty("spawnedSize").GetInt64() / 2,
            "the unreachable destination should fail most unit searches");

        // The B1 record beside the report carries the path system span (WI-N0-01) and the
        // per-case scalars (WI-N0-02).
        var metrics = benchmark.RootElement.GetProperty("statistics").EnumerateArray()
            .Select(entry => entry.GetProperty("metric").GetString())
            .ToArray();
        Assert.Contains("system.path.spanMs", metrics);
        Assert.Contains("case.squadless-10.orderTickPathSpanMs", metrics);
        Assert.Contains("case.armyai-squads-10.orderTickPathSpanMs", metrics);
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

    // Same map and zones as the capacity-probe scaled profile; a single ranger block feeds the
    // scenario's per-case spawns, so the profile's own counts are irrelevant.
    private const string ProfileJson = """
        {
          "map": {
            "name": "deepdivide",
            "path": "maps/deepdivide/deepdivide.map",
            "provenance": "scaled test profile (WI-N0-02 scenario test)"
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
              { "definition": "AmericaInfantryRanger", "count": 20 }
            ],
            "aircraft": { "definition": "AmericaJetAurora", "count": 0 }
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
