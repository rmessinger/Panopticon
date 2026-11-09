using System.Collections.Generic;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.MaterializationFixtures;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitCombatMaterializerTests
{
    private static WeaponRecord Rifle() => Weapon("Rifle") with
    {
        PrimaryDamage = Authored.Of(Hp(12)),
        PrimaryDamageRadius = Authored.Of(Distance(2)),
        DamageType = Authored.Of("SMALL_ARMS"),
        AttackRange = Authored.Of(Distance(150)),
        MinimumAttackRange = Authored.Of(Distance(10)),
        ReloadTime = Authored.Of(new Quantity(500, QuantityUnit.Milliseconds)),
        ClipSize = Authored.Of(6),
        ClipReloadTime = Authored.Of(Seconds(2)),
        TargetCategories = ListOverride<string>.Replace(["AntiGround", "AntiAirborneInfantry"]),
    };

    [Fact]
    public void WeaponValuesMatchAuthoredSource()
    {
        var soldier = (UnitRecord)ValidSoldier().Records[0];
        var content = Materialize(Set(soldier, Rifle(), Armor("Flesh"), Locomotor("Feet")));
        var weapon = content.Get<WeaponTemplate>(Id(ContentKind.Weapon, "Rifle"));
        var nugget = Assert.IsType<DamageNugget>(Assert.Single(weapon.Nuggets));

        Assert.Equal(150f, weapon.AttackRange);
        Assert.Equal(10f, weapon.MinimumAttackRange);
        Assert.Equal(LogicFrameSpan.FromMilliseconds(500, SageGame.CncGenerals).Value, weapon.CoolDownDelayBetweenShots.Min.Value);
        Assert.Equal(6, weapon.ClipSize);
        Assert.Equal(60u, weapon.ClipReloadTime.Min.Value);
        Assert.Equal(12f, nugget.Damage);
        Assert.Equal(2f, nugget.Radius);
        Assert.Equal(DamageType.SmallArms, nugget.DamageType);
        Assert.Equal(WeaponAntiFlags.AntiGround | WeaponAntiFlags.AntiAirborneInfantry, weapon.AntiMask);
        Assert.False(weapon.DeliversDamageViaProjectile);
    }

    [Fact]
    public void ArmorAdjustsDamagePerType()
    {
        var armor = Armor("Plate") with
        {
            DefaultMultiplier = Authored.Of(0.8),
            Multipliers = new Dictionary<string, Authored<double>> { ["SMALL_ARMS"] = Authored.Of(0.25), ["FLAME"] = Authored.Of(0.0) },
        };
        var unit = Unit("Tank") with { MaxHealth = Authored.Of(Hp(400)), Armor = Authored.Of(armor.Id) };

        var native = Materialize(Set(unit, armor)).Get<ArmorTemplate>(armor.Id);

        Assert.Equal(25f, native.AdjustDamage(DamageType.SmallArms, 100f), 3);
        Assert.Equal(0f, native.AdjustDamage(DamageType.Flame, 100f), 3);
        Assert.Equal(80f, native.AdjustDamage(DamageType.Explosion, 100f), 3);
        Assert.Equal(100f, native.AdjustDamage(DamageType.Unresistable, 100f), 3);
    }

    [Fact]
    public void UnitSetsBindStagedWeaponAndArmor()
    {
        var content = Materialize(ValidSoldier());
        var definition = content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Soldier"));

        var set = Assert.Single(definition.WeaponSets.Values);
        Assert.Same(content.Get<WeaponTemplate>(Id(ContentKind.Weapon, "Rifle")), set.Slots[0].Weapon.Value);
        Assert.Same(content.Get<ArmorTemplate>(Id(ContentKind.Armor, "Flesh")), Assert.Single(definition.ArmorSets).Armor.Value);
    }

    [Fact]
    public void ProjectileSpeedSetsTravelTimeWithoutProjectileObject()
    {
        var projectile = new ProjectileRecord(Id(ContentKind.Projectile, "Arrow"), At(5)) { Speed = Authored.Of(Speed(300)) };
        var bow = Weapon("Bow") with { Projectile = Authored.Of(projectile.Id) };
        var unit = Unit("Archer") with { MaxHealth = Authored.Of(Hp(50)), Weapons = ListOverride<ContentId>.Replace([bow.Id]) };

        var weapon = Materialize(Set(unit, bow, projectile)).Get<WeaponTemplate>(bow.Id);

        Assert.Equal(10f, weapon.WeaponSpeed, 4);
        Assert.False(weapon.DeliversDamageViaProjectile);
    }

    [Fact]
    public void UnknownDamageTypeFails()
    {
        var unit = Unit("U") with { MaxHealth = Authored.Of(Hp(5)), Weapons = ListOverride<ContentId>.Replace([Id(ContentKind.Weapon, "W")]) };

        Assert.False(Materialize(Set(unit, Weapon("W") with { DamageType = Authored.Of("PIERCE_MAGIC") })).Succeeded);
    }

    [Fact]
    public void TooManyWeaponsFail()
    {
        var weapons = new List<SourceRecord>();
        var ids = new List<ContentId>();
        for (var i = 0; i < WeaponTemplateSet.NumWeaponSlots + 1; i++)
        {
            weapons.Add(Weapon($"W{i}"));
            ids.Add(Id(ContentKind.Weapon, $"W{i}"));
        }

        var unit = Unit("U") with { MaxHealth = Authored.Of(Hp(5)), Weapons = ListOverride<ContentId>.Replace(ids) };

        Assert.False(Materialize(Set(unit, weapons.ToArray())).Succeeded);
    }

    [Fact]
    public void ProjectileSpeedWithMetersPerSecondConvertsToMetersPerFrame()
    {
        var projectile = new ProjectileRecord(Id(ContentKind.Projectile, "Laser"), At(6))
        {
            Speed = Authored.Of(Quantity.FromMetersPerSecond(60)),
        };
        var gun = Weapon("Gun") with { Projectile = Authored.Of(projectile.Id) };
        var unit = Unit("Shooter") with { MaxHealth = Authored.Of(Hp(100)), Weapons = ListOverride<ContentId>.Replace([gun.Id]) };

        var weapon = Materialize(Set(unit, gun, projectile)).Get<WeaponTemplate>(gun.Id);

        Assert.Equal(2.0f, weapon.WeaponSpeed, 4);
    }

    [Fact]
    public void ProjectileSpeedWithSourceDistancePerSecondConvertsViaPolicy()
    {
        var projectile = new ProjectileRecord(Id(ContentKind.Projectile, "Missile"), At(7))
        {
            Speed = Authored.Of(new Quantity(1.725, QuantityUnit.SourceDistancePerSecond)),
        };
        var launcher = Weapon("Launcher") with { Projectile = Authored.Of(projectile.Id) };
        var unit = Unit("Carrier") with { MaxHealth = Authored.Of(Hp(100)), Weapons = ListOverride<ContentId>.Replace([launcher.Id]) };

        var resolution = UnitImportResolution.Resolve(Set(unit, launcher, projectile), Registry);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new Panopticon.Content.Importing.Units.Materialization.UnitMaterializationContext(Ee2MixedPolicy));

        var weapon = content.Get<WeaponTemplate>(launcher.Id);
        // 1.725 * (12 * 0.3048 = 3.6576) / 30 = 0.210312 m per frame
        Assert.Equal(0.210312f, weapon.WeaponSpeed, 5);
    }

    [Fact]
    public void WeaponRangeWithMetersMaterializesDirectly()
    {
        var gun = Weapon("Sniper") with
        {
            AttackRange = Authored.Of(Quantity.FromMeters(30.48)),
            MinimumAttackRange = Authored.Of(Quantity.FromMeters(3.048)),
            PrimaryDamageRadius = Authored.Of(Quantity.FromMeters(1.524)),
        };
        var unit = Unit("SniperUnit") with { MaxHealth = Authored.Of(Hp(50)), Weapons = ListOverride<ContentId>.Replace([gun.Id]) };

        var content = Materialize(Set(unit, gun));
        var weapon = content.Get<WeaponTemplate>(gun.Id);
        var nugget = Assert.IsType<DamageNugget>(Assert.Single(weapon.Nuggets));

        Assert.Equal(30.48f, weapon.AttackRange, 4);
        Assert.Equal(3.048f, weapon.MinimumAttackRange, 4);
        Assert.Equal(1.524f, nugget.Radius, 4);
    }

    [Fact]
    public void WeaponRangeWithSourceDistanceConvertsViaPolicy()
    {
        var bomb = Weapon("Bomb") with
        {
            AttackRange = Authored.Of(Quantity.FromSourceDistance(0.8)),
            PrimaryDamageRadius = Authored.Of(Quantity.FromSourceDistance(0.0625)),
        };
        var bomber = Unit("Bomber") with { MaxHealth = Authored.Of(Hp(100)), Weapons = ListOverride<ContentId>.Replace([bomb.Id]) };

        var resolution = UnitImportResolution.Resolve(Set(bomber, bomb), Registry);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new Panopticon.Content.Importing.Units.Materialization.UnitMaterializationContext(Ee2MixedPolicy));

        var weapon = content.Get<WeaponTemplate>(bomb.Id);
        var nugget = Assert.IsType<DamageNugget>(Assert.Single(weapon.Nuggets));

        // 0.8 * 3.6576 = 2.92608 m; 0.0625 * 3.6576 = 0.2286 m
        Assert.Equal(2.92608f, weapon.AttackRange, 5);
        Assert.Equal(0.2286f, nugget.Radius, 5);
    }

    [Fact]
    public void SourceDistanceWithoutDeclaredScaleFails()
    {
        var bomb = Weapon("UnscaledBomb") with
        {
            AttackRange = Authored.Of(Quantity.FromSourceDistance(1.0)),
        };
        var unit = Unit("Unit") with { MaxHealth = Authored.Of(Hp(50)), Weapons = ListOverride<ContentId>.Replace([bomb.Id]) };

        var resolution = UnitImportResolution.Resolve(Set(unit, bomb), Registry);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new Panopticon.Content.Importing.Units.Materialization.UnitMaterializationContext(MetricGeneralsPolicy));

        Assert.False(content.Succeeded);
        Assert.Contains(content.Diagnostics, d => d.Code == DiagnosticCodes.MaterializationFailed && d.Message.Contains("meters-per-source-distance"));
    }
}
