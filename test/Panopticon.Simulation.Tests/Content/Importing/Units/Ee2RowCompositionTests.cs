using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-M2-01: explicit tech-tree row keys select the published epoch row (the row's UNIT cell
/// names the UnitType), UpgradeAbilities refs compose attack and movement overrides onto the
/// inherited chain, missile names resolve against db/Visuals/ammo.ddf, and unrelated inventory
/// records cannot reject a supported root. Synthetic sources; no install required.
/// </summary>
public class Ee2RowCompositionTests
{
    private const string MountedDdf =
        """
        UnitType HeavyMounted
        {
            properties { rps = HeavyMounted hitpoints = 100 }
            abilities [ Convertable { convertTimeModifier = 3.0 } Garrisonable { garrisonTypes = [ Fortress ] } ]
        }
        """;

    private const string TankDdf =
        """
        UnitType Tank
        {
            parent = HeavyMounted
            properties { moveType = Mechanized SizeX = 0.7 hitpoints = 1000 rps = LightMounted }
            abilities [
                Attack { range = 7; reloadTime = 2; applyDamageTime = 0.5; damage = 31 }
                VehicleMove {speed = 2.0125; angSpeed = 180; followTerrainSlope = 1}
                LOS { range = 5 }
            ]
        }
        UnitType BrokenUnit
        {
            parent = DoesNotExist
            abilities [ Move {speed = 1; angSpeed = 90} ]
        }
        UpgradeAbilities LightMounted1Epoch15Attack
        {
            upgradedAbilities = [
                Attack { damage = 0; range = 0; reloadTime = 666; applyDamageTime = 0.5; missileName = TankShell; angularRange = 120 }
            ]
        }
        UpgradeAbilities HeavyMounted1Epoch15Move
        {
            upgradedAbilities = [
                VehicleMove {speed = 2.3; angSpeed = 180; followTerrainSlope = 1; mustMoveToTurn = 0}
            ]
        }
        UpgradeAbilities LightMounted2Epoch13Attack
        {
            upgradedAbilities = [
                Attack { damage = 0; range = 0; reloadTime = 666; applyDamageTime = 0 }
            ]
        }
        UpgradeRefSet LightMounted_Improve_Reset
        {
            upgradeRefs = [ VeteranHitPoints ]
        }
        UnitType Warehouse
        {
            properties { hitpoints = 500 }
            abilities [ DropOff { someKey = 1 } ]
        }
        """;

    private const string AircraftDdf =
        """
        UnitType Helicopter
        {
            properties { moveType = Air hitpoints = 666 }
            abilities [
                AircraftMove {speed = 3.45; angSpeed = 90; CruisingHeight = 3.0; ElevChangeRate = 1; canHover = 1}
                Attack { range = 7; reloadTime = 2; applyDamageTime = 0; damage = 50; missileName = GuidedMissile }
                LOS { range = 5 }
            ]
        }
        """;

    private const string ModelDdf =
        """
        UnitModel LightMounted1_15
        {
            DefaultModel = LLM15_Disruptor.NIF
            States = [
                { StateName = Idle AnimName = llm15_disruptor_idle.KF }
                { StateName = Walk AnimName = llm15_disruptor_walk.KF }
                { StateName = Attack1 AnimName = llm15_disruptor_attack.KF }
                { StateName = Death AnimName = llm15_disruptor_death.KF }
            ]
        }
        """;

    private const string AmmoDdf =
        """
        UnitType TankShell
        {
            Parent = Ammo
            abilities [ ProjectileMissileMove { type = RegularProjectile; speed = 20 } PayloadAttack { weaponType = MediumProjectile } ]
        }
        UnitType GuidedMissile
        {
            abilities [ ProjectileMissileMove { type = GuidedMissile; speed = 11 } ]
        }
        """;

    private const string AmmoWithoutTankShellDdf =
        """
        UnitType GuidedMissile
        {
            abilities [ ProjectileMissileMove { type = GuidedMissile; speed = 11 } ]
        }
        """;

    private const string UnitTable =
        """"
        // UPGRADE,UNIT,TYPE,EPOCH,HP,LOS,DAMAGE,RANGE,RELOAD,BUILDTIME,DISPLAYNAME,VISUAL,UPGRADEREFS
        LightMounted1UpgradeEpoch15,Tank,Unit,15,300,11,30,10,3.5,45,Tank,LightMounted1_15,[LightMounted_Improve_Reset LightMounted1Epoch15Attack]
        HeavyMounted1UpgradeEpoch15,Tank,Unit,15,350,11,25,10,3,45,Heavy Tank,LightMounted1_15,[LightMounted_Improve_Reset LightMounted1Epoch15Attack HeavyMounted1Epoch15Move]
        LightMounted2UpgradeEpoch13,Helicopter,Unit,13,150,10,60,9,2,30,Heli,LightMounted1_15,[LightMounted_Improve_Reset LightMounted2Epoch13Attack]
        """";

    /// <summary>Tank e15 uses only the Attack override; movement stays the UnitType's own VehicleMove.</summary>
    private static readonly string[] TankExclusions =
    [
        "rps", "Convertable", "Garrisonable", "Attack.angularRange", "VehicleMove.followTerrainSlope", "VehicleMove.mustMoveToTurn",
        "ProjectileMissileMove.type", "Missile.PayloadAttack",
    ];

    private static Ee2SourceFiles Files()
    {
        var db = new MemoryFileSystem();
        db.AddFile("db/Units/mounted.ddf", MountedDdf);
        db.AddFile("db/Units/tank.ddf", TankDdf);
        db.AddFile("db/Units/helicopter.ddf", AircraftDdf);
        db.AddFile("db/Units/light_mounted1.ddf", ModelDdf);
        db.AddFile(Ee2SourceFiles.MissileTablePath, AmmoDdf);
        db.AddFile(Ee2SourceFiles.DefaultUnitTablePath, UnitTable);

        var graphics = new MemoryFileSystem();
        graphics.AddFile("cache/nifcache/llm15_disruptor_we.nifcache", [0]);
        foreach (var clip in new[] { "idle", "walk", "attack", "death" })
        {
            graphics.AddFile($"Graphics/Units/llm15_disruptor_{clip}.kf", [0]);
        }

        return Ee2SourceFiles.FromDatabase(db, graphics);
    }

    private static UnitImportResolution ResolveRows(Ee2ImportPolicy policy, params string[] rowKeys)
    {
        var set = new Ee2UnitAdapter(Files(), policy).ReadRows(rowKeys);
        return UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault());
    }

    private static UnitRecord RootUnit(UnitImportResolution resolution, string rowKey)
    {
        return resolution.Get<UnitRecord>(new ContentId(ContentSource.Ee2, ContentKind.Unit, rowKey));
    }

    private static WeaponRecord RootWeapon(UnitImportResolution resolution, string rowKey)
    {
        var unit = RootUnit(resolution, rowKey);
        return resolution.Get<WeaponRecord>(resolution.Get<AbilityRecord>(unit.ResolvedModules.Single(m => m.Tag == "Attack").Ability!.Value).Weapon.Value);
    }

    [Fact]
    public void ExplicitRowKeySelectsItsUnitTypeAndComposesStatsAndOverrides()
    {
        var resolution = ResolveRows(new Ee2ImportPolicy(15, "we", TankExclusions), "LightMounted1UpgradeEpoch15");

        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));
        var set = new Ee2UnitAdapter(Files(), new Ee2ImportPolicy(15, "we", TankExclusions)).ReadRows(["LightMounted1UpgradeEpoch15"]);
        var unit = RootUnit(resolution, "LightMounted1UpgradeEpoch15");
        var weapon = RootWeapon(resolution, "LightMounted1UpgradeEpoch15");

        // The row's UNIT cell selects Tank, not a UnitType named after the row key.
        Assert.Equal(new ContentId(ContentSource.Ee2, ContentKind.Unit, "Tank"), set.Records.OfType<UnitRecord>().Single(r => r.Id.Name == "LightMounted1UpgradeEpoch15").Parent);
        Assert.Equal(new Quantity(300, QuantityUnit.HitPoints), unit.MaxHealth.Value);
        Assert.Equal(new Quantity(11, QuantityUnit.SourceDistance), unit.VisionRange.Value);

        // CSV values beat the placeholder override values (damage 0, range 0, reload 666).
        Assert.Equal(new Quantity(30, QuantityUnit.HitPoints), weapon.PrimaryDamage.Value);
        Assert.Equal(new Quantity(10, QuantityUnit.SourceDistance), weapon.AttackRange.Value);
        Assert.Equal(new Quantity(3.5, QuantityUnit.Seconds), weapon.ReloadTime.Value);

        // The UpgradeAbilities override supplies the semantics the CSV does not carry.
        Assert.Equal(new Quantity(0.5, QuantityUnit.Seconds), weapon.PreAttackDelay.Value);
        Assert.Equal("TankShell", weapon.Projectile.Value.Name);
        Assert.False(weapon.ClipSize.IsPresent);

        // No Move override: the row inherits Tank's own VehicleMove values.
        var locomotor = resolution.Get<LocomotorRecord>(unit.Locomotors.Resolved.Single());
        Assert.Equal("Tank.Move", locomotor.Id.Name);
        Assert.Equal(new Quantity(2.0125, QuantityUnit.SourceDistancePerSecond), locomotor.Speed.Value);

        var projectile = resolution.Closure.OfType<ProjectileRecord>().Single();
        Assert.Equal(new Quantity(20, QuantityUnit.SourceDistancePerSecond), projectile.Speed.Value);
        Assert.Contains(projectile.Unclassified, f => f.Name == "ProjectileMissileMove.type" && f.AffectsGameplay);
    }

    [Fact]
    public void MoveOverrideReplacesTheInheritedLocomotor()
    {
        var resolution = ResolveRows(new Ee2ImportPolicy(15, "we", TankExclusions), "HeavyMounted1UpgradeEpoch15");

        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));
        var set = new Ee2UnitAdapter(Files(), new Ee2ImportPolicy(15, "we", TankExclusions)).ReadRows(["HeavyMounted1UpgradeEpoch15"]);
        var unit = RootUnit(resolution, "HeavyMounted1UpgradeEpoch15");
        var locomotor = resolution.Get<LocomotorRecord>(unit.Locomotors.Resolved.Single());

        Assert.Equal("HeavyMounted1UpgradeEpoch15.Move", locomotor.Id.Name);
        Assert.Equal(new ContentId(ContentSource.Ee2, ContentKind.Locomotor, "Tank.Move"), set.Records.OfType<LocomotorRecord>().Single(l => l.Id.Name == "HeavyMounted1UpgradeEpoch15.Move").Parent);
        Assert.Equal(new Quantity(2.3, QuantityUnit.SourceDistancePerSecond), locomotor.Speed.Value);
        Assert.Equal(new Quantity(180, QuantityUnit.DegreesPerSecond), locomotor.TurnRate.Value);
    }

    [Fact]
    public void AircraftMovementKeepsCruiseHeightAndHover()
    {
        var exclusions = TankExclusions.Concat(["AircraftMove.ElevChangeRate"]).ToArray();
        var resolution = ResolveRows(new Ee2ImportPolicy(13, "we", exclusions), "LightMounted2UpgradeEpoch13");

        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));
        var unit = RootUnit(resolution, "LightMounted2UpgradeEpoch13");
        var locomotor = resolution.Get<LocomotorRecord>(unit.Locomotors.Resolved.Single());

        Assert.Equal("Helicopter.Move", locomotor.Id.Name);
        Assert.Equal(new Quantity(3.45, QuantityUnit.SourceDistancePerSecond), locomotor.Speed.Value);
        Assert.Equal(new Quantity(3.0, QuantityUnit.SourceDistance), locomotor.PreferredHeight.Value);
        Assert.True(locomotor.CanHover.Value);
        Assert.Contains(locomotor.Unclassified, f => f.Name == "AircraftMove.ElevChangeRate");

        // The row's override block authors no missileName, so the Helicopter chain's GuidedMissile
        // carries into the row weapon through weapon inheritance.
        var weapon = RootWeapon(resolution, "LightMounted2UpgradeEpoch13");
        Assert.Equal("GuidedMissile", weapon.Projectile.Value.Name);
        var projectile = resolution.Closure.OfType<ProjectileRecord>().Single(p => p.Id.Name == "GuidedMissile");
        Assert.Equal(new Quantity(11, QuantityUnit.SourceDistancePerSecond), projectile.Speed.Value);
    }

    [Fact]
    public void MissingMissileFailsTheImportInsteadOfFallingBackToInstantHit()
    {
        var db = new MemoryFileSystem();
        db.AddFile("db/Units/mounted.ddf", MountedDdf);
        db.AddFile("db/Units/tank.ddf", TankDdf);
        db.AddFile(Ee2SourceFiles.MissileTablePath, AmmoWithoutTankShellDdf);
        db.AddFile(Ee2SourceFiles.DefaultUnitTablePath, UnitTable);
        var files = Ee2SourceFiles.FromDatabase(db, new MemoryFileSystem());

        var set = new Ee2UnitAdapter(files, new Ee2ImportPolicy(15, "we", TankExclusions)).ReadRows(["LightMounted1UpgradeEpoch15"]);

        Assert.Contains(set.Diagnostics, d => d.IsError && d.Code == DiagnosticCodes.UnsupportedMechanic && d.Message.Contains("TankShell"));
        Assert.False(UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault()).Succeeded);
    }

    [Fact]
    public void UnknownRowKeyOrUnitTypeIsAnExplicitFailure()
    {
        var set = new Ee2UnitAdapter(Files(), new Ee2ImportPolicy(15, "we", TankExclusions)).ReadRows(["LightMounted1UpgradeEpoch16", "MadeUpUpgradeEpoch15"]);

        Assert.Contains(set.Diagnostics, d => d.IsError && d.Code == DiagnosticCodes.MissingRoot && d.Message.Contains("LightMounted1UpgradeEpoch16"));
        Assert.Contains(set.Diagnostics, d => d.IsError && d.Code == DiagnosticCodes.MissingRoot && d.Message.Contains("MadeUpUpgradeEpoch15"));
    }

    [Fact]
    public void UnrelatedInventoryFailuresDoNotRejectASupportedRoot()
    {
        var resolution = ResolveRows(new Ee2ImportPolicy(15, "we", TankExclusions), "LightMounted1UpgradeEpoch15");

        // BrokenUnit has a missing parent and sits outside the requested closure: reported, but
        // downgraded to an inventory warning rather than rejecting the row.
        var inventory = resolution.Diagnostics.Single(d => d.Code == DiagnosticCodes.MissingParent);
        Assert.False(inventory.IsError);
        Assert.Contains("[inventory only]", inventory.Message);
        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));
    }

    [Fact]
    public void UnrelatedUnsupportedGameplayDoesNotRejectTheRoot()
    {
        // Warehouse (an unrelated discovered unit) carries an unsupported DropOff ability; it sits
        // outside the requested closure, so it must not reject the tank row.
        var resolution = ResolveRows(new Ee2ImportPolicy(15, "we", TankExclusions), "LightMounted1UpgradeEpoch15");

        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));
    }

    [Fact]
    public void UnsupportedGameplayInsideTheRootClosureStillRejects()
    {
        // Convertable and Garrisonable ride the HeavyMounted ancestor into every tank closure;
        // without exclusions they are unsupported required gameplay and must block publication.
        var resolution = ResolveRows(
            new Ee2ImportPolicy(15, "we", TankExclusions.Where(e => e is not ("Convertable" or "Garrisonable")).ToArray()),
            "LightMounted1UpgradeEpoch15");

        Assert.False(resolution.Succeeded);
        Assert.Contains(resolution.Diagnostics, d => d.IsError && d.Code == DiagnosticCodes.UnsupportedMechanic && d.Message.Contains("Convertable"));
    }

    [Fact]
    public void MissingParentInsideTheRootClosureStillRejects()
    {
        var db = new MemoryFileSystem();
        db.AddFile("db/Units/tank.ddf", TankDdf);
        db.AddFile(Ee2SourceFiles.DefaultUnitTablePath, UnitTable);
        var files = Ee2SourceFiles.FromDatabase(db, new MemoryFileSystem());

        // mounted.ddf is absent, so Tank's own parent is missing: the root must fail explicitly.
        var set = new Ee2UnitAdapter(files, new Ee2ImportPolicy(15, "we", TankExclusions)).ReadRows(["LightMounted1UpgradeEpoch15"]);

        Assert.False(UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault()).Succeeded);
    }
}
