using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tests.Tools.ReplaySketch;

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
}
