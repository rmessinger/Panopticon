using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Benchmarks.Simulation.Experiments;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// A4-38 experiment definition construction: argument validation, axis expansion with stable
/// cell keys, and JSON round-trip, all without a game installation.
/// </summary>
public class ExperimentDefinitionTests
{
    private static ExperimentDefinition CreateDefinition(
        IReadOnlyList<ExperimentAxis>? axes = null,
        string name = "policy-comparison",
        string scenarioId = "b1.empty-host")
    {
        return new ExperimentDefinition(name, scenarioId, 3, 5, 60, axes ?? [new ExperimentAxis("ai-policy", ["rule-based", "neural"])]);
    }

    [Fact]
    public void Constructor_RetainsValues()
    {
        var definition = CreateDefinition();

        Assert.Equal("policy-comparison", definition.Name);
        Assert.Equal("b1.empty-host", definition.ScenarioId);
        Assert.Equal(3u, definition.Seed);
        Assert.Equal(5, definition.WarmupFrames);
        Assert.Equal(60, definition.MeasuredFrames);
        Assert.Equal("a4.experiment-definition/1", definition.SchemaVersion);
    }

    [Fact]
    public void Constructor_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => CreateDefinition(name: " "));
        Assert.Throws<ArgumentException>(() => CreateDefinition(scenarioId: ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentDefinition("n", "s", 1, -1, 10, [new ExperimentAxis("ai-policy", ["neural"])]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperimentDefinition("n", "s", 1, 1, 0, [new ExperimentAxis("ai-policy", ["neural"])]));
        Assert.Throws<ArgumentException>(() => CreateDefinition(axes: []));
    }

    [Fact]
    public void Expand_ProducesCartesianCellsWithStableKeys()
    {
        var definition = CreateDefinition(axes:
        [
            new ExperimentAxis("seam-a", ["one", "two"]),
            new ExperimentAxis("seam-b", ["x"]),
        ]);

        var cells = definition.Expand();

        Assert.Equal(2, cells.Count);
        Assert.Equal("seam-a=one;seam-b=x", cells[0].Key);
        Assert.Equal("seam-a=two;seam-b=x", cells[1].Key);
        Assert.Equal("two", cells[1].Bindings["seam-a"]);
        Assert.Equal("x", cells[1].Bindings["seam-b"]);
        Assert.Equal(cells[0].Key, definition.Expand()[0].Key);
    }

    [Fact]
    public void Expand_SingleAxisYieldsOneCellPerImplementation()
    {
        var cells = CreateDefinition().Expand();

        Assert.Equal(2, cells.Count);
        Assert.Equal("ai-policy=rule-based", cells[0].Key);
        Assert.Equal("ai-policy=neural", cells[1].Key);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var definition = CreateDefinition();

        var restored = ExperimentDefinition.FromJson(definition.ToJson());

        Assert.Equal(definition.Name, restored.Name);
        Assert.Equal(definition.ScenarioId, restored.ScenarioId);
        Assert.Equal(definition.Seed, restored.Seed);
        Assert.Equal(definition.WarmupFrames, restored.WarmupFrames);
        Assert.Equal(definition.MeasuredFrames, restored.MeasuredFrames);
        Assert.Equal(definition.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(definition.Axes.Single().Seam, restored.Axes.Single().Seam);
        Assert.Equal(definition.Axes.Single().Implementations, restored.Axes.Single().Implementations);
    }

    [Fact]
    public void FromJson_RejectsNonDefinitionJson()
    {
        Assert.Throws<ArgumentException>(() => ExperimentDefinition.FromJson("{}"));
        Assert.Throws<ArgumentException>(() => ExperimentDefinition.FromJson("not json"));
    }

    [Fact]
    public void FromJson_RejectsUnsupportedSchemaVersion()
    {
        var json = CreateDefinition().ToJson().Replace("a4.experiment-definition/1", "a4.experiment-definition/9");

        var error = Assert.Throws<ArgumentException>(() => ExperimentDefinition.FromJson(json));
        Assert.Contains("not supported", error.Message);
        Assert.Contains("a4.experiment-definition/9", error.Message);
    }
}
