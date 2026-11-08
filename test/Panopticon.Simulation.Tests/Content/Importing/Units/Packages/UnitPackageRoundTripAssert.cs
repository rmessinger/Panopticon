#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

/// <summary>Field-by-field equality asserts between a resolved closure and a package re-read.</summary>
public static class UnitPackageRoundTripAssert
{
    public static void ClosuresRoundTrip(UnitImportResolution expected, UnitContentSet actual, ILegacyNativeTable? natives)
    {
        Assert.Empty(actual.Diagnostics);
        Assert.Equal(expected.Source.Id, actual.Source.Id);
        Assert.Equal(expected.Source.CaseSensitiveNames, actual.Source.CaseSensitiveNames);
        Assert.Equal(expected.Closure.Count, actual.Records.Count);
        Assert.Equal(expected.Roots, actual.Roots);

        foreach (var expectedRecord in expected.Closure)
        {
            var actualRecord = actual.Records.Single(record =>
                record.Id.Kind == expectedRecord.Id.Kind
                && expected.Source.NameComparer.Equals(record.Id.Name, expectedRecord.Id.Name));

            RecordEquals(expectedRecord, actualRecord, natives);
        }
    }

    private static void RecordEquals(SourceRecord expected, SourceRecord actual, ILegacyNativeTable? natives)
    {
        Assert.True(expected.Id.Equals(actual.Id), $"Record id mismatch for {expected.Id}.");
        Assert.Equal(expected.Provenance, actual.Provenance);
        Assert.Equal(expected.Parent, actual.Parent);
        Assert.Equal(expected.Legacy is not null, actual.Legacy is not null);

        if (expected.Legacy is not null)
        {
            AssertLegacyReissued(expected, actual, natives);
        }

        Assert.Equal(expected.Unclassified.Count, actual.Unclassified.Count);

        for (var i = 0; i < expected.Unclassified.Count; i++)
        {
            UnclassifiedEquals(expected.Unclassified[i], actual.Unclassified[i]);
        }

        switch (expected)
        {
            case UnitRecord unit:
                UnitEquals(unit, Assert.IsType<UnitRecord>(actual), natives);
                break;
            case WeaponRecord weapon:
                WeaponEquals(weapon, Assert.IsType<WeaponRecord>(actual));
                break;
            case ArmorRecord armor:
                ArmorEquals(armor, Assert.IsType<ArmorRecord>(actual));
                break;
            case LocomotorRecord locomotor:
                LocomotorEquals(locomotor, Assert.IsType<LocomotorRecord>(actual));
                break;
            case UpgradeRecord upgrade:
                UpgradeEquals(upgrade, Assert.IsType<UpgradeRecord>(actual));
                break;
            case AbilityRecord ability:
                AbilityEquals(ability, Assert.IsType<AbilityRecord>(actual));
                break;
            case ProjectileRecord projectile:
                ProjectileEquals(projectile, Assert.IsType<ProjectileRecord>(actual));
                break;
            case VisualStateRecord visual:
                VisualStateEquals(visual, Assert.IsType<VisualStateRecord>(actual));
                break;
        }
    }

    private static void AssertLegacyReissued(SourceRecord expected, SourceRecord actual, ILegacyNativeTable? natives)
    {
        Assert.NotNull(natives);
        Assert.Equal(ContentSource.Generals.Id, actual.Legacy!.Owner.Id);

        // Handles are session-local: the re-read record addresses the reader's own rebuilt table,
        // never the writer's original handle value.
        var native = natives!.Get(actual.Legacy.Handle);
        Assert.NotNull(native);
    }

    private static void UnclassifiedEquals(UnclassifiedField expected, UnclassifiedField actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.AffectsGameplay, actual.AffectsGameplay);
        Assert.Equal(expected.ExcludedByPolicy, actual.ExcludedByPolicy);
        Assert.Equal(expected.RawValue, actual.RawValue);
        Assert.Equal(expected.RawBlock, actual.RawBlock);
        Assert.Equal(expected.Provenance, actual.Provenance);
    }

    private static void UnitEquals(UnitRecord expected, UnitRecord actual, ILegacyNativeTable? natives)
    {
        AuthoredStringEquals("DisplayName", expected.DisplayName, actual.DisplayName);
        AuthoredQuantityEquals("MaxHealth", expected.MaxHealth, actual.MaxHealth);
        AuthoredQuantityEquals("InitialHealth", expected.InitialHealth, actual.InitialHealth);
        AuthoredQuantityEquals("GeometryMajorRadius", expected.GeometryMajorRadius, actual.GeometryMajorRadius);
        AuthoredQuantityEquals("GeometryHeight", expected.GeometryHeight, actual.GeometryHeight);
        AuthoredQuantityEquals("VisionRange", expected.VisionRange, actual.VisionRange);
        AuthoredQuantityEquals("DeathDuration", expected.DeathDuration, actual.DeathDuration);
        StringListEquals("KindOf", expected.KindOf, actual.KindOf);
        ContentIdListEquals("Locomotors", expected.Locomotors, actual.Locomotors);
        ContentIdListEquals("Weapons", expected.Weapons, actual.Weapons);
        AuthoredContentIdEquals("Armor", expected.Armor, actual.Armor);
        AuthoredQuantityEquals("BuildCost", expected.BuildCost, actual.BuildCost);
        AuthoredQuantityEquals("BuildTime", expected.BuildTime, actual.BuildTime);
        ContentIdListEquals("Prerequisites", expected.Prerequisites, actual.Prerequisites);
        ContentIdListEquals("Produces", expected.Produces, actual.Produces);
        ContentIdListEquals("Upgrades", expected.Upgrades, actual.Upgrades);
        AuthoredContentIdEquals("Visual", expected.Visual, actual.Visual);
        ContentIdListEquals("Presentation", expected.Presentation, actual.Presentation);
        ModulesEquals(expected, actual, natives);
    }

    private static void WeaponEquals(WeaponRecord expected, WeaponRecord actual)
    {
        AuthoredQuantityEquals("PrimaryDamage", expected.PrimaryDamage, actual.PrimaryDamage);
        AuthoredQuantityEquals("PrimaryDamageRadius", expected.PrimaryDamageRadius, actual.PrimaryDamageRadius);
        AuthoredStringEquals("DamageType", expected.DamageType, actual.DamageType);
        AuthoredQuantityEquals("AttackRange", expected.AttackRange, actual.AttackRange);
        AuthoredQuantityEquals("MinimumAttackRange", expected.MinimumAttackRange, actual.MinimumAttackRange);
        AuthoredQuantityEquals("ReloadTime", expected.ReloadTime, actual.ReloadTime);
        AuthoredQuantityEquals("PreAttackDelay", expected.PreAttackDelay, actual.PreAttackDelay);
        AuthoredIntEquals("ClipSize", expected.ClipSize, actual.ClipSize);
        AuthoredQuantityEquals("ClipReloadTime", expected.ClipReloadTime, actual.ClipReloadTime);
        StringListEquals("TargetCategories", expected.TargetCategories, actual.TargetCategories);
        AuthoredContentIdEquals("Projectile", expected.Projectile, actual.Projectile);
        ContentIdListEquals("Presentation", expected.Presentation, actual.Presentation);
    }

    private static void ArmorEquals(ArmorRecord expected, ArmorRecord actual)
    {
        Assert.Equal(expected.Multipliers.Count, actual.Multipliers.Count);

        foreach (var (damageType, multiplier) in expected.Multipliers)
        {
            Assert.True(actual.Multipliers.TryGetValue(damageType, out var actualMultiplier), $"Missing multiplier '{damageType}'.");
            AuthoredDoubleEquals($"Multiplier {damageType}", multiplier, actualMultiplier);
        }

        AuthoredDoubleEquals("DefaultMultiplier", expected.DefaultMultiplier, actual.DefaultMultiplier);
    }

    private static void LocomotorEquals(LocomotorRecord expected, LocomotorRecord actual)
    {
        AuthoredQuantityEquals("Speed", expected.Speed, actual.Speed);
        AuthoredQuantityEquals("TurnRate", expected.TurnRate, actual.TurnRate);
        AuthoredQuantityEquals("Acceleration", expected.Acceleration, actual.Acceleration);
        AuthoredQuantityEquals("Braking", expected.Braking, actual.Braking);
        AuthoredQuantityEquals("PreferredHeight", expected.PreferredHeight, actual.PreferredHeight);
        AuthoredBoolEquals("CanHover", expected.CanHover, actual.CanHover);
        AuthoredStringEquals("Appearance", expected.Appearance, actual.Appearance);
        StringListEquals("Surfaces", expected.Surfaces, actual.Surfaces);
    }

    private static void UpgradeEquals(UpgradeRecord expected, UpgradeRecord actual)
    {
        AuthoredStringEquals("DisplayName", expected.DisplayName, actual.DisplayName);
        AuthoredQuantityEquals("BuildCost", expected.BuildCost, actual.BuildCost);
        AuthoredQuantityEquals("BuildTime", expected.BuildTime, actual.BuildTime);
        AuthoredStringEquals("Scope", expected.Scope, actual.Scope);
        ContentIdListEquals("Prerequisites", expected.Prerequisites, actual.Prerequisites);
    }

    private static void AbilityEquals(AbilityRecord expected, AbilityRecord actual)
    {
        AuthoredStringEquals("Family", expected.Family, actual.Family);
        AuthoredQuantityEquals("Range", expected.Range, actual.Range);
        AuthoredQuantityEquals("Cooldown", expected.Cooldown, actual.Cooldown);
        AuthoredContentIdEquals("Weapon", expected.Weapon, actual.Weapon);
        AuthoredContentIdEquals("RequiredUpgrade", expected.RequiredUpgrade, actual.RequiredUpgrade);
    }

    private static void ProjectileEquals(ProjectileRecord expected, ProjectileRecord actual)
    {
        AuthoredQuantityEquals("Speed", expected.Speed, actual.Speed);
        AuthoredContentIdEquals("Visual", expected.Visual, actual.Visual);
    }

    private static void VisualStateEquals(VisualStateRecord expected, VisualStateRecord actual)
    {
        AuthoredStringEquals("ModelKey", expected.ModelKey, actual.ModelKey);
        Assert.Equal(expected.StateAnimations.Count, actual.StateAnimations.Count);

        foreach (var (state, animation) in expected.StateAnimations)
        {
            Assert.True(actual.StateAnimations.TryGetValue(state, out var actualAnimation), $"Missing state animation '{state}'.");
            AuthoredStringEquals($"StateAnimation {state}", animation, actualAnimation);
        }
    }

    private static void ModulesEquals(UnitRecord expected, UnitRecord actual, ILegacyNativeTable? natives)
    {
        Assert.Equal(expected.Modules.Count, actual.Modules.Count);

        for (var i = 0; i < expected.Modules.Count; i++)
        {
            var expectedEdit = expected.Modules[i];
            var actualEdit = actual.Modules[i];

            Assert.Equal(expectedEdit.Operation, actualEdit.Operation);
            Assert.Equal(expectedEdit.Tag, actualEdit.Tag);
            Assert.Equal(expectedEdit.Module is not null, actualEdit.Module is not null);

            if (expectedEdit.Module is not { } expectedModule)
            {
                continue;
            }

            var actualModule = actualEdit.Module!;

            Assert.Equal(expectedModule.Tag, actualModule.Tag);
            Assert.Equal(expectedModule.Family, actualModule.Family);
            Assert.Equal(expectedModule.Kinds, actualModule.Kinds);
            Assert.Equal(expectedModule.InheritanceMode, actualModule.InheritanceMode);
            Assert.Equal(expectedModule.ExclusiveGroup, actualModule.ExclusiveGroup);
            Assert.Equal(expectedModule.Inherited, actualModule.Inherited);
            Assert.Equal(expectedModule.Ability, actualModule.Ability);
            Assert.Equal(expectedModule.Legacy is not null, actualModule.Legacy is not null);

            if (actualModule.Legacy is not null)
            {
                Assert.NotNull(natives);
                Assert.NotNull(natives!.Get(actualModule.Legacy.Handle));
            }
        }
    }

    private static void AuthoredQuantityEquals(string field, Authored<Quantity> expected, Authored<Quantity> actual)
    {
        Assert.Equal(expected.IsPresent, actual.IsPresent);

        if (expected.IsPresent)
        {
            Assert.Equal(expected.Value, actual.Value);
            OriginEquals(field, expected.Origin, actual.Origin);
        }
    }

    private static void AuthoredStringEquals(string field, Authored<string> expected, Authored<string> actual)
    {
        Assert.Equal(expected.IsPresent, actual.IsPresent);

        if (expected.IsPresent)
        {
            Assert.Equal(expected.Value, actual.Value);
            OriginEquals(field, expected.Origin, actual.Origin);
        }
    }

    private static void AuthoredIntEquals(string field, Authored<int> expected, Authored<int> actual)
    {
        Assert.Equal(expected.IsPresent, actual.IsPresent);

        if (expected.IsPresent)
        {
            Assert.Equal(expected.Value, actual.Value);
            OriginEquals(field, expected.Origin, actual.Origin);
        }
    }

    private static void AuthoredDoubleEquals(string field, Authored<double> expected, Authored<double> actual)
    {
        Assert.Equal(expected.IsPresent, actual.IsPresent);

        if (expected.IsPresent)
        {
            Assert.Equal(expected.Value, actual.Value);
            OriginEquals(field, expected.Origin, actual.Origin);
        }
    }

    private static void AuthoredBoolEquals(string field, Authored<bool> expected, Authored<bool> actual)
    {
        Assert.Equal(expected.IsPresent, actual.IsPresent);

        if (expected.IsPresent)
        {
            Assert.Equal(expected.Value, actual.Value);
            OriginEquals(field, expected.Origin, actual.Origin);
        }
    }

    private static void AuthoredContentIdEquals(string field, Authored<ContentId> expected, Authored<ContentId> actual)
    {
        Assert.Equal(expected.IsPresent, actual.IsPresent);

        if (expected.IsPresent)
        {
            Assert.True(expected.Value.Equals(actual.Value), $"{field} value mismatch: {expected.Value} vs {actual.Value}.");
            OriginEquals(field, expected.Origin, actual.Origin);
        }
    }

    private static void OriginEquals(string field, ContentId? expected, ContentId? actual)
    {
        if (expected is null)
        {
            Assert.True(actual is null, $"{field} origin should be null but was {actual}.");
            return;
        }

        Assert.True(actual is not null, $"{field} origin {expected} was lost in the round trip.");
        Assert.True(expected.Value.Equals(actual.Value), $"{field} origin mismatch: {expected} vs {actual}.");
    }

    private static void StringListEquals(string field, ListOverride<string> expected, ListOverride<string> actual)
    {
        Assert.Equal(expected.Mode, actual.Mode);
        Assert.Equal(expected.Items, actual.Items);
        Assert.Equal(expected.Removals, actual.Removals);
    }

    private static void ContentIdListEquals(string field, ListOverride<ContentId> expected, ListOverride<ContentId> actual)
    {
        Assert.Equal(expected.Mode, actual.Mode);
        Assert.Equal(expected.Items.Count, actual.Items.Count);

        for (var i = 0; i < expected.Items.Count; i++)
        {
            Assert.True(expected.Items[i].Equals(actual.Items[i]), $"{field} item {i} mismatch: {expected.Items[i]} vs {actual.Items[i]}.");
        }

        Assert.Equal(expected.Removals.Count, actual.Removals.Count);

        for (var i = 0; i < expected.Removals.Count; i++)
        {
            Assert.True(expected.Removals[i].Equals(actual.Removals[i]), $"{field} removal {i} mismatch.");
        }
    }
}
