using System.Collections.Generic;
using Panopticon.Content.Importing.Units;
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
        Assert.Equal(15u, weapon.CoolDownDelayBetweenShots.Min.Value);
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
}
