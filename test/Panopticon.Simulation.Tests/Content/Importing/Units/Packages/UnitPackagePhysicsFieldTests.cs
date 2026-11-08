#nullable enable

using System;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

public sealed class UnitPackagePhysicsFieldTests : IDisposable
{
    private readonly string _rootDir;

    public UnitPackagePhysicsFieldTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "unit_pkg_physics_" + Guid.NewGuid().ToString("N"));
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
    public void PhysicsFieldsRoundTripWithAndWithoutValues()
    {
        var source = ContentSource.Generals;
        var physics = new UnitRecord(new ContentId(source, ContentKind.Unit, "PhysicsUnit"), new Provenance("generals", "Data/INI/Physics.ini", 1))
        {
            CollisionLayer = Authored.Of("LAYER_GROUND"),
            CollisionMask = ListOverride<string>.Replace(["LAYER_GROUND", "LAYER_AIR"]),
            PhysicsMaterial = Authored.Of("MATERIAL_INFANTRY"),
        };
        var bare = new UnitRecord(new ContentId(source, ContentKind.Unit, "BareUnit"), new Provenance("generals", "Data/INI/Physics.ini", 9));
        var resolution = new UnitImportResolution(source, [physics.Id, bare.Id], [physics, bare], []);

        var target = Path.Combine(_rootDir, "physics-package");
        var result = H4UnitPackageWriter.Write(target, resolution);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var reader = new H4UnitPackageReader(target);
        var set = reader.Read([]);

        UnitPackageRoundTripAssert.ClosuresRoundTrip(resolution, set, reader.Natives);

        var reread = set.Records.OfType<UnitRecord>().Single(record => record.Id.Name == "PhysicsUnit");
        Assert.Equal("LAYER_GROUND", reread.CollisionLayer.Value);
        Assert.Equal(ListOverrideMode.Replace, reread.CollisionMask.Mode);
        Assert.Equal(["LAYER_GROUND", "LAYER_AIR"], reread.CollisionMask.Items);
        Assert.Equal("MATERIAL_INFANTRY", reread.PhysicsMaterial.Value);

        var bareReread = set.Records.OfType<UnitRecord>().Single(record => record.Id.Name == "BareUnit");
        Assert.False(bareReread.CollisionLayer.IsPresent);
        Assert.Equal(ListOverrideMode.Inherit, bareReread.CollisionMask.Mode);
        Assert.False(bareReread.PhysicsMaterial.IsPresent);
    }

    [Fact]
    public void PackageWrittenBeforePhysicsFieldsLoadsWithPhysicsFieldsAbsent()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit",
              "displayName": "Ranger",
              "kindOf": ["INFANTRY"]
            }
            """);

        var reader = new H4UnitPackageReader(builder.Build());
        var set = reader.Read([]);

        Assert.Empty(set.Diagnostics);

        var unit = Assert.IsType<UnitRecord>(Assert.Single(set.Records));
        Assert.False(unit.CollisionLayer.IsPresent);
        Assert.Equal(ListOverrideMode.Inherit, unit.CollisionMask.Mode);
        Assert.False(unit.PhysicsMaterial.IsPresent);
    }
}
