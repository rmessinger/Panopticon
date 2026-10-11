using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-M2-02: the reference movement families materialize to typed native modules. Ground
/// vehicles keep their movement classification, aircraft get one typed JetAIUpdate module (fixed
/// wing versus authored hover), weapons keep target masks, ordnance, pre-attack delay, and
/// projectile travel speed from authored EE2 values. Synthetic sources shaped like the roster; no
/// install required.
/// </summary>
public class Ee2TypedMaterializationTests
{
    private const string GroundDdf =
        """
        UnitType HeavyMounted
        {
            properties { rps = Heavy_Mounted hitpoints = 100 }
            abilities [ Garrisonable { garrisonTypes = [ Fortress ] } ]
        }
        UnitType Tank
        {
            parent = HeavyMounted
            properties { moveType = Mechanized SizeX = 0.7 hitpoints = 1000 rps = Light_Mounted }
            abilities [
                Attack { range = 7; reloadTime = 2; applyDamageTime = 0.5; damage = 31 }
                VehicleMove {speed = 2.0125; angSpeed = 180}
                LOS { range = 5 }
            ]
        }
        """;

    private const string AirDdf =
        """
        UnitType Aircraft
        {
            properties { moveType = Air }
            abilities [ AircraftMove {speed = 1.725; angSpeed = 180} ]
        }
        UnitType Fighter
        {
            parent = Aircraft
            properties { moveType = Air rps = Air_Superiority hitpoints = 666 }
            abilities [
                AircraftMove {speed = 6.9; angSpeed = 90; CruisingHeight = 4.0; canHover = 0}
                Attack { range = 0; reloadTime = 1; applyDamageTime = 0; damage = 0; missileName = "GuidedMissile" }
                LOS { range = 0 }
            ]
        }
        UnitType Helicopter
        {
            properties { moveType = Air hitpoints = 666 }
            abilities [
                AircraftMove {speed = 3.45; angSpeed = 90; CruisingHeight = 3.0; canHover = 1}
                Attack { range = 7; reloadTime = 2; applyDamageTime = 0; damage = 50; missileName = GuidedMissile }
                LOS { range = 5 }
            ]
        }
        UpgradeAbilities FighterEpoch15Attack
        {
            upgradedAbilities = [
                AircraftMove {speed = 8.05; angSpeed = 95; CruisingHeight = 4.0}
                Attack { damage = 0; range = 0; reloadTime = 666; applyDamageTime = 0; missileName = "GuidedMissile"; ordnance = 32 }
            ]
        }
        """;

    private const string ModelDdf =
        """
        UnitModel Fighter_15
        {
            DefaultModel = AF15_Atmospheric.nif
            States = [
                { StateName = Idle AnimName = af15_atmospheric_idle.kf }
                { StateName = Walk AnimName = af15_atmospheric_walk.kf }
                { StateName = Death AnimName = af15_atmospheric_death.kf }
            ]
        }
        UnitModel HelicopterVisual
        {
            DefaultModel = llm13_missilehelicopter.nif
            States = [
                { StateName = Idle AnimName = llm13_missilehelicopter_idle.kf }
                { StateName = Walk AnimName = llm13_missilehelicopter_walk.kf }
                { StateName = Attack1 AnimName = llm13_missilehelicopter_attack.kf }
                { StateName = Death AnimName = llm13_missilehelicopter_death.kf }
            ]
        }
        UnitModel TankVisual
        {
            DefaultModel = llm15_disruptor.nif
            States = [
                { StateName = Idle AnimName = llm15_disruptor_idle.kf }
                { StateName = Walk AnimName = llm15_disruptor_walk.kf }
                { StateName = Attack1 AnimName = llm15_disruptor_attack.kf }
                { StateName = Death AnimName = llm15_disruptor_death.kf }
            ]
        }
        """;

    private const string AmmoDdf =
        """
        UnitType GuidedMissile
        {
            abilities [ ProjectileMissileMove { type = GuidedMissile; speed = 11 } ]
        }
        UnitType TankShell
        {
            abilities [ ProjectileMissileMove { type = RegularProjectile; speed = 20 } ]
        }
        """;

    /// <summary>rps matrix fragment: air columns hit air classes at these authored multipliers.</summary>
    private const string CombatCsv =
        """"
        // ROWS ARE DEFENDERS,,COLUMNS ARE ATTACKERS,,,,,,
        //,Null,Light_Mounted,Heavy_Mounted,Air_Superiority,Helicopter
        Null,0,0,0,0,0
        Light_Infantry,0,100,100,50,100
        Heavy_Mounted,0,100,100,50,100
        Close_Air_Support,0,0,0,200,0
        Helicopter,0,0,0,200,100
        """";

    private const string UnitTable =
        """"
        // UPGRADE,UNIT,TYPE,EPOCH,HP,LOS,DAMAGE,RANGE,RELOAD,BUILDTIME,DISPLAYNAME,VISUAL,UPGRADEREFS
        LightMounted1UpgradeEpoch15,Tank,Unit,15,300,11,30,10,3.5,45,Tank,TankVisual,[]
        FighterUpgradeEpoch15,Fighter,Unit,15,500,9,88,8,1,30,Fighter,Fighter_15,[FighterEpoch15Attack]
        HeavyMounted2UpgradeEpoch13,Helicopter,Unit,13,175,10,20,9,0.5,30,Heli,HelicopterVisual,[]
        """";

    private static readonly string[] Exclusions =
    [
        "rps", "Garrisonable", "ProjectileMissileMove.type",
    ];

    private static Ee2SourceFiles Files()
    {
        var db = new MemoryFileSystem();
        db.AddFile("db/Units/ground.ddf", GroundDdf);
        db.AddFile("db/Units/air.ddf", AirDdf);
        db.AddFile("db/Units/models.ddf", ModelDdf);
        db.AddFile(Ee2SourceFiles.MissileTablePath, AmmoDdf);
        db.AddFile(Ee2SourceFiles.CombatTablePath, CombatCsv);
        db.AddFile(Ee2SourceFiles.DefaultUnitTablePath, UnitTable);

        var graphics = new MemoryFileSystem();
        graphics.AddFile("cache/nifcache/af15_atmospheric_we.nifcache", [0]);
        graphics.AddFile("cache/nifcache/llm13_missilehelicopter_we.nifcache", [0]);
        graphics.AddFile("cache/nifcache/llm15_disruptor_we.nifcache", [0]);
        foreach (var model in new[] { "af15_atmospheric", "llm13_missilehelicopter", "llm15_disruptor" })
        {
            foreach (var clip in new[] { "idle", "walk", "attack", "death" })
            {
                graphics.AddFile($"Graphics/Units/{model}_{clip}.kf", [0]);
            }
        }

        return Ee2SourceFiles.FromDatabase(db, graphics);
    }

    private static MaterializedUnitContent Materialize(params string[] rowKeys)
    {
        var set = new Ee2UnitAdapter(Files(), new Ee2ImportPolicy(15, "we", Exclusions)).ReadRows(rowKeys);
        var resolution = UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault());
        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));

        var content = UnitRuntimeMaterializer.Materialize(
            resolution,
            new UnitMaterializationContext(new UnitConversionPolicy(SageGame.CncGenerals, gameUnitsPerSourceDistance: 12)));
        Assert.True(content.Succeeded, string.Join("\n", content.Diagnostics));
        return content;
    }

    private static ObjectDefinition Definition(MaterializedUnitContent content, string rowKey)
    {
        return content.Get<ObjectDefinition>(new ContentId(ContentSource.Ee2, ContentKind.Unit, rowKey));
    }

    [Fact]
    public void TrackedVehicleKeepsMovementClassification()
    {
        var definition = Definition(Materialize("LightMounted1UpgradeEpoch15"), "LightMounted1UpgradeEpoch15");

        Assert.True(definition.KindOf.Get(ObjectKinds.Vehicle));
        Assert.True(definition.KindOf.Get(ObjectKinds.Selectable));
        Assert.False(definition.KindOf.Get(ObjectKinds.Aircraft));

        var locomotor = definition.LocomotorSets[LocomotorSetType.Normal].Locomotors[0].Value;
        Assert.Equal(LocomotorAppearance.Treads, locomotor.Appearance);
        Assert.Equal(Surfaces.Ground, locomotor.Surfaces);
        Assert.Equal(2.0125 * 12 / 30, locomotor.MaxSpeed, 5);
        Assert.False(definition.LocomotorSets.ContainsKey(LocomotorSetType.Taxiing));
    }

    [Fact]
    public void FixedWingAircraftGetsOneTypedJetModuleAndBothLocomotorSets()
    {
        var definition = Definition(Materialize("FighterUpgradeEpoch15"), "FighterUpgradeEpoch15");

        Assert.True(definition.KindOf.Get(ObjectKinds.Aircraft));
        Assert.True(definition.KindOf.Get(ObjectKinds.Vehicle));

        // One AI module, typed: the inherited ground AI module is replaced through the shared tag,
        // never duplicated (JetAIUpdateModuleData derives from AIUpdateModuleData, so the exact
        // runtime type is asserted).
        var ai = Assert.Single(definition.Behaviors.Values.Select(module => module.Data).OfType<AIUpdateModuleData>());
        Assert.IsType<JetAIUpdateModuleData>(ai);

        var jet = Assert.IsType<JetAIUpdateModuleData>(definition.Behaviors.Values.Single(module => module.Data is JetAIUpdateModuleData).Data);
        Assert.True(jet.NeedsRunway);
        Assert.True(jet.KeepsParkingSpaceWhenAirborne);
        Assert.Equal(4.0 * 12, jet.MinHeight);

        var locomotor = definition.LocomotorSets[LocomotorSetType.Normal].Locomotors[0].Value;
        Assert.Equal(LocomotorAppearance.Wings, locomotor.Appearance);
        Assert.Equal(Surfaces.Air, locomotor.Surfaces);
        Assert.Equal(8.05 * 12 / 30.0, locomotor.MaxSpeed, 5);
        Assert.Equal(4.0 * 12, locomotor.PreferredHeight, 3);
        Assert.Equal(95 * Math.PI / 180.0 / 30, locomotor.MaxTurnRate, 7);

        // Every Generals airborne locomotor applies 2D friction while airborne; without it
        // forward-speed maintenance accumulates lateral velocity through turns (WI-M2-04).
        Assert.True(locomotor.Apply2DFrictionWhenAirborne);
        Assert.True(locomotor.AllowAirborneMotiveForce);

        // The parking flow taxis on a dedicated ground locomotor mirroring Generals
        // BasicJetTaxiLocomotor, not the authored air speed (whose turn radius dwarfs the
        // parking bone spacing).
        Assert.True(definition.LocomotorSets.ContainsKey(LocomotorSetType.Taxiing));
        var taxi = Assert.Single(definition.LocomotorSets[LocomotorSetType.Taxiing].Locomotors).Value;
        Assert.Equal(Surfaces.Ground, taxi.Surfaces);
        Assert.Equal(LocomotorAppearance.Treads, taxi.Appearance);
        Assert.Equal(50 / 30.0, taxi.MaxSpeed, 5);
        Assert.Equal(180 * Math.PI / 180.0 / 30, taxi.MaxTurnRate, 7);
        Assert.Equal(100 / (30.0 * 30.0), taxi.Acceleration, 5);
    }

    [Fact]
    public void AuthoredHoverSelectsTheHelicopterConfiguration()
    {
        var definition = Definition(Materialize("HeavyMounted2UpgradeEpoch13"), "HeavyMounted2UpgradeEpoch13");

        var jet = Assert.IsType<JetAIUpdateModuleData>(definition.Behaviors.Values.Single(module => module.Data is JetAIUpdateModuleData).Data);
        Assert.False(jet.NeedsRunway);
        Assert.False(jet.KeepsParkingSpaceWhenAirborne);
        Assert.Equal(3.0 * 12, jet.MinHeight);

        var locomotor = definition.LocomotorSets[LocomotorSetType.Normal].Locomotors[0].Value;
        Assert.Equal(LocomotorAppearance.Hover, locomotor.Appearance);
        Assert.Equal(Surfaces.Air, locomotor.Surfaces);
        Assert.True(locomotor.Apply2DFrictionWhenAirborne);
    }

    [Fact]
    public void WeaponKeepsAuthoredSemanticsAndDataDrivenTargetMask()
    {
        var content = Materialize("LightMounted1UpgradeEpoch15", "FighterUpgradeEpoch15");

        // Tank (rps Light_Mounted) engages ground only; its authored attack carries no ordnance.
        var tank = content.Get<WeaponTemplate>(new ContentId(ContentSource.Ee2, ContentKind.Weapon, "LightMounted1UpgradeEpoch15.Attack"));
        Assert.Equal(WeaponAntiFlags.AntiGround, tank.AntiMask);
        Assert.Equal(0, tank.ClipSize);
        Assert.Equal(30, tank.Nuggets.OfType<DamageNugget>().Single().Damage);
        Assert.Equal(10 * 12, tank.AttackRange, 3);
        Assert.Equal(0.5 * 30, tank.PreAttackDelay.Min.Value, 1);

        // Fighter (rps Air_Superiority) engages air and ground; ordnance 32 becomes the clip and
        // the weapon rearms at base (RETURN_TO_BASE), so a sortie ends with the return flight.
        var fighter = content.Get<WeaponTemplate>(new ContentId(ContentSource.Ee2, ContentKind.Weapon, "FighterUpgradeEpoch15.Attack"));
        Assert.Equal(WeaponAntiFlags.AntiGround | WeaponAntiFlags.AntiAirborneVehicle, fighter.AntiMask);
        Assert.Equal(32, fighter.ClipSize);
        Assert.Equal(WeaponReloadType.ReturnToBase, fighter.AutoReloadsClip);
        Assert.Equal(11.0 * 12 / 30, fighter.WeaponSpeed, 5);
    }

    [Fact]
    public void MissingAttackClipIsAReportedGapNotAFabricatedBinding()
    {
        // Fighter_15 authors no Attack1 state: materialization succeeds without the condition
        // state, and the validator reports the missing clip as a warning.
        var set = new Ee2UnitAdapter(Files(), new Ee2ImportPolicy(15, "we", Exclusions)).ReadRows(["FighterUpgradeEpoch15"]);
        var resolution = UnitImportResolution.Resolve(set, UnitCapabilityRegistry.CreateDefault());

        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));
        Assert.Contains(resolution.Diagnostics, d => !d.IsError && d.Code == DiagnosticCodes.MissingVisualState && d.Message.Contains("Attack"));

        var content = UnitRuntimeMaterializer.Materialize(
            resolution,
            new UnitMaterializationContext(new UnitConversionPolicy(SageGame.CncGenerals, gameUnitsPerSourceDistance: 12)));
        Assert.True(content.Succeeded, string.Join("\n", content.Diagnostics));
    }

    [Fact]
    public void UnsupportedMovementFamilyIsRejected()
    {
        const string ShipDdf =
            """
            UnitType Boat
            {
                properties { moveType = Foot hitpoints = 50 }
                abilities [ ShipMove {speed = 1; angSpeed = 90} LOS { range = 5 } ]
            }
            """;
        var db = new MemoryFileSystem();
        db.AddFile("db/Units/ship.ddf", ShipDdf);
        var set = new Ee2UnitAdapter(
            new Ee2SourceFiles(db, new MemoryFileSystem(), ["db/Units/ship.ddf"], Ee2SourceFiles.DefaultUnitTablePath),
            new Ee2ImportPolicy(1, "we", [])).Read(["Boat"]);

        Assert.Contains(set.Records.OfType<UnitRecord>().Single(r => r.Id.Name == "Boat").Unclassified, f => f.Name == "ShipMove" && f.AffectsGameplay);
    }
}
