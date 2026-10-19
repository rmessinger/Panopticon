using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class ScenarioMatrixTests
{
    [Fact]
    public void Expand_TwoAxes_ProducesCartesianProduct()
    {
        var matrix = new ScenarioMatrix
        {
            Axes =
            [
                new SweepAxis { Name = "a", Values = ["x", "y"] },
                new SweepAxis { Name = "b", Values = ["1", "2", "3"] },
            ],
        };

        var cells = matrix.Expand();

        Assert.Equal(6, cells.Count);
    }

    [Fact]
    public void Expand_WithConstraintFilter_PrunesDisallowedCells()
    {
        var matrix = new ScenarioMatrix
        {
            Axes =
            [
                new SweepAxis { Name = "a", Values = ["x", "z"] },
                new SweepAxis { Name = "b", Values = ["y", "q"] },
            ],
            Filters =
            [
                new ConstraintFilter("a==x && b==y", bindings => !(bindings["a"] == "x" && bindings["b"] == "y")),
            ],
        };

        var cells = matrix.Expand();

        Assert.Equal(3, cells.Count);
    }

    [Fact]
    public void AssertionTemplate_Resolve_SubstitutesTokens()
    {
        var template = new AssertionTemplate
        {
            Kind = "state",
            Id = "assert-{idx}",
            Path = "player[{idx}].money",
            Op = ">=",
            Value = "0",
        };

        var concrete = Assert.IsType<StateAssertion>(template.ToConcreteAssertion(new Dictionary<string, string>
        {
            ["idx"] = "0",
        }));

        Assert.Equal("assert-0", concrete.Id);
        Assert.Equal("player[0].money", concrete.Path);
    }

    [Fact]
    public void MatrixReport_ToJson_EmitsCorrectShape()
    {
        var report = new MatrixReport(
        [
            new CellResult(
                new Dictionary<string, string> { ["faction"] = "USA" },
                new AssertionReport(
                [
                    new AssertionResult
                    {
                        AssertionId = "a1",
                        Passed = true,
                    },
                ])),
            new CellResult(
                new Dictionary<string, string> { ["faction"] = "GLA" },
                new AssertionReport(
                [
                    new AssertionResult
                    {
                        AssertionId = "a2",
                        Passed = false,
                        FailFrame = 10,
                    },
                ])),
        ]);

        var json = report.ToJson();

        Assert.Contains("\"totalCells\":2", json);
        Assert.Contains("\"passed\":1", json);
        Assert.Contains("\"failed\":1", json);
        Assert.Contains("\"faction\":\"USA\"", json);
        Assert.Contains("\"faction\":\"GLA\"", json);
    }

    [Fact]
    public void Bindings_ApplyTypedTargetsToConcreteScenarios()
    {
        var matrix = new ScenarioMatrix
        {
            BaseScenario = ScenarioMatrixTestHelpers.BaseScenario(),
            Axes =
            [
                new SweepAxis { Name = "seed", Values = ["11", "22"] },
                new SweepAxis { Name = "count", Values = ["2"] },
            ],
            Bindings =
            [
                new MatrixBinding { Target = MatrixBindingTarget.Seed, Axis = "seed" },
                new MatrixBinding { Target = MatrixBindingTarget.Parameter, Axis = "seed", Key = "difficulty" },
                new MatrixBinding { Target = MatrixBindingTarget.Seam, Axis = "count", Key = "partition" },
                new MatrixBinding { Target = MatrixBindingTarget.PlacementCount, Axis = "count", Key = "AmericaTankCrusader" },
                new MatrixBinding { Target = MatrixBindingTarget.Map, Axis = "seed", Format = "maps/strip {value}/strip {value}.map" },
            ],
        };

        var cells = matrix.Expand();
        Assert.Equal(2, cells.Count);

        foreach (var cell in cells)
        {
            var seedValue = cell.Bindings["seed"] == "11" ? 11u : 22u;
            Assert.Equal(seedValue, cell.ConcreteScenario.Seed);
            Assert.Equal($"maps/strip {seedValue}/strip {seedValue}.map", cell.ConcreteScenario.MapPath);
            Assert.Equal(seedValue.ToString(), cell.ConcreteScenario.Parameters["difficulty"]);
            Assert.Equal("2", cell.SeamSelections["partition"]);
            var placement = Assert.Single(cell.ConcreteScenario.Placements);
            Assert.Equal(2, placement.Quantity);
        }
    }

    [Fact]
    public void Validate_ReportsUnknownPathsAndIncompatibleValues_BeforeExpand()
    {
        var matrix = new ScenarioMatrix
        {
            BaseScenario = ScenarioMatrixTestHelpers.BaseScenario(),
            Axes = [new SweepAxis { Name = "a", Values = ["1", "two"] }],
            Bindings =
            [
                new MatrixBinding { Target = MatrixBindingTarget.Seed, Axis = "missing" },
                new MatrixBinding { Target = MatrixBindingTarget.Parameter, Axis = "a" },
                new MatrixBinding { Target = MatrixBindingTarget.Seed, Axis = "a" },
                new MatrixBinding { Target = MatrixBindingTarget.Seed, Axis = "a" },
            ],
            Filters = [new ConstraintFilter { Terms = [new ConstraintTerm { Axis = "nope", Op = "~=", Value = "1" }] }],
        };

        var diagnostics = matrix.Validate();

        Assert.Contains(diagnostics, d => d.Contains("bindings[0].axis") && d.Contains("missing"));
        Assert.Contains(diagnostics, d => d.Contains("bindings[1].key") && d.Contains("Parameter"));
        Assert.Contains(diagnostics, d => d.Contains("axis 'a' value 'two' does not parse as uint"));
        Assert.Contains(diagnostics, d => d.Contains("duplicate binding"));
        Assert.Contains(diagnostics, d => d.Contains("filters[0].terms[0].axis") && d.Contains("nope"));
        Assert.Contains(diagnostics, d => d.Contains("filters[0].terms[0].op") && d.Contains("~="));
    }

    [Fact]
    public void Validate_AcceptsAWellFormedMatrix()
    {
        var matrix = new ScenarioMatrix
        {
            BaseScenario = ScenarioMatrixTestHelpers.BaseScenario(),
            Axes = [new SweepAxis { Name = "a", Values = ["x", "z"] }],
            Bindings = [new MatrixBinding { Target = MatrixBindingTarget.Seam, Axis = "a", Key = "partition" }],
            Filters = [new ConstraintFilter { Terms = [new ConstraintTerm { Axis = "a", Op = "!=", Value = "z" }] }],
        };

        Assert.Empty(matrix.Validate());
    }

    [Fact]
    public void TermsFilter_PrunesCellsWithoutDelegates()
    {
        var matrix = new ScenarioMatrix
        {
            BaseScenario = ScenarioMatrixTestHelpers.BaseScenario(),
            Axes =
            [
                new SweepAxis { Name = "a", Values = ["x", "z"] },
                new SweepAxis { Name = "b", Values = ["y", "q"] },
            ],
            Filters =
            [
                new ConstraintFilter
                {
                    Expression = "a==x && b==y",
                    Terms =
                    [
                        new ConstraintTerm { Axis = "a", Op = "==", Value = "x" },
                        new ConstraintTerm { Axis = "b", Op = "==", Value = "y" },
                    ],
                },
            ],
        };

        var cells = matrix.Expand();

        var cell = Assert.Single(cells);
        Assert.Equal("x", cell.Bindings["a"]);
        Assert.Equal("y", cell.Bindings["b"]);
    }
}


internal static class ScenarioMatrixTestHelpers
{
    public static ReplayScenario BaseScenario() => new()
    {
        MapPath = "maps/alpine assault/alpine assault.map",
        Placements = [new ScenarioPlacement
        {
            DefinitionName = "AmericaTankCrusader",
            PlayerIndex = 0,
            Position = new NormalizedPosition(0.5f, 0.5f),
        }],
        Players = [new PlayerSlotConfig { Name = "USA", FactionIndex = 2 }],
    };
}
