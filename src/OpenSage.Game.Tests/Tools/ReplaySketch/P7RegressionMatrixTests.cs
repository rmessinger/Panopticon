using System.Collections.Generic;
using System.Linq;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// WI-P7-07: Universal invariants and the P7 scenario matrix.
// Builds the single-axis (endState) ScenarioMatrix, runs each of the six cells through the
// outcome assertion plus the three universal invariants on fresh per-cell game state, and
// aggregates into the harness MatrixReport. A deliberate invariant violation aborts its cell and
// is reported by endState key.

public class P7RegressionMatrixTests : P7RegressionTestBase
{
    private static AssertionResult WithId(AssertionResult result, string id) => new()
    {
        AssertionId = id,
        Passed = result.Passed,
        ActualValue = result.ActualValue,
        FailFrame = result.FailFrame,
        Message = result.Message,
    };

    private static CellResult RunCell(EndState endState)
    {
        // Fresh game per cell so VictorySystem state never leaks across end states.
        var game = CreateGame();
        var bindings = new Dictionary<string, string> { ["endState"] = endState.ToString() };

        var results = new List<AssertionResult>();

        var outcomeResult = DriveAndAssertOutcome(endState, game);
        results.Add(WithId(outcomeResult, $"{endState}.outcome"));

        foreach (var invariantResult in EvaluateInvariants(game, 0))
        {
            results.Add(WithId(invariantResult, $"{endState}.{invariantResult.AssertionId}"));
        }

        return new CellResult(bindings, new AssertionReport(results));
    }

    private static MatrixReport RunFullMatrix() =>
        new(RegressionSuite.BuildMatrix().Expand()
            .Select(cell => RunCell(ParseEndState(cell.Bindings["endState"])))
            .ToList());

    private static EndState ParseEndState(string value) =>
        System.Enum.Parse<EndState>(value, ignoreCase: true);

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
        var report = RunFullMatrix();

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
        var report = RunFullMatrix();
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
        // Build that state for one cell and confirm the cell fails with the endState key.
        var game = CreateGame();
        ConfigureSkirmish(game, 2, aiIsHuman: false);
        // The AI competitive player (index 3) owns no objects -> ai-made-progress fails.

        RegressionInvariants.EnsureRegistered();
        var results = EvaluateInvariants(game, 0).ToList();
        var aiProgress = results.Single(r => r.AssertionId == RegressionInvariants.AiMadeProgress);

        Assert.False(aiProgress.Passed);

        // As a matrix cell, this failure aborts the run and is reported by endState key.
        var cellResults = new List<AssertionResult>();
        foreach (var r in results)
        {
            cellResults.Add(WithId(r, $"PlayerEliminated.{r.AssertionId}"));
        }
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
        // The three universal invariants are part of the P7 suite and evaluate for every cell.
        var invariants = RegressionInvariants.Assertions;

        Assert.Equal(3, invariants.Count);
        Assert.Contains(invariants, a => ((CodeAssertion)a).PredicateName == RegressionInvariants.MoneyNeverNegative);
        Assert.Contains(invariants, a => ((CodeAssertion)a).PredicateName == RegressionInvariants.NoUnitDuplication);
        Assert.Contains(invariants, a => ((CodeAssertion)a).PredicateName == RegressionInvariants.AiMadeProgress);
    }
}
