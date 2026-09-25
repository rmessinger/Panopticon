using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Content;
using Panopticon.Tools.ReplaySketch;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class P2ScenarioMatrixTests
{
    [Fact]
    public void Matrix_ExpandsTo18Cells_WithMirrorMatchesIncluded()
    {
        var matrix = BuildP2Matrix();
        var cells = matrix.Expand();

        Assert.Equal(18, cells.Count);
        Assert.Contains(
            cells,
            cell =>
                cell.Bindings["humanFaction"] == "USA"
                && cell.Bindings["aiFaction"] == "USA"
                && cell.Bindings["mapSize"] == "small");
        Assert.Contains(
            cells,
            cell =>
                cell.Bindings["humanFaction"] == "China"
                && cell.Bindings["aiFaction"] == "China"
                && cell.Bindings["mapSize"] == "medium");
    }

    [Fact]
    public void Matrix_AllCells_AIPlaysLegalGame()
    {
        var matrix = BuildP2Matrix();
        var cells = matrix.Expand();
        Assert.Equal(18, cells.Count);

        var installationService = new GeneralsInstallationService();
        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            return;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"rs-p2-matrix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var results = new List<CellResult>(cells.Count);
            foreach (var cell in cells)
            {
                cell.ConcreteScenario.MapPath = ResolveMapPath(cell.Bindings["mapSize"]);
                var report = HarnessRunner.RunAndAssert(cell.ConcreteScenario, installation, tempDirectory);
                results.Add(new CellResult(cell.Bindings, report));

                Assert.True(report.Results.Count >= 4, $"Expected at least 4 assertions for cell {ToCellKey(cell.Bindings)}.");
            }

            var matrixReport = new MatrixReport(results);
            Assert.True(matrixReport.AllPassed, matrixReport.ToJson());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static ScenarioMatrix BuildP2Matrix()
    {
        var scenario = new ReplayScenario
        {
            // Concrete map is mapped from the mapSize axis by ResolveMapPath(...) per-cell.
            MapPath = "maps/alpine assault/alpine assault.map",
            Players =
            {
                new PlayerSlotConfig
                {
                    Name = "Human",
                    FactionIndex = 2,
                    Color = 1,
                    StartPosition = 1,
                    Team = 1,
                },
                new PlayerSlotConfig
                {
                    Name = "AI",
                    FactionIndex = 3,
                    Color = 2,
                    StartPosition = 2,
                    Team = 2,
                },
            },
        };

        return new ScenarioMatrix
        {
            BaseScenario = scenario,
            Axes =
            [
                new SweepAxis { Name = "humanFaction", Values = ["USA", "GLA", "China"] },
                new SweepAxis { Name = "aiFaction", Values = ["USA", "GLA", "China"] },
                new SweepAxis { Name = "mapSize", Values = ["small", "medium"] },
            ],
            AssertionTemplates =
            [
                new AssertionTemplate { Kind = "state", Id = "p2-buildings-{humanFaction}-{aiFaction}-{mapSize}", Path = "player[3].buildingCount", Op = ">=", Value = "1" },
                new AssertionTemplate { Kind = "state", Id = "p2-units-{humanFaction}-{aiFaction}-{mapSize}", Path = "player[3].unitCount", Op = ">=", Value = "1" },
                new AssertionTemplate { Kind = "state", Id = "p2-attacks-{humanFaction}-{aiFaction}-{mapSize}", Path = "player[3].attackOrderCount", Op = ">=", Value = "1" },
                new AssertionTemplate { Kind = "invariant", Id = "p2-bank-nonnegative-{humanFaction}-{aiFaction}-{mapSize}", Path = "player[3].money", Op = ">=", Value = "0" },
            ],
        };
    }

    private static string ResolveMapPath(string mapSize)
    {
        return mapSize switch
        {
            "small" => "maps/alpine assault/alpine assault.map",
            "medium" => "maps/tournament desert/tournament desert.map",
            _ => "maps/alpine assault/alpine assault.map",
        };
    }

    private static string ToCellKey(IReadOnlyDictionary<string, string> key)
    {
        return $"{key["humanFaction"]}-{key["aiFaction"]}-{key["mapSize"]}";
    }
}
