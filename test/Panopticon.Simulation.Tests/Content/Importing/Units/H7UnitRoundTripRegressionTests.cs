#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Data.Ini;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Tests.Content.Tools.H5Migrate;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Unit round-trip regression suite for WI-H7-02, synthetic roster (routine CI, no install).
/// The legacy INI roster converts in-process through the actual convert-ini pipeline
/// (H5MigrateCli.Run with a synthetic install root, plus the same pipeline's own write stage for
/// the full-kind closure that unit-name roots cannot express), is re-read through
/// H4UnitPackageReader, and compared only through the shared H5-03 RoundTripValidator and its
/// delegated UnitPackageConversionVerifier. No golden baselines and no second comparer.
/// </summary>
public sealed class H7UnitRoundTripRegressionTests
{
    private const string MissingRecordCode = "H5-CLI-013";

    private const string ExtraRecordCode = "H5-CLI-014";

    private const string TypedMismatchCode = "H5-CLI-006";

    [Fact]
    public void SyntheticRoster_ConvertIniRoundTrip_ValidatorPassesEverySection()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var installRoot = H7SyntheticIniRoster.WriteTo(Path.Combine(tempRoot, "install"));
            var packageDir = Path.Combine(tempRoot, "package");

            using (var standardOut = new StringWriter())
            using (var errorOut = new StringWriter())
            {
                var exitCode = H5MigrateCli.Run(
                    ["convert-ini", "--game-path", installRoot, "--out", packageDir, .. H7SyntheticIniRoster.RosterRoots],
                    standardOut,
                    errorOut,
                    CancellationToken.None);

                Assert.True(exitCode == 0, $"convert-ini failed (exit {exitCode}): {errorOut}");
            }

            var expected = LoadExpectedResolution(installRoot, H7SyntheticIniRoster.RosterRoots);
            var provider = new IniRetainedPayloadProvider(new DiskFileSystem(installRoot));
            var report = RoundTripValidator.ValidateUnits(expected.Resolution, expected.Adapter.Natives, provider, packageDir);

            Assert.True(
                report.Passed,
                $"RoundTripValidator failed: {string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => $"{f.Code} {f.Message}"))}");
            Assert.Equal("units", report.Mode);

            // Every comparison section the units validator owns passed: closure inventory, field-by-field
            // records (provenance under the H5 allowlist), retained bytes, diagnostic sets, and
            // materialized templates.
            Assert.All(report.Sections, section => Assert.True(section.Passed, $"Section '{section.Section}' failed."));

            // Non-vacuous closure: the roster units, their child-override and prerequisite dependencies,
            // the shared armor and locomotors, and the implicit DefaultThingTemplate parent.
            var closureNames = expected.Resolution.Closure.Select(record => record.Id.Name).ToList();
            Assert.Subset(
                new HashSet<string>(closureNames, StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(H7SyntheticIniRoster.ClosureRecordNames, StringComparer.OrdinalIgnoreCase));

            // Record verdicts: one per closure record, all passed.
            Assert.Equal(expected.Resolution.Closure.Count, report.Records.Count);
            Assert.All(report.Records, record => Assert.True(record.Passed, $"Record '{record.Record}' verdict failed."));

            // Scoped AssetReference identity preserved: every reloaded id keeps a non-empty scope and
            // name, and the child override carries its own weapon in the same scope.
            var reader = new H4UnitPackageReader(packageDir);
            var reloaded = reader.Read(expected.Resolution.Roots.Select(root => root.Name).ToList());
            Assert.DoesNotContain(reloaded.Diagnostics, diagnostic => diagnostic.IsError);
            Assert.All(reloaded.Records, record =>
            {
                Assert.False(string.IsNullOrWhiteSpace(record.Id.Source.Id));
                Assert.False(string.IsNullOrWhiteSpace(record.Id.Name));
            });

            var vehicle = reloaded.Records.OfType<UnitRecord>().Single(record => record.Id.Name == H7SyntheticIniRoster.VehicleName);
            Assert.Equal(800, vehicle.BuildCost.Value.Value);
            Assert.Contains(vehicle.Weapons.Items, weapon => weapon.Name == H7SyntheticIniRoster.CannonName && weapon.Kind == ContentKind.Weapon);

            // Retained inventory: every closure record is legacy-INI-bound, so the package must carry
            // one exact retained INI block per record (byte equality is asserted by the validator).
            var retainedFiles = Directory.EnumerateFiles(Path.Combine(packageDir, "retained"), "*.ini", SearchOption.AllDirectories);
            Assert.Equal(expected.Resolution.Closure.Count, retainedFiles.Count());
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticRoster_FullKindClosure_UpgradeRecordRoundTripsThroughValidator()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var installRoot = H7SyntheticIniRoster.WriteTo(Path.Combine(tempRoot, "install"));

            // The convert-ini CLI takes unit-name roots only, and the Generals adapter never emits an
            // Upgrade-kind reference, so the upgrade record reaches the closure only through the same
            // pipeline's empty-roster stage (adapter.Read with no roots = every staged record).
            var expected = LoadExpectedResolution(installRoot, []);
            Assert.Subset(
                new HashSet<ContentKind> { ContentKind.Unit, ContentKind.Weapon, ContentKind.Armor, ContentKind.Locomotor, ContentKind.Upgrade },
                expected.Resolution.Closure.Select(record => record.Id.Kind).ToHashSet());

            using var fileSystem = new CompositeFileSystem(new DiskFileSystem(installRoot), new BigFileSystem(installRoot));
            var provider = new IniRetainedPayloadProvider(fileSystem);
            var packageDir = Path.Combine(tempRoot, "full-kind-package");

            var write = H4UnitPackageWriter.Write(packageDir, expected.Resolution, provider);
            Assert.True(write.Success, string.Join("; ", write.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));

            var report = RoundTripValidator.ValidateUnits(expected.Resolution, expected.Adapter.Natives, provider, packageDir);
            Assert.True(
                report.Passed,
                $"RoundTripValidator failed: {string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => $"{f.Code} {f.Message}"))}");

            var upgradeVerdict = Assert.Single(report.Records, record => record.Record == H7SyntheticIniRoster.UpgradeName);
            Assert.True(upgradeVerdict.Passed);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticRoster_MissingRecord_FailsRecordInventoryWithNamedDiagnostic()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (expected, provider, packageDir) = ConvertSyntheticRoster(tempRoot);

            UnitPackageTamper.DropRecord(packageDir, H7SyntheticIniRoster.RifleName);

            var report = RoundTripValidator.ValidateUnits(expected.Resolution, expected.Adapter.Natives, provider, packageDir);

            Assert.False(report.Passed);
            Assert.Contains(
                AllFindings(report),
                f => f.Code == MissingRecordCode
                    && f.FailureClass == RoundTripFailureClass.MissingRecord
                    && f.Message.Contains(H7SyntheticIniRoster.RifleName, StringComparison.Ordinal)
                    && f.Message.Contains("is missing from reloaded package", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticRoster_ExtraRecord_FailsRecordInventoryWithNamedDiagnostic()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (expected, provider, packageDir) = ConvertSyntheticRoster(tempRoot);

            UnitPackageTamper.DuplicateRecord(packageDir, H7SyntheticIniRoster.ArmorName);

            var report = RoundTripValidator.ValidateUnits(expected.Resolution, expected.Adapter.Natives, provider, packageDir);

            Assert.False(report.Passed);
            Assert.Contains(
                AllFindings(report),
                f => f.Code == ExtraRecordCode && f.FailureClass == RoundTripFailureClass.ExtraRecord);
            // The duplicated armor is named by the re-resolution dependency findings against the set.
            Assert.Contains(
                AllFindings(report),
                f => f.Message.Contains(H7SyntheticIniRoster.ArmorName, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticRoster_AssetReferenceScopeChange_FailsComparisonWithNamedField()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (expected, provider, packageDir) = ConvertSyntheticRoster(tempRoot);

            // Deliberate valid tamper: the scope attribute of the infantry's weapon reference moves to
            // a foreign scope, and the payload digest is recomputed so integrity still passes. The
            // comparison (not integrity) must catch the scope change.
            UnitPackageTamper.EditRecordPayload(packageDir, H7SyntheticIniRoster.InfantryName, payload =>
            {
                var weaponItem = payload["weapons"]!["items"]![0]!.AsObject();
                Assert.Equal(H7SyntheticIniRoster.RifleName, weaponItem["name"]!.GetValue<string>());
                weaponItem["source"] = "h7-tampered-scope";
            });

            var reader = new H4UnitPackageReader(packageDir);
            var reloaded = reader.Read(expected.Resolution.Roots.Select(root => root.Name).ToList());
            Assert.DoesNotContain(reloaded.Diagnostics, diagnostic => diagnostic.IsError);

            var infantry = reloaded.Records.OfType<UnitRecord>().Single(record => record.Id.Name == H7SyntheticIniRoster.InfantryName);
            var scoped = Assert.Single(infantry.Weapons.Items, weapon => weapon.Name == H7SyntheticIniRoster.RifleName);
            Assert.Equal("h7-tampered-scope", scoped.Source.Id);

            var report = RoundTripValidator.ValidateUnits(expected.Resolution, expected.Adapter.Natives, provider, packageDir);

            Assert.False(report.Passed);
            Assert.Contains(
                AllFindings(report),
                f => f.FailureClass == RoundTripFailureClass.TypedFieldMismatch
                    && f.Message.Contains("'Weapons'", StringComparison.Ordinal)
                    && f.Message.Contains("item mismatch", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticRoster_ValuedUnclassifiedField_RetainsValueThroughConverterWithNamedDiagnostic()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var packageDir = Path.Combine(tempRoot, "unclassified-package");
            var (resolution, unitName) = ResolveUnclassifiedScout(policyExcluded: true);

            var excludedWarning = Assert.Single(
                resolution.Diagnostics,
                diagnostic => diagnostic.Code == DiagnosticCodes.ExcludedByPolicy);
            Assert.Contains("FutureArmorPenetration", excludedWarning.Message, StringComparison.Ordinal);
            Assert.Contains("excluded by the import policy", excludedWarning.Message, StringComparison.Ordinal);

            var write = H4UnitPackageWriter.Write(packageDir, resolution);
            Assert.True(write.Success, string.Join("; ", write.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));

            var reader = new H4UnitPackageReader(packageDir);
            var reloaded = reader.Read([unitName]);
            Assert.DoesNotContain(reloaded.Diagnostics, diagnostic => diagnostic.IsError);

            var scout = Assert.Single(reloaded.Records.OfType<UnitRecord>());
            var field = Assert.Single(scout.Unclassified);
            Assert.Equal("FutureArmorPenetration", field.Name);
            Assert.True(field.AffectsGameplay);
            Assert.True(field.ExcludedByPolicy);
            Assert.Equal("42.5", field.RawValue);
            Assert.NotNull(field.Provenance);

            // The shared validator's field-by-field record comparison runs over the reloaded package:
            // no unclassified, record, closure, or retained finding may appear.
            var report = RoundTripValidator.ValidateUnits(resolution, null, new EmptyRetainedPayloadProvider(), packageDir);
            Assert.DoesNotContain(AllFindings(report), f => f.Section == "unclassified");
            Assert.DoesNotContain(AllFindings(report), f => f.Section is "records" or "closure" or "retained");

            // The validator's diagnostic-set gate compares resolution-stage diagnostics against
            // package read-stage diagnostics, so source-side warnings (the policy exclusion and the
            // visual-binding info) surface as count differences. Pinned verbatim: the value round
            // trip itself is verified above and by the section guards; a validator change that
            // closes this gap must turn this assertion into a fully passed report.
            var diagnosticFindings = AllFindings(report).Where(f => f.Section == "diagnostics").ToList();
            Assert.Contains(diagnosticFindings, f => f.Code == TypedMismatchCode && f.Message.Contains("H2A-EXCLUDED-BY-POLICY", StringComparison.Ordinal));
            Assert.Contains(diagnosticFindings, f => f.Code == TypedMismatchCode && f.Message.Contains("H2A-NO-VISUAL", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticRoster_UnexcludedValuedGameplayField_BlocksPublicationWithNamedDiagnostic()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var packageDir = Path.Combine(tempRoot, "blocked-package");
            var (resolution, _) = ResolveUnclassifiedScout(policyExcluded: false);

            Assert.False(resolution.Succeeded);
            var mechanic = Assert.Single(
                resolution.Diagnostics,
                diagnostic => diagnostic.Code == DiagnosticCodes.UnsupportedMechanic);
            Assert.Contains("FutureArmorPenetration", mechanic.Message, StringComparison.Ordinal);

            var write = H4UnitPackageWriter.Write(packageDir, resolution);
            Assert.False(write.Success);
            Assert.Contains(write.Diagnostics, diagnostic => diagnostic.Code == UnitPackageWriteDiagnosticCodes.WriteRefused);
            Assert.False(Directory.Exists(packageDir), "A refused conversion must not leave a package behind.");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticRoster_ChangedUnclassifiedValue_IsCaughtByComparerWithNamedField()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var packageDir = Path.Combine(tempRoot, "unclassified-package");
            var (resolution, unitName) = ResolveUnclassifiedScout(policyExcluded: true);
            Assert.True(H4UnitPackageWriter.Write(packageDir, resolution).Success);

            UnitPackageTamper.EditRecordPayload(packageDir, unitName, payload =>
            {
                var field = payload["unclassified"]![0]!.AsObject();
                Assert.Equal("FutureArmorPenetration", field["name"]!.GetValue<string>());
                field["rawValue"] = "99.0";
            });

            var report = RoundTripValidator.ValidateUnits(resolution, null, new EmptyRetainedPayloadProvider(), packageDir);

            Assert.False(report.Passed);
            var finding = Assert.Single(AllFindings(report), f => f.Section == "unclassified");
            Assert.Equal(TypedMismatchCode, finding.Code);
            Assert.Equal(RoundTripFailureClass.TypedFieldMismatch, finding.FailureClass);
            Assert.Contains("FutureArmorPenetration", finding.Message, StringComparison.Ordinal);
            Assert.Contains("Unclassified field", finding.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SyntheticRoster_NumericQuantityUnitString_IsRejectedWithoutSilentCoercion()
    {
        var tempRoot = NewTempRoot();

        try
        {
            var (expected, provider, packageDir) = ConvertSyntheticRoster(tempRoot);

            // Correction-note coverage (WI-H5 numeric enum fix): a numeric enum string for the
            // quantity unit must not be silently coerced back into the typed record; the round
            // trip must fail on the field rather than compare equal. The tamper rewrites the
            // authored wrapper into the flat quantity shape the reader also accepts, so the
            // rejection is exercised through StrictEnumParser instead of shape mismatch.
            UnitPackageTamper.EditRecordPayload(packageDir, H7SyntheticIniRoster.InfantryName, payload =>
            {
                Assert.Equal("HitPoints", payload["maxHealth"]!["value"]!["unit"]!.GetValue<string>());
                payload["maxHealth"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["value"] = 180.0,
                    ["unit"] = "1",
                };
            });

            var report = RoundTripValidator.ValidateUnits(expected.Resolution, expected.Adapter.Natives, provider, packageDir);

            Assert.False(report.Passed);
            Assert.Contains(AllFindings(report), f => f.Message.Contains("MaxHealth", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static (LoadedRoster Expected, IniRetainedPayloadProvider Provider, string PackageDir) ConvertSyntheticRoster(string tempRoot)
    {
        var installRoot = H7SyntheticIniRoster.WriteTo(Path.Combine(tempRoot, "install"));
        var packageDir = Path.Combine(tempRoot, "package");

        using (var standardOut = new StringWriter())
        using (var errorOut = new StringWriter())
        {
            var exitCode = H5MigrateCli.Run(
                ["convert-ini", "--game-path", installRoot, "--out", packageDir, .. H7SyntheticIniRoster.RosterRoots],
                standardOut,
                errorOut,
                CancellationToken.None);

            Assert.True(exitCode == 0, $"convert-ini failed (exit {exitCode}): {errorOut}");
        }

        var expected = LoadExpectedResolution(installRoot, H7SyntheticIniRoster.RosterRoots);
        var provider = new IniRetainedPayloadProvider(new DiskFileSystem(installRoot));
        return (expected, provider, packageDir);
    }

    private static LoadedRoster LoadExpectedResolution(string installRoot, IReadOnlyList<string> rosterRoots)
    {
        using var fileSystem = new CompositeFileSystem(new DiskFileSystem(installRoot), new BigFileSystem(installRoot));
        var staging = GeneralsIniLoader.Load(fileSystem);
        var adapter = new GeneralsUnitAdapter(staging, SageGame.CncGenerals);

        // Mirror H5MigrateCli.ResolveRosterUnits: the pipeline prepends DefaultThingTemplate to any
        // explicit unit-name roster because Generals objects inherit its default modules. An empty
        // roster keeps every staged record as a root (the pipeline's full-kind closure).
        var roots = new List<string>();
        if (rosterRoots.Count > 0)
        {
            roots.Add("DefaultThingTemplate");
            roots.AddRange(rosterRoots);
        }

        var set = adapter.Read(roots);
        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());
        Assert.True(
            resolution.Succeeded,
            string.Join("; ", resolution.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message)));
        return new LoadedRoster(adapter, resolution);
    }

    /// <summary>
    /// Drives a synthetic valued-unclassified scout through the converter's resolve and write
    /// stages. The Generals INI adapter itself never produces unclassified fields (exotic INI
    /// fields are carried by the native template and the retained block bytes), so the source set
    /// is synthetic; resolver, writer, reader, and validator are the production pipeline stages.
    /// </summary>
    private static (UnitImportResolution Resolution, string UnitName) ResolveUnclassifiedScout(bool policyExcluded)
    {
        var source = new ContentSource("h7-unclassified", caseSensitiveNames: false);
        var provenance = new Provenance(source.Id, "synthetic/scout.ini", 12);
        var unitId = new ContentId(source, ContentKind.Unit, "SyntheticScout");

        var unit = new UnitRecord(unitId, provenance)
        {
            DisplayName = Authored.Of("Synthetic Scout"),
            MaxHealth = Authored.Of(new Quantity(120, QuantityUnit.HitPoints)),
            InitialHealth = Authored.Of(new Quantity(120, QuantityUnit.HitPoints)),
            Weapons = ListOverride<ContentId>.Replace([new ContentId(source, ContentKind.Weapon, "ScoutRifle")]),
            Armor = Authored.Of(new ContentId(source, ContentKind.Armor, "ScoutArmor")),
            Locomotors = ListOverride<ContentId>.Replace([new ContentId(source, ContentKind.Locomotor, "ScoutFeet")]),
            Modules = [ModuleEdit.Add(new UnitModule("ModuleTag_Body", "ActiveBody", ModuleKinds.Body))],
            Unclassified =
            [
                new UnclassifiedField(
                    "FutureArmorPenetration",
                    AffectsGameplay: true,
                    provenance,
                    ExcludedByPolicy: policyExcluded,
                    RawValue: "42.5"),
            ],
        };

        var weapon = new WeaponRecord(new ContentId(source, ContentKind.Weapon, "ScoutRifle"), provenance)
        {
            PrimaryDamage = Authored.Of(new Quantity(10, QuantityUnit.HitPoints)),
            AttackRange = Authored.Of(new Quantity(120, QuantityUnit.GameDistance)),
            ReloadTime = Authored.Of(new Quantity(30, QuantityUnit.LogicFrames)),
        };

        var armor = new ArmorRecord(new ContentId(source, ContentKind.Armor, "ScoutArmor"), provenance)
        {
            Multipliers = new Dictionary<string, Authored<double>>(StringComparer.OrdinalIgnoreCase)
            {
                ["SMALL_ARMS"] = Authored.Of(0.5),
            },
        };

        var locomotor = new LocomotorRecord(new ContentId(source, ContentKind.Locomotor, "ScoutFeet"), provenance)
        {
            Speed = Authored.Of(new Quantity(30, QuantityUnit.DistancePerSecond)),
        };

        var set = new UnitContentSet(source, [unit, weapon, armor, locomotor], [unitId]);
        return (UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry()), unitId.Name);
    }

    private static IReadOnlyList<RoundTripFinding> AllFindings(RoundTripReport report)
    {
        return report.Sections.SelectMany(section => section.Findings).ToList();
    }

    private static string NewTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonH7Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private sealed record LoadedRoster(GeneralsUnitAdapter Adapter, UnitImportResolution Resolution);

    /// <summary>Retained provider for legacy-free closures; any request is a suite bug.</summary>
    private sealed class EmptyRetainedPayloadProvider : IUnitPackageRetainedPayloadProvider
    {
        public UnitPackageRetainedBlock? GetBlock(UnitRetainedBlockRequest request)
        {
            throw new InvalidOperationException($"No synthetic record needs retention, but '{request.RecordId}' requested a block.");
        }
    }
}
