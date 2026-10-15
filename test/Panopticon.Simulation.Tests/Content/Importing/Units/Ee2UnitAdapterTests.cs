using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Ee2;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Logic.Object;
using Panopticon.Tests.Content;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>Synthetic EE2 sources shaped like the real maceman closure; no install required.</summary>
public class Ee2UnitAdapterTests
{
    private const string HumanDdf =
        """
        UnitType LandMammal
        {
            properties {
                moveType = kUnitMoveType_Foot
                SizeX = 0.125
                hitpoints = 50
                rps = LandNonCombat
            }
            abilities [ LOS { range = 5 } ]
        }
        UnitType Human
        {
            parent = LandMammal
            abilities [ Move {speed = 1.15; angSpeed = 720} Garrisonable { garrisonTypes = [ Tower ] } ]
            icon = icon_unit_default_human
        }
        UnitModel BaseHuman
        {
            ChildNames = [ TheHealthBar ]
            DefaultState = Idle
        }
        """;

    private const string MacemanDdf =
        """
        UnitType HeavyInfantry1
        {
            parent = Human
            properties { rps = HeavyInfantry SizeX = 0.125 }
            abilities = [
                Attack { range = 0.2; reloadTime = 1.5; applyDamageTime = 0.5; damage = 0 }
                LOS { range = 0 }
                Move {speed = 1.7250; angSpeed = 720}
            ]
        }
        UnitModel HeavyInfantry1_01
        {
            Parent = BaseHuman
            DefaultModel = lhi1_maceman.nif
            States = [
                { StateName = Idle AnimName = sh_maceman_idle01.KF }
                { StateName = Walk AnimName = sh_maceman_walk01.KF }
                { StateName = Attack1 AnimName = sh_maceman_attack01.KF }
                { StateName = Death AnimName = sh_maceman_death01.KF }
            ]
        }
        """;

    private const string UnitTable =
        """"
        // UPGRADE,UNIT,TYPE,EPOCH,HP,LOS,DAMAGE,RANGE,RELOAD,BUILDTIME,FOOD,WOOD,STONE,GOLD,TIN,DISPLAYNAME,VISUAL,CIV
        HeavyInfantry1UpgradeEpoch1,HeavyInfantry1,"""Unit""",1,75,5,5,0.8,1.5,37,35,0,0,15,10,Maceman,HeavyInfantry1_01,All
        """";

    /// <summary>rps matrix fragment: Heavy_Infantry hits ground and helicopters per the real table.</summary>
    private const string CombatCsv =
        """"
        // ROWS ARE DEFENDERS,,COLUMNS ARE ATTACKERS,,,,,,
        //,Null,Heavy_Infantry
        Null,0,0
        Light_Infantry,0,100
        Helicopter,0,100
        """";

    public static readonly string[] MacemanExclusions = ["rps", "Garrisonable", "Attack.applyDamageTime", "Cost"];

    private static Ee2SourceFiles Files()
    {
        var db = new MemoryFileSystem();
        db.AddFile("db/Units/human.ddf", HumanDdf);
        db.AddFile("db/Units/heavy_infantry1.ddf", MacemanDdf);
        db.AddFile(Ee2SourceFiles.CombatTablePath, CombatCsv);
        db.AddFile(Ee2SourceFiles.DefaultUnitTablePath, UnitTable);

        var graphics = new MemoryFileSystem();
        graphics.AddFile("cache/nifcache/lhi1_maceman_we.nifcache", [0]);
        graphics.AddFile("cache/nifcache/lhi1_maceman_am.nifcache", [0]);
        foreach (var clip in new[] { "idle01", "walk01", "attack01", "death01" })
        {
            graphics.AddFile($"Graphics/Humans/sh_maceman_{clip}.kf", [0]);
        }

        return new Ee2SourceFiles(db, graphics, ["db/Units/human.ddf", "db/Units/heavy_infantry1.ddf"], Ee2SourceFiles.DefaultUnitTablePath);
    }

    private static UnitContentSet Read(params string[] exclusions)
    {
        return new Ee2UnitAdapter(Files(), new Ee2ImportPolicy(1, "we", exclusions)).Read(["HeavyInfantry1"]);
    }

    private static UnitImportResolution Resolve(params string[] exclusions)
    {
        return UnitImportResolution.Resolve(Read(exclusions), UnitCapabilityRegistry.CreateDefault());
    }

    private static readonly ContentId Root = new(ContentSource.Ee2, ContentKind.Unit, "HeavyInfantry1UpgradeEpoch1");

    [Fact]
    public void EpochRowAndInheritedChainResolveToAuthoredValues()
    {
        var resolution = Resolve(MacemanExclusions);
        var unit = resolution.Get<UnitRecord>(Root);
        var weapon = resolution.Get<WeaponRecord>(unit.Weapons.Resolved.Count > 0 ? unit.Weapons.Resolved[0] : resolution.Get<AbilityRecord>(unit.ResolvedModules.Single(m => m.Tag == "Attack").Ability!.Value).Weapon.Value);
        var locomotor = resolution.Get<LocomotorRecord>(unit.Locomotors.Resolved.Single());

        Assert.True(resolution.Succeeded, string.Join("\n", resolution.Diagnostics.Where(d => d.IsError)));
        Assert.Equal(new Quantity(75, QuantityUnit.HitPoints), unit.MaxHealth.Value);
        Assert.Equal(new Quantity(5, QuantityUnit.SourceDistance), unit.VisionRange.Value);
        Assert.Equal(new Quantity(0.0625, QuantityUnit.SourceDistance), unit.GeometryMajorRadius.Value);
        Assert.Equal(new Quantity(37, QuantityUnit.Seconds), unit.BuildTime.Value);
        Assert.Contains("INFANTRY", unit.KindOf.Resolved);
        Assert.Contains("CAN_ATTACK", unit.KindOf.Resolved);
        Assert.Equal(new Quantity(5, QuantityUnit.HitPoints), weapon.PrimaryDamage.Value);
        Assert.Equal(new Quantity(0.8, QuantityUnit.SourceDistance), weapon.AttackRange.Value);
        Assert.Equal(new Quantity(1.5, QuantityUnit.Seconds), weapon.ReloadTime.Value);
        Assert.Equal(new Quantity(1.725, QuantityUnit.SourceDistancePerSecond), locomotor.Speed.Value);
        Assert.Equal("HeavyInfantry1.Move", locomotor.Id.Name);
    }

    [Fact]
    public void ProvenancePointsAtDdfAndCsvLines()
    {
        var set = Read(MacemanExclusions);

        Assert.Equal(new Provenance("ee2", Ee2SourceFiles.DefaultUnitTablePath, 2), set.Records.Single(r => r.Id.Equals(Root)).Provenance);
        Assert.Equal(1, set.Records.OfType<UnitRecord>().Single(r => r.Id.Name == "HeavyInfantry1").Provenance.Line);
    }

    [Fact]
    public void UnsupportedMechanicsBlockUnlessExcluded()
    {
        var blocked = Resolve();
        var allowed = Resolve(MacemanExclusions);

        var unsupported = blocked.Diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedMechanic).Select(d => d.Message).ToArray();
        Assert.Contains(unsupported, m => m.Contains("Garrisonable"));
        Assert.Contains(unsupported, m => m.Contains("Cost (FOOD=35)"));
        Assert.Contains(unsupported, m => m.Contains("rps"));
        Assert.False(blocked.Succeeded);

        Assert.True(allowed.Succeeded);
        Assert.Contains(allowed.Diagnostics, d => d.Code == DiagnosticCodes.ExcludedByPolicy && d.Message.Contains("Garrisonable"));
    }

    [Fact]
    public void VisualUsesRegionalVariantAndArchivePaths()
    {
        var resolution = Resolve(MacemanExclusions);
        var visual = resolution.Get<VisualStateRecord>(new ContentId(ContentSource.Ee2, ContentKind.VisualState, "HeavyInfantry1_01"));

        Assert.Equal("lhi1_maceman_we", visual.ModelKey.Value);
        Assert.Equal("lhi1_maceman_we#kf=Graphics/Humans/sh_maceman_walk01.kf", visual.StateAnimations["Move"].Value);
        Assert.Equal(4, visual.StateAnimations.Count);
    }

    [Fact]
    public void MissingEpochRowIsReported()
    {
        var set = new Ee2UnitAdapter(Files(), new Ee2ImportPolicy(3, "we", MacemanExclusions)).Read(["HeavyInfantry1"]);

        Assert.Contains(set.Diagnostics, d => d.Code == DiagnosticCodes.MissingRoot && d.Message.Contains("HeavyInfantry1UpgradeEpoch3"));
    }

    [Fact]
    public void MaterializesAndPublishesIntoEe2Collections()
    {
        var store = new AssetStore(SageGame.CncGenerals, null!, null!, null!, null!, null!, null!, OnDemandAssetLoadStrategy.None);
        store.PushScope();
        var publisher = new UnitContentPublisher(store);
        var policy = new UnitConversionPolicy(SageGame.CncGenerals, gameUnitsPerSourceDistance: 12);
        var content = UnitRuntimeMaterializer.Materialize(Resolve(MacemanExclusions), new UnitMaterializationContext(policy, publisher.Lookup, visualStore: store));

        Assert.True(content.Succeeded, string.Join("\n", content.Diagnostics));
        var definition = content.Get<ObjectDefinition>(Root);
        var weapon = definition.WeaponSets.Values.Single().Slots[0].Weapon.Value;
        Assert.Equal(9.6f, weapon.AttackRange, 3);
        Assert.Equal(75f, ((ActiveBodyModuleData)definition.Behaviors[UnitBodyMovementMaterializer.DefaultBodyTag].Data).MaxHealth);
        Assert.Equal(1.725f * 12 / 30, definition.LocomotorSets[LocomotorSetType.Normal].Locomotors[0].Value.MaxSpeed, 4);

        var result = publisher.Publish(content, new AcceptAllVisuals());
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.NotNull(store.GetSourceContent(ContentSource.Ee2).ObjectDefinitions.GetByName("HeavyInfantry1UpgradeEpoch1"));
        Assert.Null(store.ObjectDefinitions.GetByName("HeavyInfantry1UpgradeEpoch1"));
    }

    private sealed class AcceptAllVisuals : IUnitVisualInstaller
    {
        public IReadOnlyList<ImportDiagnostic> Verify(IReadOnlyList<VisualAssetRequest> requests) => [];

        public void Install(IReadOnlyList<VisualAssetRequest> requests, AssetStore store)
        {
        }
    }
}
