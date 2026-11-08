#nullable enable

using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

public sealed class UnitPackageVersionTests
{
    [Fact]
    public void UnknownMajorSchemaVersionIsRejectedWithDiagnosticError()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.WithSchemaVersion(2);
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.SchemaVersion, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void ContainerKindMismatchIsRejectedWithDiagnosticError()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.WithContainerKind("wrong-package-kind");
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.ContainerKind, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void UnknownManifestMembersProduceWarningAndAreIgnored()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.WithExtraManifestProperty("futureManifestFeature", "someValue");
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read([]);

        Assert.DoesNotContain(set.Diagnostics, d => d.IsError);
        Assert.Single(set.Records);

        var warning = Assert.Single(set.Diagnostics, d => d.Severity == ImportDiagnosticSeverity.Warning);
        Assert.Equal(UnitPackageDiagnosticCodes.UnknownManifestMember, warning.Code);
        Assert.Contains("futureManifestFeature", warning.Message);
    }

    [Fact]
    public void UnknownRecordPayloadFieldIsRetainedAsValuedUnclassifiedWithWarning()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit",
              "displayName": "Ranger",
              "futureArmorPenetrationFactor": 42.5,
              "futureComplexBlock": ["alpha", "beta"]
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read([]);

        Assert.DoesNotContain(set.Diagnostics, d => d.IsError);
        var unit = Assert.Single(set.Records.OfType<UnitRecord>());

        var warnings = set.Diagnostics.Where(d => d.Severity == ImportDiagnosticSeverity.Warning).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w => Assert.Equal(UnitPackageDiagnosticCodes.UnknownField, w.Code));
        Assert.Contains(warnings, w => w.Message.Contains("futureArmorPenetrationFactor"));
        Assert.Contains(warnings, w => w.Message.Contains("futureComplexBlock"));

        Assert.Equal(2, unit.Unclassified.Count);
        var scalarField = unit.Unclassified.Single(f => f.Name == "futureArmorPenetrationFactor");
        Assert.Equal("42.5", scalarField.RawValue);
        Assert.False(scalarField.AffectsGameplay);

        var blockField = unit.Unclassified.Single(f => f.Name == "futureComplexBlock");
        Assert.NotNull(blockField.RawBlock);
        Assert.Equal(["alpha", "beta"], blockField.RawBlock);
    }

    [Fact]
    public void MissingRetainedPayloadForLegacyBoundRecordIsHardError()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit",
              "hasLegacyBinding": true
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.MissingRetainedPayload, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void MissingRetainedPayloadForLegacyBoundModuleIsHardError()
    {
        using var builder = new UnitPackageTestBuilder();

        // Retained INI contains Ranger without ModuleTag_Custom
        builder.AddRetainedPayload(
            "retained/ranger.ini",
            UnitPackageFormat.SupportedGeneralsIniDialect,
            UnitPackageFormat.SupportedGeneralsIniDialectVersion,
            """
            Object Ranger
              Body = ActiveBody ModuleTag_Body
                MaxHealth = 180.0
                InitialHealth = 180.0
              End
            End
            """);

        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit",
              "hasLegacyBinding": true,
              "modules": [
                {
                  "operation": "Add",
                  "tag": "ModuleTag_Missing",
                  "module": {
                    "tag": "ModuleTag_Missing",
                    "family": "ActiveBody",
                    "hasLegacyBinding": true
                  }
                }
              ]
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.MissingRetainedPayload, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void UnsupportedDialectIsHardError()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRetainedPayload(
            "retained/unit.dat",
            "unsupported-dialect",
            1,
            "some dummy content");

        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.UnsupportedDialect, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void UnsupportedDialectVersionIsHardError()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRetainedPayload(
            "retained/unit.ini",
            UnitPackageFormat.SupportedGeneralsIniDialect,
            dialectVersion: 99,
            "some dummy content");

        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.UnsupportedDialectVersion, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void NumericEnumStringIsRejectedWithoutSilentCoercion()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit",
              "maxHealth": { "value": 180.0, "unit": "1" }
            }
            """);

        var packageDir = builder.Build();
        var reader = new H4UnitPackageReader(packageDir);

        var set = reader.Read([]);

        // Unit with numeric enum string "1" for QuantityUnit must be rejected as an invalid quantity or omitted/error
        // Here maxHealth unit "1" fails StrictEnumParser and fails TryParse
        var unit = set.Records.OfType<UnitRecord>().FirstOrDefault();
        Assert.NotNull(unit);
        Assert.False(unit.MaxHealth.IsPresent);
    }

    [Fact]
    public void NumericEnumStringInManifestRecordKindIsRejectedAsError()
    {
        using var builder = new UnitPackageTestBuilder();
        var packageDir = builder.Build();

        // Manifest record entry declaring numeric kind "0" instead of "Unit"
        var manifestPath = System.IO.Path.Combine(packageDir, UnitPackageFormat.ManifestFileName);
        var manifestObj = new
        {
            containerKind = UnitPackageFormat.ContainerKind,
            schemaVersion = UnitPackageFormat.CurrentSchemaVersion,
            source = new { id = "generals", caseSensitiveNames = false },
            records = new[]
            {
                new
                {
                    name = "Ranger",
                    kind = "0",
                    path = "records/ranger.json",
                    byteLength = 10,
                    sha256 = new string('a', 64),
                },
            },
        };

        System.IO.File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifestObj));

        var reader = new H4UnitPackageReader(packageDir);
        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.InvalidEnumValue, diag.Code);
        Assert.Empty(set.Records);
    }
}
