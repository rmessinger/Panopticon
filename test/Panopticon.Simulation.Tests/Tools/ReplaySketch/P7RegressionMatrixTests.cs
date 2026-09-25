using System.Collections.Generic;
using System.Linq;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

// WI-P7-07: Universal invariants and the P7 scenario matrix.
// Builds the single-axis (endState) ScenarioMatrix, runs each of the six cells through the
// outcome assertion plus the three universal invariants on fresh per-cell game state, and
// aggregates into the harness MatrixReport. A deliberate invariant violation aborts its cell and
// is reported by endState key.

public class P7RegressionMatrixTests : P7RegressionTestBase
{
    [Fact]
    public void Matrix_HasSingleEndStateAxisWithSixValues()
    {
        var matrix = RegressionSuite.BuildMatrix();

        var axis = Assert.Single(matrix.Axes);
        Assert.Equal("endState", axis.Name);
        Assert.Equal(6, axis.Values.Count);
    }

    [Fact]
    public void Matrix_Expansion_ProducesOneCellPerEndState()
    {
        var cells = RegressionSuite.BuildMatrix().Expand();

        Assert.Equal(6, cells.Count);
        var keys = cells.Select(c => c.Bindings["endState"]).OrderBy(n => n).ToList();
        Assert.Equal(
            new[] { "AISurrenders", "MutualElimination", "PlayerEliminated", "PlayerSurrenders", "ScoreDecision", "TeamWin" },
            keys);
    }

    [Fact]
    public void Matrix_AllEndStateCells_PassWithInvariants()
    {
        var report = RunRegressionMatrix();

        Assert.Equal(6, report.Cells.Count);
        Assert.True(report.AllPassed,
            "P7 matrix failed: " + string.Join("; ", report.Cells
                .Where(c => !c.Report.AllPassed)
                .Select(c => $"{c.Key["endState"]}: {string.Join(", ", c.Report.Results.Where(r => !r.Passed).Select(r => r.Message))}")));
        // Each cell carries one outcome assertion plus the three universal invariants.
        foreach (var cell in report.Cells)
        {
            Assert.Equal(4, cell.Report.Results.Count);
        }
    }

    [Fact]
    public void Matrix_Report_IdentifiesEachCellByEndState()
    {
        var report = RunRegressionMatrix();
        var json = report.ToJson();

        Assert.Contains("\"totalCells\":6", json, System.StringComparison.Ordinal);
        foreach (var endState in new[] { "PlayerEliminated", "PlayerSurrenders", "AISurrenders", "TeamWin", "MutualElimination", "ScoreDecision" })
        {
            Assert.Contains($"\"endState\":\"{endState}\"", json, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Matrix_InvariantViolation_AbortsAndReportsCell()
    {
        // A non-human competitive player that owns nothing violates the AI-progress invariant.
        var game = CreateGame();
        ConfigureSkirmish(game, 2, aiIsHuman: false);

        RegressionInvariants.EnsureRegistered();
        var results = EvaluateInvariants(game, 0).ToList();
        var aiProgress = results.Single(r => r.AssertionId == RegressionInvariants.AiMadeProgress);

        Assert.False(aiProgress.Passed);

        // As a matrix cell, this failure aborts the run and is reported by endState key.
        var cellResults = results
            .Select(r => new AssertionResult
            {
                AssertionId = $"PlayerEliminated.{r.AssertionId}",
                Passed = r.Passed,
                ActualValue = r.ActualValue,
                FailFrame = r.FailFrame,
                Message = r.Message,
            })
            .ToList();
        var cell = new CellResult(
            new Dictionary<string, string> { ["endState"] = "PlayerEliminated" },
            new AssertionReport(cellResults));

        Assert.False(cell.Report.AllPassed);
        var json = new MatrixReport([cell]).ToJson();
        Assert.Contains("\"endState\":\"PlayerEliminated\"", json, System.StringComparison.Ordinal);
        Assert.Contains(RegressionInvariants.AiMadeProgress, json, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Matrix_UniversalInvariants_AttachedToAllSixScenarios()
    {
        var invariants = RegressionInvariants.Assertions;

        Assert.Equal(3, invariants.Count);
        Assert.Contains(invariants, a => ((CodeAssertion)a).PredicateName == RegressionInvariants.MoneyNeverNegative);
        Assert.Contains(invariants, a => ((CodeAssertion)a).PredicateName == RegressionInvariants.NoUnitDuplication);
        Assert.Contains(invariants, a => ((CodeAssertion)a).PredicateName == RegressionInvariants.AiMadeProgress);
    }
}
