using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Tools.TestHarness;
using Xunit;

namespace Panopticon.Tests.Tools.TestHarness;

// WI-N0-02 install-free coverage: the case planner's shape and the report's JSON round-trip.
// The live 10-unit run is the Interactive scenario test, not this class.
public class PathfindingLoadPlannerTests
{
    [Fact]
    public void BuildCases_CoversSquadlessSquadAndImpassableModes()
    {
        var cases = PathfindingLoadCasePlanner.BuildCases([1, 10]);

        Assert.Equal(
            ["squadless-1", "squadless-10", "armyai-squads-1", "armyai-squads-10", "squadless-10-impassable"],
            cases.Select(c => c.Name));
        Assert.All(
            cases.Take(4),
            c => Assert.Equal(PathfindingLoadCasePlanner.CrossMapDestination, c.DestinationKind));
        var impassable = cases[^1];
        Assert.Equal(PathfindingLoadCasePlanner.ImpassablePocketDestination, impassable.DestinationKind);
        Assert.Equal(PathfindingLoadCasePlanner.SquadlessMode, impassable.Mode);
        Assert.Equal(10, impassable.Size);
        Assert.Equal(PathfindingLoadCasePlanner.SquadlessMode, cases[0].Mode);
        Assert.Equal(PathfindingLoadCasePlanner.ArmyAiSquadsMode, cases[2].Mode);
        Assert.Equal(10, cases[1].Size);
    }

    [Fact]
    public void BuildCases_UsesConfiguredImpassableSize()
    {
        var cases = PathfindingLoadCasePlanner.BuildCases([5], impassableCaseSize: 3);

        Assert.Equal("squadless-3-impassable", cases[^1].Name);
        Assert.Equal(3, cases[^1].Size);
    }

    [Fact]
    public void Report_RoundTripsThroughJson()
    {
        var report = new PathfindingLoadReport
        {
            Map = "maps/deepdivide/deepdivide.map",
            ProfilePath = "profile.json",
            Seed = 7,
            GridWidth = 300,
            GridHeight = 300,
            Derivation = "derived per tick",
            Cases =
            [
                new PathfindingLoadCaseResult
                {
                    Name = "squadless-10",
                    Mode = PathfindingLoadCasePlanner.SquadlessMode,
                    DestinationKind = PathfindingLoadCasePlanner.CrossMapDestination,
                    RequestedSize = 10,
                    SpawnedSize = 10,
                    SquadsAtOrder = 0,
                    SettleFrames = 5,
                    WindowTicks = 30,
                    DestinationX = 3000f,
                    DestinationY = 2950f,
                    DestinationZ = 0f,
                    OrderTickPathSpanMs = 1.25,
                    Requests = new PathfindingLoadRequestCounts
                    {
                        Unit = 10,
                        SquadLeader = 0,
                        BlockedFollower = 0,
                        Horde = 0,
                        Async = 0,
                    },
                    SyncRequests = 10,
                    NodesExpanded = 4000,
                    FailedSearches = 0,
                    EndpointAdjustments = 0,
                    MeanSearchMs = 0.125,
                    P99SearchMs = 0.2,
                },
            ],
            FailureReasons = [],
            AllPassed = true,
        };

        var directory = Path.Combine(Path.GetTempPath(), "pathfinding-load-planner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "report.json");
        try
        {
            PathfindingLoadReport.WriteJson(report, path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("maps/deepdivide/deepdivide.map", root.GetProperty("map").GetString());
            Assert.Equal(7, root.GetProperty("seed").GetInt32());
            Assert.Equal(300, root.GetProperty("gridWidth").GetInt32());
            Assert.Equal(300, root.GetProperty("gridHeight").GetInt32());
            var result = root.GetProperty("cases")[0];
            Assert.Equal("squadless-10", result.GetProperty("name").GetString());
            Assert.Equal(10, result.GetProperty("spawnedSize").GetInt32());
            Assert.Equal(10, result.GetProperty("requests").GetProperty("unit").GetInt64());
            Assert.Equal(4000, result.GetProperty("nodesExpanded").GetInt64());
            Assert.Equal(0.125, result.GetProperty("meanSearchMs").GetDouble(), 5);
            Assert.Equal(0.2, result.GetProperty("p99SearchMs").GetDouble(), 5);
            Assert.True(root.GetProperty("allPassed").GetBoolean());

            var text = PathfindingLoadReport.WriteText(report);
            Assert.Contains("squadless-10", text);
            Assert.Contains("seed 7", text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
