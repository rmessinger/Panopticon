using System.Collections.Generic;
using System.Linq;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// WI-P6b-08: P6b scenario matrix.
// Real CI [Fact] coverage: a ScenarioMatrix sweeps archetype x faction, a ConstraintFilter prunes
// cells without a reference unit, and every valid cell runs the full six-point contract. The
// aggregate MatrixReport names failing cells by their archetype x faction key; unimplemented
// handlers appear as explicit "P6b gap:" failures, never silent passes.

public class P6bScenarioMatrixTests : P6bContractTestBase
{
    private static readonly IReadOnlyList<string> ArchetypeAxis =
        UnitArchetypeCatalog.Archetypes.Select(a => a.ToString()).ToList();

    private static ScenarioMatrix BuildMatrix()
    {
        return new ScenarioMatrix
        {
            Axes =
            [
                new SweepAxis { Name = "archetype", Values = ArchetypeAxis },
                new SweepAxis { Name = "faction", Values = UnitArchetypeCatalog.Factions },
            ],
            Filters =
            [
                new ConstraintFilter(
                    "hasReferenceUnit(archetype, faction)",
                    bindings => UnitArchetypeCatalog.HasReferenceUnit(
                        ParseArchetype(bindings["archetype"]), bindings["faction"])),
            ],
        };
    }

    private static UnitArchetype ParseArchetype(string value) =>
        System.Enum.Parse<UnitArchetype>(value, ignoreCase: true);

    /// <summary>
    /// Runs the full contract for one matrix cell and lifts the result into a harness
    /// <see cref="AssertionReport"/> (one AssertionResult per contract point) so the aggregate
    /// <see cref="MatrixReport"/> reuses the P0-5b report shape with per-cell keys.
    /// </summary>
    private static CellResult RunCell(IReadOnlyDictionary<string, string> bindings)
    {
        var archetype = ParseArchetype(bindings["archetype"]);
        var faction = bindings["faction"];
        var scenario = UnitArchetypeCatalog.BuildScenario(archetype, faction);
        var contract = RunContract(scenario);

        var results = contract.Results.Select(r => new AssertionResult
        {
            AssertionId = $"{scenario.UnitDefinitionName}.{r.Point}",
            Passed = r.Passed,
            ActualValue = r.Outcome.ToString(),
            Message = r.Detail,
        }).ToList();

        return new CellResult(bindings, new AssertionReport(results));
    }

    [Fact]
    public void Matrix_Axes_ArchetypeByFaction_RawExpansionIsEighteen()
    {
        var matrix = BuildMatrix();

        // Apply only the axes (no filter) to count the raw archetype x faction cells.
        var axesOnly = new ScenarioMatrix
        {
            Axes = matrix.Axes,
        };

        Assert.Equal(18, axesOnly.Expand().Count);
    }

    [Fact]
    public void Matrix_ConstraintFilter_PrunesCellsWithoutReferenceUnit()
    {
        var matrix = BuildMatrix();
        var validCells = matrix.Expand();

        // 18 raw cells; GLA aircraft and GLA support have no reference unit and are pruned.
        Assert.Equal(UnitArchetypeCatalog.ValidCellCount, validCells.Count);
        Assert.Equal(2, 18 - validCells.Count);

        // GLA keeps four archetypes (infantry, vehicle, builder, superweapon); aircraft and
        // support are the pruned cells (they are absent from the valid set).
        var glaValid = validCells
            .Where(c => c.Bindings["faction"] == UnitArchetypeCatalog.FactionGLA)
            .Select(c => c.Bindings["archetype"])
            .OrderBy(n => n)
            .ToList();
        Assert.Equal(
            new[]
            {
                UnitArchetype.Builder.ToString(),
                UnitArchetype.Infantry.ToString(),
                UnitArchetype.Superweapon.ToString(),
                UnitArchetype.Vehicle.ToString(),
            },
            glaValid);

        var pruned = ArchetypeAxis.Except(glaValid).OrderBy(n => n).ToList();
        Assert.Equal(
            new[] { UnitArchetype.Aircraft.ToString(), UnitArchetype.Support.ToString() },
            pruned);
    }

    [Fact]
    public void Matrix_EachValidCell_HasArchetypeAndFactionKeys()
    {
        var matrix = BuildMatrix();

        foreach (var cell in matrix.Expand())
        {
            Assert.True(cell.Bindings.ContainsKey("archetype"));
            Assert.True(cell.Bindings.ContainsKey("faction"));
            Assert.Contains(cell.Bindings["faction"], UnitArchetypeCatalog.Factions);
            Assert.Contains(cell.Bindings["archetype"], ArchetypeAxis);
        }
    }

    [Fact]
    public void Matrix_AllValidCells_RunFullContract()
    {
        var matrix = BuildMatrix();
        var cells = matrix.Expand().Select(c => RunCell(c.Bindings)).ToList();

        var report = new MatrixReport(cells);

        Assert.Equal(UnitArchetypeCatalog.ValidCellCount, cells.Count);

        // Every cell reports exactly the six contract points.
        foreach (var cell in cells)
        {
            Assert.Equal(UnitContractScenario.DefaultContractPoints.Count, cell.Report.Results.Count);
        }

        // Cells whose reference unit has an unimplemented special power surface explicit gaps
        // (support/superweapon). Those cells must NOT be AllPassed, and every failing result must
        // carry a "P6b gap:" message naming the unit and special power. Non-gap cells pass cleanly.
        foreach (var cell in cells)
        {
            foreach (var result in cell.Report.Results)
            {
                if (!result.Passed)
                {
                    Assert.StartsWith("P6b gap:", result.Message);
                }
            }
        }

        // The matrix as a whole does not pass until the documented gaps are closed.
        Assert.False(report.AllPassed);
    }

    [Fact]
    public void Matrix_GapCells_ReportedInAggregateWithCellKey()
    {
        var matrix = BuildMatrix();
        var report = new MatrixReport(matrix.Expand().Select(c => RunCell(c.Bindings)).ToList());
        var json = report.ToJson();

        // The aggregate report carries total/passed/failed counts and per-cell keys.
        Assert.Contains("\"totalCells\":16", json, System.StringComparison.Ordinal);

        // Each failing cell is identified by its archetype x faction key and its gap detail.
        var failingCells = report.Cells.Where(c => !c.Report.AllPassed).ToList();
        Assert.NotEmpty(failingCells);
        foreach (var cell in failingCells)
        {
            var key = cell.Key;
            Assert.Contains($"\"archetype\":\"{key["archetype"]}\"", json, System.StringComparison.Ordinal);
            Assert.Contains($"\"faction\":\"{key["faction"]}\"", json, System.StringComparison.Ordinal);
            Assert.Contains("P6b gap:", json, System.StringComparison.Ordinal);
        }

        // The failing cells are exactly the superweapon archetypes (USA/China/GLA, each fires an
        // unimplemented particle/nuclear/scud power). The USA Ambulance support no longer fails:
        // P6c implemented its CleanupArea handler (see P6bSupportContractTests). China support
        // (Hacker) and GLA support are not failing: the Hacker has no command-set
        // SpecialPowerType, and GLA support is pruned.
        var failingKeys = failingCells
            .Select(c => (archetype: c.Key["archetype"], faction: c.Key["faction"]))
            .OrderBy(t => t.archetype)
            .ThenBy(t => t.faction)
            .ToList();
        Assert.Equal(
            new[]
            {
                (UnitArchetype.Superweapon.ToString(), UnitArchetypeCatalog.FactionChina),
                (UnitArchetype.Superweapon.ToString(), UnitArchetypeCatalog.FactionGLA),
                (UnitArchetype.Superweapon.ToString(), UnitArchetypeCatalog.FactionUSA),
            },
            failingKeys);
    }
}
