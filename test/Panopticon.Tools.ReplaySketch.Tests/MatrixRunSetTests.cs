using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Tools.ReplaySketch;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class MatrixRunSetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-runset-{Guid.NewGuid():N}");

    private static ScenarioMatrix NewMatrix() => new()
    {
        BaseScenario = ScenarioMatrixTestHelpers.BaseScenario(),
        Axes =
        [
            new SweepAxis { Name = "seed", Values = ["11", "22"] },
            new SweepAxis { Name = "difficulty", Values = ["easy", "hard"] },
        ],
        Bindings = [new MatrixBinding { Target = MatrixBindingTarget.Seed, Axis = "seed" }],
    };

    [Fact]
    public void CellKey_IsOrdinalSortedAndSanitized()
    {
        var key = ScenarioMatrixRunner.BuildCellKey(new Dictionary<string, string> { { "b", "2" }, { "a", "1" } });

        Assert.Equal("a=1;b=2", key);
        Assert.Equal("axis=a_b", ScenarioMatrixRunner.BuildCellKey(new Dictionary<string, string> { { "axis", "a/b" } }));
    }

    [Fact]
    public void CollidingKeys_ThrowNamingBothRawBindings()
    {
        var matrix = new ScenarioMatrix
        {
            BaseScenario = ScenarioMatrixTestHelpers.BaseScenario(),
            Axes = [new SweepAxis { Name = "axis", Values = ["v/1", "v_1"] }],
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ScenarioMatrixRunner.RunCells(matrix, _root, (_, _) => AssertionReport.Empty));

        Assert.Contains("axis=v/1", ex.Message);
        Assert.Contains("axis=v_1", ex.Message);
    }

    [Fact]
    public void FailureIsolation_RecordsErrorAndPreservesCompletedCells()
    {
        var matrix = NewMatrix();
        var failed = false;

        var report = ScenarioMatrixRunner.RunCells(matrix, _root, (cell, cellDirectory) =>
        {
            if (cell.Bindings["difficulty"] == "hard" && !failed)
            {
                failed = true;
                throw new InvalidOperationException("simulated cell failure");
            }

            File.WriteAllText(Path.Combine(cellDirectory, "marker.txt"), "ok");
            return AssertionReport.Empty;
        });

        Assert.Equal(4, report.Cells.Count);
        var failedCell = Assert.Single(report.Cells, c => c.Status == "failed");
        Assert.Equal("simulated cell failure", failedCell.Error);
        Assert.False(failedCell.AllPassed);
        Assert.Equal(3, report.Cells.Count(c => c.Status == "passed"));
        Assert.Equal(1, report.FailedCells);
        Assert.False(report.AllPassed);
        Assert.All(report.Cells.Where(c => c.Status == "passed"), c => Assert.True(Directory.Exists(Path.Combine(_root, c.RelativePath))));
    }

    [Fact]
    public void Manifest_UsesRelativePathsAndRelocates()
    {
        var matrix = NewMatrix();
        var report = ScenarioMatrixRunner.RunCells(matrix, _root, (_, cellDirectory) =>
        {
            File.WriteAllText(Path.Combine(cellDirectory, "marker.txt"), "ok");
            return AssertionReport.Empty;
        });
        ScenarioMatrixRunner.WriteRunSetManifest(_root, report);

        var manifestPath = Path.Combine(_root, ScenarioMatrixRunner.ManifestFileName);
        Assert.True(File.Exists(manifestPath));
        var json = File.ReadAllText(manifestPath);
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.DoesNotContain(_root, json);
        Assert.Contains("cells/difficulty=easy;seed=11", json);
        Assert.Contains("seed:seed", json);

        var moved = _root + "-moved";
        Directory.Move(_root, moved);
        var movedJson = File.ReadAllText(Path.Combine(moved, ScenarioMatrixRunner.ManifestFileName));
        Assert.Equal(json, movedJson);
        Assert.All(report.Cells, c => Assert.True(File.Exists(Path.Combine(moved, c.RelativePath, "marker.txt"))));
    }

    [Fact]
    public void CellStatus_SeparatesAssertionFailureFromExecutionError()
    {
        var matrix = new ScenarioMatrix
        {
            BaseScenario = ScenarioMatrixTestHelpers.BaseScenario(),
            Axes = [new SweepAxis { Name = "seed", Values = ["11"] }],
        };

        var report = ScenarioMatrixRunner.RunCells(matrix, _root, (_, _) => new AssertionReport([]));

        var cell = Assert.Single(report.Cells);
        Assert.Equal("passed", cell.Status);
        Assert.Null(cell.Error);
        Assert.True(cell.AllPassed);
        Assert.Equal("cells/seed=11", cell.RelativePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
            if (Directory.Exists(_root + "-moved"))
            {
                Directory.Delete(_root + "-moved", recursive: true);
            }
        }
    }

}
