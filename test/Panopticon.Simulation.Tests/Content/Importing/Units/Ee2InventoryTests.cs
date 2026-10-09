using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.IO;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-M2-01 against the operator install: every db/Units DDF parses, the surveyed record
/// census matches, UnitType/UnitModel inheritance resolves (or reports precisely), the reference
/// roster rows exist with their stock values, and the patch archive layer is reported without
/// being composed. Requires EMPIRE_EARTH_II_PATH; no EE2 asset is committed.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2InventoryTests(ITestOutputHelper output)
{
    public static readonly string[] ReferenceRowKeys =
    [
        "HeavyInfantry1UpgradeEpoch15",
        "LightMounted1UpgradeEpoch15",
        "HeavyMounted1UpgradeEpoch15",
        "HeavyArtilleryUpgradeEpoch14",
        "FighterUpgradeEpoch15",
        "BomberUpgradeEpoch15",
        "LightMounted2UpgradeEpoch13",
    ];

    private static string InstallRoot()
    {
        return Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH")
            ?? throw new InvalidOperationException("Interactive test requires EMPIRE_EARTH_II_PATH pointing at an Empire Earth II install.");
    }

    internal static Ee2SourceFiles OpenInstall()
    {
        var root = InstallRoot();
        var patches = Directory.EnumerateFiles(Path.Combine(root, "zips"), "db_*.zip")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return Ee2SourceFiles.FromDatabase(
            new ZipFileSystem(Path.Combine(root, "zips/db.zip")),
            new ZipFileSystem(Path.Combine(root, "zips/graphics.zip")),
            patches);
    }

    private static (int UnitTypes, int UnitModels, int UpgradeAbilities, int UpgradeRefSets, int Other) Census(Ee2SourceFiles files, Func<string, bool>? filter = null)
    {
        var counts = (UnitTypes: 0, UnitModels: 0, UpgradeAbilities: 0, UpgradeRefSets: 0, Other: 0);
        foreach (var path in files.DdfPaths.Where(path => filter?.Invoke(path) != false))
        {
            var entry = files.Database.GetFile(path);
            using var reader = new StreamReader(entry.Open());
            foreach (var record in DdfReader.Read(reader.ReadToEnd(), path))
            {
                switch (record.Type)
                {
                    case "UnitType":
                        counts.UnitTypes++;
                        break;
                    case "UnitModel":
                        counts.UnitModels++;
                        break;
                    case "UpgradeAbilities":
                        counts.UpgradeAbilities++;
                        break;
                    case "UpgradeRefSet":
                        counts.UpgradeRefSets++;
                        break;
                    default:
                        counts.Other++;
                        break;
                }
            }
        }

        return counts;
    }

    [Fact]
    public void EveryUnitDdfParsesAndTheCensusMatchesTheSurvey()
    {
        var files = OpenInstall();

        Assert.Equal(117, files.DdfPaths.Count);
        Assert.Contains("db/Units/tankbarrier.ddf", files.DdfPaths);
        Assert.Contains("db/Units/test_building.ddf", files.DdfPaths);
        Assert.Contains("db/Units/test_human.ddf", files.DdfPaths);

        // Full-archive census (all 117 DDFs, including the two test files the game's own
        // dbunitfiles.cfg does not load).
        var counts = Census(files);
        Assert.Equal((438, 618, 184, 50), (counts.UnitTypes, counts.UnitModels, counts.UpgradeAbilities, counts.UpgradeRefSets));
        output.WriteLine($"archive census: {counts}");

        // Census scoped to the 115 db/Units files dbunitfiles.cfg actually loads. Directly
        // measured: 434 UnitTypes and 616 UnitModels (the read-only survey reported 432/615;
        // this assertion is the installed truth, and the two test DDFs the cfg skips hold the
        // remaining 4 UnitTypes and 2 UnitModels).
        var cfg = ReadUnitFileConfig(files);
        Assert.Equal(115, cfg.Count);
        var cfgScoped = Census(files, path => cfg.Contains(Normalize(path)));
        Assert.Equal((434, 616), (cfgScoped.UnitTypes, cfgScoped.UnitModels));
        output.WriteLine($"cfg census: {cfgScoped}");
    }

    private static HashSet<string> ReadUnitFileConfig(Ee2SourceFiles files)
    {
        var entry = files.Database.GetFile("db/dbunitfiles.cfg");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd()
            .Split('\n')
            .Select(line => line.Trim().Trim('"'))
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            .Where(line => line.StartsWith("db/units/", StringComparison.OrdinalIgnoreCase))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) => path.Trim().Replace('\\', '/').ToLowerInvariant();

    [Fact]
    public void FullInventoryReadsAndInheritanceResolves()
    {
        var set = new Ee2UnitAdapter(OpenInstall(), new Ee2ImportPolicy(15, "we", ["rps"])).ReadRows([]);

        Assert.DoesNotContain(set.Diagnostics, d => d.IsError && d.Code == DiagnosticCodes.UnsupportedMechanic);

        var resolution = UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault());
        var unresolved = resolution.Diagnostics
            .Where(d => d.Code is DiagnosticCodes.MissingParent or DiagnosticCodes.InheritanceCycle or DiagnosticCodes.ParentKindMismatch)
            .ToList();
        foreach (var diagnostic in unresolved)
        {
            output.WriteLine(diagnostic.ToString());
        }

        // Inventory findings are allowed but must name their source; none may be a hard error,
        // because no root was requested.
        Assert.All(unresolved, d => Assert.False(d.IsError));
        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));
    }

    [Fact]
    public void PatchArchivesAreReportedWithoutBeingComposed()
    {
        var set = new Ee2UnitAdapter(OpenInstall(), new Ee2ImportPolicy(15, "we", ["rps"])).ReadRows([]);

        var layer = Assert.Single(set.Diagnostics, d => d.Code == DiagnosticCodes.ArchiveLayer);
        Assert.Contains("stock", layer.Message);
        Assert.Contains("db_155.zip", layer.Message);
    }

    [Fact]
    public void UnitTableCensusMatchesTheSurvey()
    {
        var files = OpenInstall();
        var entry = files.Database.GetFile(files.UnitTablePath);
        using var reader = new StreamReader(entry.Open());
        var table = Ee2CsvTable.Read(reader.ReadToEnd(), files.UnitTablePath);

        Assert.Equal(396, RowCount(files));
        Assert.Equal(34, table.Columns.Count);
        Assert.Equal(2, table.Columns.Count(c => c.Equals("DISPLAYNAME", StringComparison.OrdinalIgnoreCase)));

        foreach (var rowKey in ReferenceRowKeys)
        {
            Assert.True(table.TryGetRow(rowKey, out _, out _), $"missing reference row {rowKey}");
        }
    }

    private static int RowCount(Ee2SourceFiles files)
    {
        var entry = files.Database.GetFile(files.UnitTablePath);
        using var reader = new StreamReader(entry.Open());
        var text = reader.ReadToEnd();
        return text.Split('\n').Count(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal));
    }

    [Fact]
    public void ReferenceRosterRowsComposeWithStockValuesAndOverrides()
    {
        var exclusions = ReferenceExclusions;
        var set = new Ee2UnitAdapter(OpenInstall(), new Ee2ImportPolicy(15, "we", exclusions)).ReadRows(ReferenceRowKeys);

        foreach (var rowKey in ReferenceRowKeys)
        {
            var root = new ContentId(ContentSource.Ee2, ContentKind.Unit, rowKey);
            var unit = set.Records.OfType<UnitRecord>().Single(r => r.Id.Equals(root));
            var weapon = set.Records.OfType<WeaponRecord>().Single(w => w.Id.Name == $"{rowKey}.Attack");
            var overrideInfo = set.Diagnostics.Single(d => d.Code == DiagnosticCodes.AbilityOverride && d.Message.Contains(rowKey));
            output.WriteLine($"{rowKey}: parent={unit.Parent?.Name} hp={unit.MaxHealth.Value.Value} los={unit.VisionRange.Value.Value} " +
                $"dmg={weapon.PrimaryDamage.Value.Value} rng={weapon.AttackRange.Value.Value} rld={weapon.ReloadTime.Value.Value} " +
                $"missile={weapon.Projectile.IsPresent} clip={weapon.ClipSize.GetValueOrDefault(0)} | {overrideInfo.Message}");
        }

        // Stock values asserted directly against the surveyed install values.
        AssertRow(set, "HeavyInfantry1UpgradeEpoch15", "HeavyInfantry1", 185, 10, 11, 9, 1.5);
        AssertRow(set, "LightMounted1UpgradeEpoch15", "Tank", 300, 11, 30, 10, 3.5);
        AssertRow(set, "HeavyMounted1UpgradeEpoch15", "HeavyTank", 350, 11, 25, 10, 3);
        AssertRow(set, "HeavyArtilleryUpgradeEpoch14", "HeavyArtillery", 225, 10, 150, 15, 4);
        AssertRow(set, "FighterUpgradeEpoch15", "Fighter", 500, 9, 88, 8, 1);
        AssertRow(set, "BomberUpgradeEpoch15", "Bomber", 940, 9, 270, 1.5, 0.3);
        AssertRow(set, "LightMounted2UpgradeEpoch13", "LightMounted2", 150, 10, 60, 9, 2);

        // Ability-override semantics composed from the rows' UPGRADEREFS.
        var tank = Weapon(set, "LightMounted1UpgradeEpoch15");
        Assert.Equal("TankShell", tank.Projectile.Value.Name);
        Assert.Equal(new Quantity(0.5, QuantityUnit.Seconds), tank.PreAttackDelay.Value);

        var fighter = Weapon(set, "FighterUpgradeEpoch15");
        Assert.Equal("GuidedMissile", fighter.Projectile.Value.Name);
        Assert.Equal(32, fighter.ClipSize.Value);

        var bomber = Weapon(set, "BomberUpgradeEpoch15");
        Assert.Equal("Bomb", bomber.Projectile.Value.Name);
        Assert.Equal(10, bomber.ClipSize.Value);
        Assert.Equal(new Quantity(3, QuantityUnit.SourceDistance), bomber.PrimaryDamageRadius.Value);

        var artillery = Weapon(set, "HeavyArtilleryUpgradeEpoch14");
        Assert.Equal("GuidedMissile", artillery.Projectile.Value.Name);
        Assert.Equal(new Quantity(3.0, QuantityUnit.SourceDistance), artillery.MinimumAttackRange.Value);

        // HeavyTank e15 carries a VehicleMove override; Fighter e15 an AircraftMove override.
        var heavyTankUnit = set.Records.OfType<UnitRecord>().Single(r => r.Id.Name == "HeavyMounted1UpgradeEpoch15");
        var heavyTankMove = set.Records.OfType<LocomotorRecord>().Single(l => l.Id.Name == "HeavyMounted1UpgradeEpoch15.Move");
        Assert.Equal(["HeavyMounted1UpgradeEpoch15.Move"], heavyTankUnit.Locomotors.Items.Select(id => id.Name));
        Assert.Equal(new Quantity(2.3, QuantityUnit.SourceDistancePerSecond), heavyTankMove.Speed.Value);

        var fighterUnit = set.Records.OfType<UnitRecord>().Single(r => r.Id.Name == "FighterUpgradeEpoch15");
        var fighterMove = set.Records.OfType<LocomotorRecord>().Single(l => l.Id.Name == "FighterUpgradeEpoch15.Move");
        Assert.Equal(["FighterUpgradeEpoch15.Move"], fighterUnit.Locomotors.Items.Select(id => id.Name));
        Assert.Equal(new Quantity(8.05, QuantityUnit.SourceDistancePerSecond), fighterMove.Speed.Value);
        Assert.Equal(new Quantity(95, QuantityUnit.DegreesPerSecond), fighterMove.TurnRate.Value);
        Assert.Equal(new Quantity(4.0, QuantityUnit.SourceDistance), fighterMove.PreferredHeight.Value);

        // Missiles resolve against db/Visuals/ammo.ddf with authored speeds.
        var missiles = set.Records.OfType<ProjectileRecord>().ToDictionary(p => p.Id.Name, p => p.Speed.Value);
        Assert.Equal(new Quantity(20, QuantityUnit.SourceDistancePerSecond), missiles["TankShell"]);
        Assert.Equal(new Quantity(11, QuantityUnit.SourceDistancePerSecond), missiles["GuidedMissile"]);
        Assert.Equal(new Quantity(2, QuantityUnit.SourceDistancePerSecond), missiles["Bomb"]);
    }

    /// <summary>
    /// The census of gameplay fields M2 does not simulate for the reference roster. Each is a real
    /// authored mechanic reported as excluded, never silently dropped; M3 owns the balance ones.
    /// </summary>
    public static readonly string[] ReferenceExclusions =
    [
        "ownertype", "rps", "FootprintType", "SizeY", "mass", "popCount", "attributes", "nonAttributes", "stance",
        "Garrisonable", "Convertable", "ConvertAttack", "Cost", "GoesOnAirMissions", "visualName",
        "Attack.areaDamageFriendlyRatio", "Attack.angularRange", "Attack.turretControllerName", "Attack.ignoreBlocking",
        "Attack.attackOnTheMove", "Attack.WeaponType", "Attack.packTime", "Attack.unpackTime", "Attack.minRangeImmobile",
        "Attack.capture", "Attack.startWithNoOrdnance",
        "VehicleMove.followTerrainSlope", "VehicleMove.mustMoveToTurn", "VehicleMove.bounce",
        "AircraftMove.followTerrainSlope", "AircraftMove.mustMoveToTurn", "AircraftMove.ElevChangeRate",
        "AircraftMove.OnGroundScale", "AircraftMove.CargoHeight", "AircraftMove.accel",
        "Move.angAccel", "Move.bounce",
        "ProjectileMissileMove.type", "ProjectileMissileMove.accel", "ProjectileMissileMove.angSpeed",
        "Missile.PayloadAttack",
    ];

    private static void AssertRow(UnitContentSet set, string rowKey, string unitType, double hp, double los, double damage, double range, double reload)
    {
        var unit = set.Records.OfType<UnitRecord>().Single(r => r.Id.Name == rowKey);
        Assert.Equal(unitType, unit.Parent?.Name);
        Assert.Equal(hp, unit.MaxHealth.Value.Value);
        Assert.Equal(los, unit.VisionRange.Value.Value);

        var weapon = Weapon(set, rowKey);
        Assert.Equal(damage, weapon.PrimaryDamage.Value.Value);
        Assert.Equal(range, weapon.AttackRange.Value.Value);
        Assert.Equal(reload, weapon.ReloadTime.Value.Value);
    }

    private static WeaponRecord Weapon(UnitContentSet set, string rowKey)
    {
        return set.Records.OfType<WeaponRecord>().Single(w => w.Id.Name == $"{rowKey}.Attack");
    }
}
