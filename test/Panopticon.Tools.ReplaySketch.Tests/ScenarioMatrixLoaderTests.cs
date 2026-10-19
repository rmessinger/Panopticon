using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class ScenarioMatrixLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-matrix-{Guid.NewGuid():N}");

    private string PathFor(string name) => Path.Combine(_root, name);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void RoundTrip_PreservesAxesTermsBindingsTemplatesAndScenario()
    {
        Directory.CreateDirectory(_root);
        var document = new ScenarioMatrixDocument
        {
            Scenario = ScenarioMatrixTestHelpers.BaseScenario(),
            Axes = [new SweepAxis { Name = "seed", Values = ["11", "22"] }],
            Filters =
            [
                new ConstraintFilter
                {
                    Expression = "seed != 22",
                    Terms = [new ConstraintTerm { Axis = "seed", Op = "!=", Value = "22" }],
                },
            ],
            Bindings =
            [
                new MatrixBinding { Target = MatrixBindingTarget.Seed, Axis = "seed" },
                new MatrixBinding { Target = MatrixBindingTarget.Seam, Axis = "seed", Key = "partition" },
            ],
            AssertionTemplates = [new AssertionTemplate { Kind = "state", Path = "player[0].money", Op = ">=", Value = "0" }],
        };
        var path = PathFor("matrix.json");
        document.Save(path);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("predicate", json, StringComparison.Ordinal);

        var loaded = ScenarioMatrixDocument.Load(path);
        Assert.Equal(1, loaded.SchemaVersion);
        var axis = Assert.Single(loaded.Axes);
        Assert.Equal("seed", axis.Name);
        var filter = Assert.Single(loaded.Filters);
        var term = Assert.Single(filter.Terms);
        Assert.Equal("!=", term.Op);
        Assert.Equal("22", term.Value);
        var binding = Assert.Single(loaded.Bindings, b => b.Target == MatrixBindingTarget.Seam);
        Assert.Equal("partition", binding.Key);
        var template = Assert.Single(loaded.AssertionTemplates);
        Assert.Equal("player[0].money", template.Path);
        Assert.Equal("maps/alpine assault/alpine assault.map", loaded.Scenario.MapPath);
    }

    [Fact]
    public void VersionMismatch_IsRejectedNamingBothVersions()
    {
        Directory.CreateDirectory(_root);
        var path = PathFor("future.json");
        File.WriteAllText(path, """{"schemaVersion": 99, "scenario": {"mapPath": "maps/a/a.map", "players": [{"name": "P"}]}}""");

        var error = Assert.Throws<InvalidOperationException>(() => ScenarioMatrixDocument.Load(path));
        Assert.Contains("99", error.Message);
        Assert.Contains("1", error.Message);
    }

    [Fact]
    public void InvalidBindings_AreRejectedAtLoad()
    {
        Directory.CreateDirectory(_root);
        var document = new ScenarioMatrixDocument
        {
            Scenario = ScenarioMatrixTestHelpers.BaseScenario(),
            Axes = [new SweepAxis { Name = "a", Values = ["x"] }],
            Bindings = [new MatrixBinding { Target = MatrixBindingTarget.Seed, Axis = "missing" }],
        };
        var path = PathFor("invalid.json");
        document.Save(path);

        var error = Assert.Throws<InvalidOperationException>(() => ScenarioMatrixDocument.Load(path));
        Assert.Contains("bindings[0].axis", error.Message);
    }

    [Fact]
    public void LoadedDocument_ExpandsThroughTheSameMatrixModel()
    {
        Directory.CreateDirectory(_root);
        var document = new ScenarioMatrixDocument
        {
            Scenario = ScenarioMatrixTestHelpers.BaseScenario(),
            Axes =
            [
                new SweepAxis { Name = "seed", Values = ["11", "22"] },
                new SweepAxis { Name = "skip", Values = ["yes", "no"] },
            ],
            Bindings = [new MatrixBinding { Target = MatrixBindingTarget.Seed, Axis = "seed" }],
            Filters = [new ConstraintFilter { Terms = [new ConstraintTerm { Axis = "skip", Op = "==", Value = "no" }] }],
        };
        var path = PathFor("expandable.json");
        document.Save(path);

        var cells = ScenarioMatrixDocument.Load(path).ToMatrix().Expand();

        Assert.Equal(2, cells.Count);
        Assert.All(cells, cell => Assert.Equal("no", cell.Bindings["skip"]));
        Assert.Contains(cells, cell => cell.ConcreteScenario.Seed == 11u);
        Assert.Contains(cells, cell => cell.ConcreteScenario.Seed == 22u);
    }
}
