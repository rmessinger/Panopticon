#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

/// <summary>
/// Compares materialized runtime templates field by field between the INI path and package path.
/// Handles are never compared (D-H4-3: fresh handles, template equality only).
/// </summary>
public static class UnitPackageTemplateComparer
{
    public static int CompareRosterTemplates(
        AssetStore expectedStore,
        AssetStore actualStore,
        IReadOnlyList<string> roster)
    {
        ArgumentNullException.ThrowIfNull(expectedStore);
        ArgumentNullException.ThrowIfNull(actualStore);
        ArgumentNullException.ThrowIfNull(roster);

        var count = 0;
        var checkedWeapons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedArmors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedLocomotors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedUpgrades = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var unitName in roster)
        {
            var expectedDef = expectedStore.ObjectDefinitions.GetByName(unitName);
            var actualDef = actualStore.ObjectDefinitions.GetByName(unitName);
            Assert.NotNull(expectedDef);
            Assert.NotNull(actualDef);

            Assert.Equal(expectedDef!.Name, actualDef!.Name);
            count++;
            Assert.Equal(expectedDef.KindOf, actualDef.KindOf);
            count++;
            Assert.Equal(expectedDef.BuildCost, actualDef.BuildCost);
            count++;
            Assert.Equal(expectedDef.BuildTime.Value, actualDef.BuildTime.Value);
            count++;
            Assert.Equal(expectedDef.RefundValue, actualDef.RefundValue);
            count++;
            Assert.Equal(expectedDef.VisionRange, actualDef.VisionRange);
            count++;
            Assert.Equal(expectedDef.ShroudClearingRange, actualDef.ShroudClearingRange);
            count++;
            Assert.Equal(expectedDef.Geometry.MajorRadius, actualDef.Geometry.MajorRadius);
            count++;
            Assert.Equal(expectedDef.Geometry.Shapes[0].Height, actualDef.Geometry.Shapes[0].Height);
            count++;

            // Behaviors
            Assert.Equal(expectedDef.Behaviors.Count, actualDef.Behaviors.Count);
            count++;
            foreach (var (key, expectedMod) in expectedDef.Behaviors)
            {
                Assert.True(actualDef.Behaviors.TryGetValue(key, out var actualMod), $"Missing module '{key}' on {unitName}");
                count++;
                Assert.Equal(expectedMod.Data.GetType(), actualMod!.Data.GetType());
                count++;
                Assert.Equal(expectedMod.InheritanceMode, actualMod.InheritanceMode);
                count++;
                Assert.Equal(expectedMod.Data.ModuleKinds, actualMod.Data.ModuleKinds);
                count++;

                if (expectedMod.Data is ActiveBodyModuleData expectedBody && actualMod.Data is ActiveBodyModuleData actualBody)
                {
                    Assert.Equal(expectedBody.MaxHealth, actualBody.MaxHealth);
                    count++;
                    Assert.Equal(expectedBody.InitialHealth, actualBody.InitialHealth);
                    count++;
                }
            }

            // Weapon sets
            Assert.Equal(expectedDef.WeaponSets.Count, actualDef.WeaponSets.Count);
            count++;
            foreach (var (cond, expectedSet) in expectedDef.WeaponSets)
            {
                Assert.True(actualDef.WeaponSets.TryGetValue(cond, out var actualSet), $"Missing weapon set condition '{cond}' on {unitName}");
                count++;
                Assert.Equal(expectedSet.Slots.Length, actualSet!.Slots.Length);
                count++;
                for (var s = 0; s < expectedSet.Slots.Length; s++)
                {
                    var expSlot = expectedSet.Slots[s];
                    var actSlot = actualSet.Slots[s];
                    Assert.Equal(expSlot?.Weapon?.Name, actSlot?.Weapon?.Name);
                    count++;
                    if (expSlot?.Weapon?.Name is { } wName && checkedWeapons.Add(wName))
                    {
                        var expW = expectedStore.WeaponTemplates.GetByName(wName);
                        var actW = actualStore.WeaponTemplates.GetByName(wName);
                        if (expW is not null && actW is not null)
                        {
                            count += CompareWeapon(expW, actW);
                        }
                    }
                }
            }

            // Armor sets
            Assert.Equal(expectedDef.ArmorSets.Count, actualDef.ArmorSets.Count);
            count++;
            for (var a = 0; a < expectedDef.ArmorSets.Count; a++)
            {
                var expArmorSet = expectedDef.ArmorSets[a];
                var actArmorSet = actualDef.ArmorSets[a];
                Assert.Equal(expArmorSet.Conditions, actArmorSet.Conditions);
                count++;
                Assert.Equal(expArmorSet.Armor?.Name, actArmorSet.Armor?.Name);
                count++;
                if (expArmorSet.Armor?.Name is { } aName && checkedArmors.Add(aName))
                {
                    var expA = expectedStore.ArmorTemplates.GetByName(aName);
                    var actA = actualStore.ArmorTemplates.GetByName(aName);
                    if (expA is not null && actA is not null)
                    {
                        count += CompareArmor(expA, actA);
                    }
                }
            }

            // Locomotors
            Assert.Equal(expectedDef.LocomotorSets.Count, actualDef.LocomotorSets.Count);
            count++;
            foreach (var (lType, expLocoSet) in expectedDef.LocomotorSets)
            {
                Assert.True(actualDef.LocomotorSets.TryGetValue(lType, out var actLocoSet), $"Missing locomotor set '{lType}' on {unitName}");
                count++;
                Assert.Equal(expLocoSet.Speed, actLocoSet!.Speed);
                count++;
                Assert.Equal(expLocoSet.Locomotors.Length, actLocoSet.Locomotors.Length);
                count++;
                for (var l = 0; l < expLocoSet.Locomotors.Length; l++)
                {
                    var expLocoRef = expLocoSet.Locomotors[l];
                    var actLocoRef = actLocoSet.Locomotors[l];
                    Assert.Equal(expLocoRef.Name, actLocoRef.Name);
                    count++;
                    if (expLocoRef.Name is { } lName && checkedLocomotors.Add(lName))
                    {
                        var expL = expectedStore.LocomotorTemplates.GetByName(lName);
                        var actL = actualStore.LocomotorTemplates.GetByName(lName);
                        if (expL is not null && actL is not null)
                        {
                            count += CompareLocomotor(expL, actL);
                        }
                    }
                }
            }
        }

        foreach (var expUp in expectedStore.Upgrades)
        {
            if (checkedUpgrades.Add(expUp.Name))
            {
                var actUp = actualStore.Upgrades.GetByName(expUp.Name);
                if (actUp is not null)
                {
                    count += CompareUpgrade(expUp, actUp);
                }
            }
        }

        return count;
    }

    private static int CompareWeapon(WeaponTemplate expected, WeaponTemplate actual)
    {
        var count = 0;
        Assert.Equal(expected.Name, actual.Name);
        count++;
        Assert.Equal(expected.AttackRange, actual.AttackRange);
        count++;
        Assert.Equal(expected.MinimumAttackRange, actual.MinimumAttackRange);
        count++;
        Assert.Equal(expected.CoolDownDelayBetweenShots.Min.Value, actual.CoolDownDelayBetweenShots.Min.Value);
        count++;
        Assert.Equal(expected.CoolDownDelayBetweenShots.Max.Value, actual.CoolDownDelayBetweenShots.Max.Value);
        count++;
        Assert.Equal(expected.ClipSize, actual.ClipSize);
        count++;
        Assert.Equal(expected.ClipReloadTime.Min.Value, actual.ClipReloadTime.Min.Value);
        count++;
        Assert.Equal(expected.ClipReloadTime.Max.Value, actual.ClipReloadTime.Max.Value);
        count++;
        Assert.Equal(expected.AutoReloadWhenIdle.Min.Value, actual.AutoReloadWhenIdle.Min.Value);
        count++;
        Assert.Equal(expected.AutoReloadWhenIdle.Max.Value, actual.AutoReloadWhenIdle.Max.Value);
        count++;
        Assert.Equal(expected.ContinuousFireOne, actual.ContinuousFireOne);
        count++;
        Assert.Equal(expected.WeaponSpeed, actual.WeaponSpeed);
        count++;
        Assert.Equal(expected.WeaponRecoil, actual.WeaponRecoil);
        count++;
        Assert.Equal(expected.LeechRangeWeapon, actual.LeechRangeWeapon);
        count++;

        var expNuggets = expected.Nuggets.OfType<DamageNugget>().ToList();
        var actNuggets = actual.Nuggets.OfType<DamageNugget>().ToList();
        Assert.Equal(expNuggets.Count, actNuggets.Count);
        count++;
        for (var i = 0; i < expNuggets.Count; i++)
        {
            Assert.Equal(expNuggets[i].Damage, actNuggets[i].Damage);
            count++;
            Assert.Equal(expNuggets[i].Radius, actNuggets[i].Radius);
            count++;
            Assert.Equal(expNuggets[i].DamageType, actNuggets[i].DamageType);
            count++;
        }

        return count;
    }

    private static int CompareArmor(ArmorTemplate expected, ArmorTemplate actual)
    {
        var count = 0;
        Assert.Equal(expected.Name, actual.Name);
        count++;
        Assert.Equal(expected.DamageScalar, actual.DamageScalar);
        count++;
        Assert.Equal(expected.FlankedPenalty, actual.FlankedPenalty);
        count++;
        Assert.Equal(expected.Values.Length, actual.Values.Length);
        count++;
        for (var i = 0; i < expected.Values.Length; i++)
        {
            Assert.Equal(expected.Values[i], actual.Values[i]);
            count++;
        }

        return count;
    }

    private static int CompareLocomotor(LocomotorTemplate expected, LocomotorTemplate actual)
    {
        var count = 0;
        Assert.Equal(expected.Name, actual.Name);
        count++;
        Assert.Equal(expected.Surfaces, actual.Surfaces);
        count++;
        Assert.Equal(expected.MaxSpeed, actual.MaxSpeed);
        count++;
        Assert.Equal(expected.MaxSpeedDamaged, actual.MaxSpeedDamaged);
        count++;
        Assert.Equal(expected.MinSpeed, actual.MinSpeed);
        count++;
        Assert.Equal(expected.MaxTurnRate, actual.MaxTurnRate);
        count++;
        Assert.Equal(expected.MaxTurnRateDamaged, actual.MaxTurnRateDamaged);
        count++;
        Assert.Equal(expected.Acceleration, actual.Acceleration);
        count++;
        Assert.Equal(expected.Braking, actual.Braking);
        count++;
        Assert.Equal(expected.Lift, actual.Lift);
        count++;
        Assert.Equal(expected.PreferredHeight, actual.PreferredHeight);
        count++;
        Assert.Equal(expected.Appearance, actual.Appearance);
        count++;
        Assert.Equal(expected.BehaviorZ, actual.BehaviorZ);
        count++;
        Assert.Equal(expected.AllowAirborneMotiveForce, actual.AllowAirborneMotiveForce);
        count++;
        return count;
    }

    private static int CompareUpgrade(UpgradeTemplate expected, UpgradeTemplate actual)
    {
        var count = 0;
        Assert.Equal(expected.Name, actual.Name);
        count++;
        Assert.Equal(expected.Type, actual.Type);
        count++;
        Assert.Equal(expected.BuildCost, actual.BuildCost);
        count++;
        Assert.Equal(expected.BuildTime, actual.BuildTime);
        count++;
        return count;
    }
}
