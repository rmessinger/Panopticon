#nullable enable

using System;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

public sealed class UnitPackageForwardCompatibilityTests : IDisposable
{
    private readonly string _rootDir;

    public UnitPackageForwardCompatibilityTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "unit_pkg_forward_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
        {
            Directory.Delete(_rootDir, recursive: true);
        }
    }

    [Fact]
    public void UnknownFutureFieldLoadsWithWarningAndSurvivesWriteReadCycleVerbatim()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRoot("Ranger");
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit",
              "displayName": "Ranger",
              "futureArmorPenetrationFactor": 42.5,
              "futureTerrainTag": "ROUGH"
            }
            """);

        var first = new H4UnitPackageReader(builder.Build()).Read([]);

        Assert.DoesNotContain(first.Diagnostics, d => d.IsError);
        var warnings = first.Diagnostics.Where(d => d.Severity == ImportDiagnosticSeverity.Warning).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w => Assert.Equal(UnitPackageDiagnosticCodes.UnknownField, w.Code));

        var resolution = new UnitImportResolution(first.Source, first.Roots, first.Records, []);
        var target = Path.Combine(_rootDir, "future-package");
        var written = H4UnitPackageWriter.Write(target, resolution);

        Assert.True(written.Success, string.Join("; ", written.Diagnostics));

        var reader = new H4UnitPackageReader(target);
        var second = reader.Read([]);

        UnitPackageRoundTripAssert.ClosuresRoundTrip(resolution, second, reader.Natives);

        var unit = second.Records.OfType<UnitRecord>().Single();
        Assert.Equal("42.5", unit.Unclassified.Single(field => field.Name == "futureArmorPenetrationFactor").RawValue);
        Assert.Equal("ROUGH", unit.Unclassified.Single(field => field.Name == "futureTerrainTag").RawValue);
    }

    [Fact]
    public void ReservedPhysicsNamesLoadWithWarningAndSurviveWriteReadCycleVerbatim()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRoot("Ranger");
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit",
              "physicsParticipation": "Dynamic",
              "collisionShapeSource": "Geometry"
            }
            """);

        var first = new H4UnitPackageReader(builder.Build()).Read([]);

        Assert.DoesNotContain(first.Diagnostics, d => d.IsError);
        var warnings = first.Diagnostics.Where(d => d.Severity == ImportDiagnosticSeverity.Warning).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w => Assert.Equal(UnitPackageDiagnosticCodes.ReservedField, w.Code));
        Assert.Contains(warnings, w => w.Message.Contains("physicsParticipation") && w.Message.Contains("AQ-H4-1"));
        Assert.Contains(warnings, w => w.Message.Contains("collisionShapeSource") && w.Message.Contains("AQ-H4-1"));

        var resolution = new UnitImportResolution(first.Source, first.Roots, first.Records, []);
        var target = Path.Combine(_rootDir, "reserved-package");
        var written = H4UnitPackageWriter.Write(target, resolution);

        Assert.True(written.Success, string.Join("; ", written.Diagnostics));

        var reader = new H4UnitPackageReader(target);
        var second = reader.Read([]);

        UnitPackageRoundTripAssert.ClosuresRoundTrip(resolution, second, reader.Natives);

        var unit = second.Records.OfType<UnitRecord>().Single();
        Assert.Equal("Dynamic", unit.Unclassified.Single(field => field.Name == "physicsParticipation").RawValue);
        Assert.Equal("Geometry", unit.Unclassified.Single(field => field.Name == "collisionShapeSource").RawValue);
    }
}
