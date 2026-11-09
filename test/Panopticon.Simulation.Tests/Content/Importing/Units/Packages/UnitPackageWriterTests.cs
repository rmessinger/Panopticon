#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

public sealed class UnitPackageWriterTests : IDisposable
{
    private static readonly string RetainedIniContent =
        """
        Armor InfantryArmor
          Armor = DEFAULT 100%
          Armor = SMALL_ARMS 50%
        End

        Locomotor BasicHumanLocomotor
          Surfaces = GROUND RUBBLE
          Speed = 30
          TurnRate = 180
          Appearance = TWO_LEGS
        End

        Weapon RangerRifle
          PrimaryDamage = 5.0
          DamageType = SMALL_ARMS
          AttackRange = 100.0
          DelayBetweenShots = 500
        End

        Object Ranger
          BuildCost = 225
          BuildTime = 5.0
          VisionRange = 150
          KindOf = INFANTRY SELECTABLE
          ArmorSet
            Conditions = None
            Armor = InfantryArmor
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY RangerRifle
          End
          Body = ActiveBody ModuleTag_Body
            MaxHealth = 180.0
            InitialHealth = 180.0
          End
          Behavior = AIUpdateInterface ModuleTag_AI
          End
          Locomotor = SET_NORMAL BasicHumanLocomotor
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryHeight = 12.0
        End
        """;

    private readonly string _rootDir;

    public UnitPackageWriterTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "unit_pkg_writer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootDir, recursive: true);
        }
        catch
        {
            // Ignore temp dir cleanup errors
        }
    }

    [Fact]
    public void WriteThenReadRoundTripsResolvedClosureFieldByField()
    {
        var (resolution, provider) = ResolvedRangerClosure();

        var target = Path.Combine(_rootDir, "package");
        var result = H4UnitPackageWriter.Write(target, resolution, provider);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(target, result.PackageRoot);

        var reader = new H4UnitPackageReader(target);
        var set = reader.Read([]);

        UnitPackageRoundTripAssert.ClosuresRoundTrip(resolution, set, reader.Natives);

        // Retained blocks re-issue native templates from the reader's own rebuilt table.
        var ranger = set.Records.OfType<UnitRecord>().Single(record => record.Id.Name == "Ranger");
        var rangerNative = Assert.IsType<Panopticon.Logic.Object.ObjectDefinition>(reader.Natives!.Get(ranger.Legacy!.Handle));
        Assert.Equal("Ranger", rangerNative.Name);
        Assert.Equal(225, rangerNative.BuildCost);
    }

    [Fact]
    public void WriteThenReadRoundTripsListEditModesAndModuleOperations()
    {
        var source = ContentSource.Generals;
        ContentId Id(ContentKind kind, string name) => new(source, kind, name);

        var unit = new UnitRecord(Id(ContentKind.Unit, "EditModeUnit"), new Provenance("generals", "Data/INI/Edit.ini", 5))
        {
            KindOf = ListOverride<string>.Edit(["AIR"], ["INFANTRY"]),
            Weapons = ListOverride<ContentId>.Replace([Id(ContentKind.Weapon, "MachineGun")]),
            Prerequisites = ListOverride<ContentId>.Inherit,
            Modules =
            [
                ModuleEdit.Replace("ModuleTag_Body", new UnitModule(
                    "ModuleTag_Body",
                    "ActiveBody",
                    Panopticon.Logic.Object.ModuleKinds.Body | Panopticon.Logic.Object.ModuleKinds.Update,
                    Panopticon.Logic.Object.ModuleInheritanceMode.AlwaysInherit,
                    ExclusiveGroup: "AI_GROUP")),
                ModuleEdit.Remove("ModuleTag_Old"),
                ModuleEdit.Add(new UnitModule(
                    "ModuleTag_Ability",
                    "SpecialPower",
                    Panopticon.Logic.Object.ModuleKinds.SpecialPower,
                    Inherited: true,
                    Ability: Id(ContentKind.Ability, "FlashbangAttack"))),
            ],
            Unclassified =
            [
                new UnclassifiedField("BonusField", false, new Provenance("generals", "Data/INI/Edit.ini", 6), false, "edit-value", ["edit block"]),
            ],
        };

        var resolution = new UnitImportResolution(source, [unit.Id], [unit], []);
        var target = Path.Combine(_rootDir, "edit-package");

        var result = H4UnitPackageWriter.Write(target, resolution);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var reader = new H4UnitPackageReader(target);
        var set = reader.Read([]);

        UnitPackageRoundTripAssert.ClosuresRoundTrip(resolution, set, reader.Natives);

        var reread = Assert.IsType<UnitRecord>(set.Records.Single());
        Assert.Equal(ListOverrideMode.Edit, reread.KindOf.Mode);
        Assert.Equal(["AIR"], reread.KindOf.Items);
        Assert.Equal(["INFANTRY"], reread.KindOf.Removals);
        Assert.Equal(ListOverrideMode.Inherit, reread.Prerequisites.Mode);
        Assert.Equal(3, reread.Modules.Count);
        Assert.Equal(ModuleEditOperation.Replace, reread.Modules[0].Operation);
        Assert.Equal(Panopticon.Logic.Object.ModuleKinds.Body | Panopticon.Logic.Object.ModuleKinds.Update, reread.Modules[0].Module!.Kinds);
        Assert.Equal(ModuleEditOperation.Remove, reread.Modules[1].Operation);
        Assert.Null(reread.Modules[1].Module);
        Assert.True(reread.Modules[2].Module!.Inherited);
        Assert.Equal("FlashbangAttack", reread.Modules[2].Module!.Ability!.Value.Name);
    }

    [Fact]
    public void WriteRefusesClosureCarryingErrorDiagnosticsWithoutCreatingTarget()
    {
        var source = ContentSource.Generals;
        var unit = new UnitRecord(new ContentId(source, ContentKind.Unit, "BrokenUnit"), new Provenance("generals", "Data/INI/Broken.ini", 1));
        var resolution = new UnitImportResolution(
            source,
            [unit.Id],
            [unit],
            [new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportStage.Validate, DiagnosticCodes.UnsupportedMechanic, "unsupported mechanic", unit.Id)]);

        var target = Path.Combine(_rootDir, "refused");

        var result = H4UnitPackageWriter.Write(target, resolution);

        Assert.False(result.Success);
        var refusal = Assert.Single(result.Diagnostics);
        Assert.Equal(UnitPackageWriteDiagnosticCodes.WriteRefused, refusal.Code);
        Assert.Contains("refusing", refusal.Message);
        Assert.Contains(DiagnosticCodes.UnsupportedMechanic, refusal.Message);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void WriteRefusesBindingsWithoutProviderAndProviderWithoutBlock()
    {
        var source = ContentSource.Generals;
        var bound = new WeaponRecord(new ContentId(source, ContentKind.Weapon, "BoundWeapon"), new Provenance("generals", "Data/INI/Bound.ini", 2))
        {
            Legacy = new LegacyBinding(source, 7),
            PrimaryDamage = Authored.Of(new Quantity(5, QuantityUnit.HitPoints)),
        };
        var resolution = new UnitImportResolution(source, [bound.Id], [bound], []);

        var withoutProvider = H4UnitPackageWriter.Write(Path.Combine(_rootDir, "no-provider"), resolution);
        Assert.False(withoutProvider.Success);
        Assert.Contains(withoutProvider.Diagnostics, diagnostic => diagnostic.Code == UnitPackageWriteDiagnosticCodes.RetainedBlockMissing);

        var nullProvider = H4UnitPackageWriter.Write(
            Path.Combine(_rootDir, "null-block"),
            resolution,
            new DelegatingProvider(_ => null));
        Assert.False(nullProvider.Success);
        var missing = nullProvider.Diagnostics.Single();
        Assert.Equal(UnitPackageWriteDiagnosticCodes.RetainedBlockMissing, missing.Code);
        Assert.Contains("BoundWeapon", missing.Message);
        Assert.False(Directory.Exists(Path.Combine(_rootDir, "no-provider")));
        Assert.False(Directory.Exists(Path.Combine(_rootDir, "null-block")));
    }

    [Fact]
    public void WriteRefusesUnsupportedRetainedDialect()
    {
        var source = ContentSource.Generals;
        var bound = new LocomotorRecord(new ContentId(source, ContentKind.Locomotor, "BoundLocomotor"), new Provenance("generals", "Data/INI/Loco.ini", 3))
        {
            Legacy = new LegacyBinding(source, 2),
            Speed = Authored.Of(new Quantity(30, QuantityUnit.DistancePerSecond)),
        };
        var resolution = new UnitImportResolution(source, [bound.Id], [bound], []);

        var result = H4UnitPackageWriter.Write(
            Path.Combine(_rootDir, "bad-dialect"),
            resolution,
            new DelegatingProvider(_ => new UnitPackageRetainedBlock("ee2-ddf", 1, "Locomotor BoundLocomotor\nEnd"u8.ToArray())));

        Assert.False(result.Success);
        var refusal = Assert.Single(result.Diagnostics);
        Assert.Equal(UnitPackageWriteDiagnosticCodes.RetainedDialectUnsupported, refusal.Code);
        Assert.False(Directory.Exists(Path.Combine(_rootDir, "bad-dialect")));
    }

    [Fact]
    public void AtomicOverwriteSecondWriteWinsWithoutResidue()
    {
        var (firstResolution, firstProvider) = ResolvedRangerClosure();

        var source = ContentSource.Generals;
        var secondUnit = new UnitRecord(new ContentId(source, ContentKind.Unit, "SecondUnit"), new Provenance("generals", "Data/INI/Second.ini", 4))
        {
            DisplayName = Authored.Of("Second"),
            MaxHealth = Authored.Of(new Quantity(42, QuantityUnit.HitPoints)),
        };
        var secondResolution = new UnitImportResolution(source, [secondUnit.Id], [secondUnit], []);

        var target = Path.Combine(_rootDir, "overwrite");

        Assert.True(H4UnitPackageWriter.Write(target, firstResolution, firstProvider).Success);
        Assert.True(H4UnitPackageWriter.Write(target, secondResolution, options: new UnitPackageWriteOptions(new UnitPackageProvenance("generals", "resolved-closure"))).Success);

        var reader = new H4UnitPackageReader(target);
        var set = reader.Read([]);

        Assert.DoesNotContain(set.Diagnostics, diagnostic => diagnostic.IsError);
        var unit = Assert.Single(set.Records);
        Assert.Equal("SecondUnit", unit.Id.Name);
        Assert.Equal("SecondUnit", Assert.Single(set.Roots).Name);

        // The caller-supplied package-level provenance survives in the manifest.
        var (manifest, manifestDiagnostics) = UnitPackageManifestReader.ParseAndValidate(
            File.ReadAllText(Path.Combine(target, UnitPackageFormat.ManifestFileName)));
        Assert.NotNull(manifest);
        Assert.Empty(manifestDiagnostics);
        Assert.NotNull(manifest.Provenance);
        Assert.Equal("resolved-closure", manifest.Provenance.FilePath);

        // The first write's payload files are gone; only the second package's files remain.
        Assert.False(File.Exists(Path.Combine(target, "records", "units", "Ranger.json")));

        Assert.Empty(Directory.EnumerateDirectories(_rootDir, "*.staging-*"));
        Assert.Empty(Directory.EnumerateDirectories(_rootDir, "*.backup-*"));
    }

    [Fact]
    public void ProviderFailureMidWriteLeavesOriginalPackageIntactWithoutResidue()
    {
        var (resolution, provider) = ResolvedRangerClosure();
        var target = Path.Combine(_rootDir, "guarded");

        Assert.True(H4UnitPackageWriter.Write(target, resolution, provider).Success);

        var requestsBeforeThrow = 2;
        var callCount = 0;
        var throwingProvider = new DelegatingProvider(request =>
        {
            callCount++;

            if (callCount > requestsBeforeThrow)
            {
                throw new InvalidOperationException("provider exploded mid-write");
            }

            return new UnitPackageRetainedBlock(
                UnitPackageFormat.SupportedGeneralsIniDialect,
                UnitPackageFormat.SupportedGeneralsIniDialectVersion,
                System.Text.Encoding.UTF8.GetBytes(RetainedIniContent),
                new UnitPackageProvenance("generals", "Data/INI/Test.ini", 1));
        });

        var result = H4UnitPackageWriter.Write(target, resolution, throwingProvider);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == UnitPackageWriteDiagnosticCodes.PayloadProviderFailed);
        Assert.Contains("provider exploded mid-write", result.Diagnostics.Single(diagnostic => diagnostic.Code == UnitPackageWriteDiagnosticCodes.PayloadProviderFailed).Message);

        // The previous package is intact and readable.
        var reader = new H4UnitPackageReader(target);
        var set = reader.Read([]);
        Assert.DoesNotContain(set.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(10, set.Records.Count);

        // No staging or backup residue remains next to the target; the target itself stays.
        var residue = Directory.GetFileSystemEntries(_rootDir)
            .Select(Path.GetFileName)
            .Where(name => name!.StartsWith("guarded.", StringComparison.Ordinal));
        Assert.Empty(residue);
    }

    internal static (UnitImportResolution Resolution, DelegatingProvider Provider) ResolvedRangerClosure()
    {
        var source = ContentSource.Generals;

        ContentId Id(ContentKind kind, string name) => new(source, kind, name);

        var parent = new UnitRecord(Id(ContentKind.Unit, "BaseInfantry"), new Provenance("generals", "Data/INI/Base.ini", 10))
        {
            DisplayName = Authored.Of("Base Infantry"),
            MaxHealth = Authored.Of(new Quantity(100, QuantityUnit.HitPoints)),
            VisionRange = Authored.Of(new Quantity(120, QuantityUnit.GameDistance)),
            KindOf = ListOverride<string>.Replace(["INFANTRY"]),
            Weapons = ListOverride<ContentId>.Replace([Id(ContentKind.Weapon, "MachineGun")]),
            Unclassified =
            [
                new UnclassifiedField(
                    "BonusField",
                    AffectsGameplay: false,
                    new Provenance("generals", "Data/INI/Base.ini", 20),
                    ExcludedByPolicy: false,
                    "parent-value",
                    ["parent block line"]),
            ],
        };

        var child = new UnitRecord(Id(ContentKind.Unit, "Ranger"), new Provenance("generals", "Data/INI/Ranger.ini", 30))
        {
            Parent = Id(ContentKind.Unit, "BaseInfantry"),
            Legacy = new LegacyBinding(source, 15),
            DisplayName = Authored.Of("Ranger"),
            MaxHealth = Authored.Of(new Quantity(180, QuantityUnit.HitPoints)),
            Weapons = ListOverride<ContentId>.Edit(
                [Id(ContentKind.Weapon, "RangerRifle")],
                [Id(ContentKind.Weapon, "MachineGun")]),
            Armor = Authored.Of(Id(ContentKind.Armor, "InfantryArmor")),
            BuildCost = Authored.Of(new Quantity(225, QuantityUnit.Currency)),
            Locomotors = ListOverride<ContentId>.Replace([Id(ContentKind.Locomotor, "BasicHumanLocomotor")]),
            Upgrades = ListOverride<ContentId>.Replace([Id(ContentKind.Upgrade, "RangerFlashbang")]),
            Visual = Authored.Of(Id(ContentKind.VisualState, "RangerVisual")),
            Modules =
            [
                ModuleEdit.Add(new UnitModule(
                    "ModuleTag_Body",
                    "ActiveBody",
                    Panopticon.Logic.Object.ModuleKinds.Body,
                    Legacy: new LegacyBinding(source, 10))),
                ModuleEdit.Add(new UnitModule(
                    "ModuleTag_AI",
                    "AIUpdateInterface",
                    Panopticon.Logic.Object.ModuleKinds.Update,
                    Legacy: new LegacyBinding(source, 11))),
                ModuleEdit.Add(new UnitModule(
                    "ModuleTag_Death",
                    "Die",
                    Panopticon.Logic.Object.ModuleKinds.Die)),
            ],
            Unclassified =
            [
                new UnclassifiedField(
                    "BonusField",
                    AffectsGameplay: false,
                    new Provenance("generals", "Data/INI/Ranger.ini", 40),
                    ExcludedByPolicy: false,
                    "child-value"),
                new UnclassifiedField(
                    "ChildOnly",
                    AffectsGameplay: false,
                    new Provenance("generals", "Data/INI/Ranger.ini", 41),
                    ExcludedByPolicy: false,
                    "child-only",
                    ["child line 1", "child line 2"]),
            ],
        };

        var machineGun = new WeaponRecord(Id(ContentKind.Weapon, "MachineGun"), new Provenance("generals", "Data/INI/Base.ini", 50))
        {
            PrimaryDamage = Authored.Of(new Quantity(8, QuantityUnit.HitPoints)),
            DamageType = Authored.Of("SMALL_ARMS"),
            AttackRange = Authored.Of(new Quantity(80, QuantityUnit.GameDistance)),
            ReloadTime = Authored.Of(new Quantity(400, QuantityUnit.Milliseconds)),
            ClipSize = Authored.Of(0),
        };

        var rangerRifle = new WeaponRecord(Id(ContentKind.Weapon, "RangerRifle"), new Provenance("generals", "Data/INI/Ranger.ini", 60))
        {
            Legacy = new LegacyBinding(source, 12),
            PrimaryDamage = Authored.Of(new Quantity(5, QuantityUnit.HitPoints)),
            DamageType = Authored.Of("SMALL_ARMS"),
            AttackRange = Authored.Of(new Quantity(100, QuantityUnit.GameDistance)),
            ReloadTime = Authored.Of(new Quantity(500, QuantityUnit.Milliseconds)),
            ClipReloadTime = Authored.Of(new Quantity(0, QuantityUnit.LogicFrames)),
            Projectile = Authored.Of(Id(ContentKind.Projectile, "BulletProjectile")),
        };

        var armor = new ArmorRecord(Id(ContentKind.Armor, "InfantryArmor"), new Provenance("generals", "Data/INI/Ranger.ini", 70))
        {
            Legacy = new LegacyBinding(source, 13),
            Multipliers = new Dictionary<string, Authored<double>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DEFAULT"] = Authored.Of(1.0),
                ["SMALL_ARMS"] = Authored.Of(0.5),
            },
            DefaultMultiplier = Authored.Of(1.0),
        };

        var locomotor = new LocomotorRecord(Id(ContentKind.Locomotor, "BasicHumanLocomotor"), new Provenance("generals", "Data/INI/Ranger.ini", 80))
        {
            Legacy = new LegacyBinding(source, 14),
            Speed = Authored.Of(new Quantity(30, QuantityUnit.DistancePerSecond)),
            TurnRate = Authored.Of(new Quantity(180, QuantityUnit.DegreesPerSecond)),
            Acceleration = Authored.Of(new Quantity(60, QuantityUnit.DistancePerSecondSquared)),
            Braking = Authored.Of(new Quantity(60, QuantityUnit.DistancePerSecondSquared)),
            PreferredHeight = Authored.Of(new Quantity(0, QuantityUnit.Meters)),
            CanHover = Authored.Of(false),
            Appearance = Authored.Of("TWO_LEGS"),
            Surfaces = ListOverride<string>.Replace(["GROUND", "RUBBLE"]),
        };

        var upgrade = new UpgradeRecord(Id(ContentKind.Upgrade, "RangerFlashbang"), new Provenance("generals", "Data/INI/Ranger.ini", 90))
        {
            DisplayName = Authored.Of("Flashbang Grenades"),
            BuildCost = Authored.Of(new Quantity(500, QuantityUnit.Currency)),
            BuildTime = Authored.Of(new Quantity(30, QuantityUnit.Seconds)),
            Scope = Authored.Of("Player"),
            Prerequisites = ListOverride<ContentId>.Replace([]),
        };

        var ability = new AbilityRecord(Id(ContentKind.Ability, "FlashbangAttack"), new Provenance("generals", "Data/INI/Ranger.ini", 100))
        {
            Family = Authored.Of("SpecialPower"),
            Range = Authored.Of(new Quantity(120, QuantityUnit.GameDistance)),
            Cooldown = Authored.Of(new Quantity(15, QuantityUnit.Seconds)),
            Weapon = Authored.Of(Id(ContentKind.Weapon, "RangerRifle")),
            RequiredUpgrade = Authored.Of(Id(ContentKind.Upgrade, "RangerFlashbang")),
        };

        var projectile = new ProjectileRecord(Id(ContentKind.Projectile, "BulletProjectile"), new Provenance("generals", "Data/INI/Ranger.ini", 110))
        {
            Speed = Authored.Of(new Quantity(300, QuantityUnit.SourceDistancePerSecond)),
            Visual = Authored.Of(Id(ContentKind.VisualState, "RangerVisual")),
        };

        var visual = new VisualStateRecord(Id(ContentKind.VisualState, "RangerVisual"), new Provenance("generals", "Data/INI/Ranger.ini", 120))
        {
            ModelKey = Authored.Of("Data/W3D/AIRngr.w3d"),
            StateAnimations = new Dictionary<string, Authored<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Idle"] = Authored.Of("AIRngr_IDLA"),
                ["Move"] = Authored.Of("AIRngr_WKA"),
                ["Attack"] = Authored.Of("AIRngr_ATA"),
                ["Death"] = Authored.Of("AIRngr_DTA"),
            },
        };

        var records = new List<SourceRecord> { parent, child, machineGun, rangerRifle, armor, locomotor, upgrade, ability, projectile, visual };
        var roots = new List<ContentId> { Id(ContentKind.Unit, "Ranger"), Id(ContentKind.Unit, "BaseInfantry") };

        // Resolve through the real inheritance resolver so origins are stamped and parents are
        // cleared exactly as production closures carry them.
        var inheritance = new UnitInheritanceResolver().Resolve(new UnitContentSet(source, records, roots));
        Assert.Empty(inheritance.Diagnostics);

        var resolution = new UnitImportResolution(source, roots, inheritance.Resolved.Values.ToArray(), inheritance.Diagnostics);

        var provider = new DelegatingProvider(_ => new UnitPackageRetainedBlock(
            UnitPackageFormat.SupportedGeneralsIniDialect,
            UnitPackageFormat.SupportedGeneralsIniDialectVersion,
            System.Text.Encoding.UTF8.GetBytes(RetainedIniContent),
            new UnitPackageProvenance("generals", "Data/INI/Test.ini", 1)));

        return (resolution, provider);
    }

    internal sealed class DelegatingProvider(Func<UnitRetainedBlockRequest, UnitPackageRetainedBlock?> provider) : IUnitPackageRetainedPayloadProvider
    {
        public UnitPackageRetainedBlock? GetBlock(UnitRetainedBlockRequest request)
        {
            return provider(request);
        }
    }
}
