using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Simulation;
using Xunit;

namespace Panopticon.Tests.Simulation;

public sealed class DomainStateSchemaTests
{
    private static DomainStateField Int(string name, string family = "vehicles", DomainConservationRole role = DomainConservationRole.Holding)
        => new(name, "persons", DomainQuantityType.Integer, role, family);

    private static DomainStateSchema ValidSchema() => new(
        "transport-graph",
        1,
        new[]
        {
            new DomainRecordKind(
                "node",
                "network",
                new DomainStateField[]
                {
                    new("occupancy", "vehicles", DomainQuantityType.Integer, DomainConservationRole.Holding, "vehicles"),
                    new("labelled", "index", DomainQuantityType.Integer, DomainConservationRole.None, null),
                },
                Array.Empty<string>()),
            new DomainRecordKind(
                "vehicle",
                "movement",
                new DomainStateField[]
                {
                    new("edge_progress", "ratio", DomainQuantityType.Real, DomainConservationRole.None, null),
                },
                new[] { "edge" }),
        });

    [Fact]
    public void ValidSchemaPassesAndCountsFields()
    {
        var schema = ValidSchema();

        schema.Validate();

        var node = schema.FindKind("node");
        Assert.Equal(2, node.IntegerFieldCount);
        Assert.Equal(0, node.RealFieldCount);
        var vehicle = schema.FindKind("vehicle");
        Assert.Equal(0, vehicle.IntegerFieldCount);
        Assert.Equal(1, vehicle.RealFieldCount);
        Assert.Equal(new[] { "edge" }, vehicle.ReferenceNames);
    }

    [Fact]
    public void FindKind_RejectsUnknownName()
    {
        var schema = ValidSchema();

        Assert.Throws<InvalidOperationException>(() => schema.FindKind("formation"));
    }

    [Fact]
    public void DuplicateKindNameFails()
    {
        var schema = ValidSchema().WithKinds(new[]
        {
            ValidSchema().Kinds[0],
            ValidSchema().Kinds[0],
        });

        var error = Assert.Throws<InvalidOperationException>(schema.Validate);

        Assert.Contains("node", error.Message);
        Assert.Contains("more than once", error.Message);
    }

    [Fact]
    public void DuplicateFieldNameWithinKindFails()
    {
        var schema = ValidSchema().WithKinds(new[]
        {
            new DomainRecordKind(
                "node",
                "network",
                new DomainStateField[]
                {
                    Int("occupancy"),
                    Int("occupancy"),
                },
                Array.Empty<string>()),
        });

        var error = Assert.Throws<InvalidOperationException>(schema.Validate);

        Assert.Contains("occupancy", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyUnitFails(string unit)
    {
        var schema = ValidSchema().WithKinds(new[]
        {
            new DomainRecordKind(
                "node",
                "network",
                new DomainStateField[] { new("occupancy", unit, DomainQuantityType.Integer, DomainConservationRole.None, null) },
                Array.Empty<string>()),
        });

        var error = Assert.Throws<InvalidOperationException>(schema.Validate);

        Assert.Contains("occupancy", error.Message);
        Assert.Contains("empty unit", error.Message);
    }

    [Fact]
    public void EmptyLayerFails()
    {
        var schema = ValidSchema().WithKinds(new[]
        {
            new DomainRecordKind(
                "node",
                "",
                new DomainStateField[] { Int("occupancy") },
                Array.Empty<string>()),
        });

        var error = Assert.Throws<InvalidOperationException>(schema.Validate);

        Assert.Contains("node", error.Message);
        Assert.Contains("empty layer", error.Message);
    }

    [Fact]
    public void RealConservedFieldFails()
    {
        var schema = ValidSchema().WithKinds(new[]
        {
            new DomainRecordKind(
                "node",
                "network",
                new DomainStateField[]
                {
                    new("strength", "ratio", DomainQuantityType.Real, DomainConservationRole.Holding, "vehicles"),
                },
                Array.Empty<string>()),
        });

        var error = Assert.Throws<InvalidOperationException>(schema.Validate);

        Assert.Contains("strength", error.Message);
        Assert.Contains("must be integers", error.Message);
    }

    [Fact]
    public void ConservedFieldWithoutFamilyFails()
    {
        var schema = ValidSchema().WithKinds(new[]
        {
            new DomainRecordKind(
                "node",
                "network",
                new DomainStateField[]
                {
                    new("occupancy", "vehicles", DomainQuantityType.Integer, DomainConservationRole.Holding, null),
                },
                Array.Empty<string>()),
        });

        var error = Assert.Throws<InvalidOperationException>(schema.Validate);

        Assert.Contains("occupancy", error.Message);
        Assert.Contains("without a family", error.Message);
    }

    [Fact]
    public void FamilyOnNeutralFieldFails()
    {
        var schema = ValidSchema().WithKinds(new[]
        {
            new DomainRecordKind(
                "node",
                "network",
                new DomainStateField[]
                {
                    new("labelled", "index", DomainQuantityType.Integer, DomainConservationRole.None, "vehicles"),
                },
                Array.Empty<string>()),
        });

        var error = Assert.Throws<InvalidOperationException>(schema.Validate);

        Assert.Contains("labelled", error.Message);
        Assert.Contains("family", error.Message);
    }
}

file static class DomainStateSchemaTestExtensions
{
    public static DomainStateSchema WithKinds(this DomainStateSchema schema, IReadOnlyList<DomainRecordKind> kinds)
        => new(schema.DomainId, schema.Version, kinds);
}
