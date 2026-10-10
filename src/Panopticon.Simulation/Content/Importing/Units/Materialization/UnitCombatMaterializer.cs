#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Card WI-H2a-08: native weapons, armor, and the unit's default weapon and armor sets. A typed
/// projectile weapon becomes a direct-damage weapon whose travel time follows the projectile speed;
/// projectile objects are not materialized for typed sources.
/// </summary>
internal static class UnitCombatMaterializer
{
    public static WeaponTemplate Materialize(WeaponRecord record, UnitImportResolution resolution, UnitMaterializationContext context)
    {
        var weapon = new WeaponTemplate();
        weapon.AssignImportedName("Weapon", record.Id.Name);

        weapon.AttackRange = (float)UnitBodyMovementMaterializer.ConvertDistance(record, nameof(record.AttackRange), record.AttackRange.Value, context);

        if (record.MinimumAttackRange.IsPresent)
        {
            weapon.MinimumAttackRange = (float)UnitBodyMovementMaterializer.ConvertDistance(record, nameof(record.MinimumAttackRange), record.MinimumAttackRange.Value, context);
        }

        weapon.CoolDownDelayBetweenShots = new RangeDuration(ConvertFrames(record, nameof(record.ReloadTime), record.ReloadTime.Value, context));

        if (record.PreAttackDelay.IsPresent)
        {
            weapon.PreAttackDelay = new RangeDuration(ConvertFrames(record, nameof(record.PreAttackDelay), record.PreAttackDelay.Value, context));
        }

        if (record.ClipSize.IsPresent)
        {
            weapon.ClipSize = record.ClipSize.Value;
        }

        if (record.ClipReloadTime.IsPresent)
        {
            weapon.ClipReloadTime = new RangeDuration(ConvertFrames(record, nameof(record.ClipReloadTime), record.ClipReloadTime.Value, context));
        }

        var nugget = new DamageNugget
        {
            Damage = (float)context.Convert(record.Id, nameof(record.PrimaryDamage), record.PrimaryDamage.Value, record.PrimaryDamage.Value.Value, "hit points"),
        };

        if (record.PrimaryDamageRadius.IsPresent)
        {
            nugget.Radius = (float)UnitBodyMovementMaterializer.ConvertDistance(record, nameof(record.PrimaryDamageRadius), record.PrimaryDamageRadius.Value, context);
        }

        if (record.DamageType.IsPresent)
        {
            if (EngineEnumNames.TryParse<DamageType>(record.DamageType.Value, out var damageType))
            {
                nugget.DamageType = damageType;
            }
            else
            {
                context.Error(record.Id, $"Unknown damage type '{record.DamageType.Value}'.");
            }
        }

        weapon.Nuggets.Add(nugget);

        var projectile = record.Projectile.IsPresent
            ? resolution.Closure.OfType<ProjectileRecord>().FirstOrDefault(r => r.Id.Equals(record.Projectile.Value))
            : null;

        if (projectile is not null && projectile.Speed.IsPresent)
        {
            weapon.WeaponSpeed = (float)context.Convert(record.Id, "ProjectileSpeed", projectile.Speed.Value, context.Policy.ToDistancePerFrame(projectile.Speed.Value), "distance/frame");
        }

        if (record.TargetCategories.Mode == ListOverrideMode.Replace)
        {
            var mask = WeaponAntiFlags.None;
            foreach (var category in record.TargetCategories.Resolved)
            {
                if (Enum.TryParse<WeaponAntiFlags>(category, ignoreCase: true, out var flag) && Enum.IsDefined(flag))
                {
                    mask |= flag;
                }
                else
                {
                    context.Error(record.Id, $"Unknown target category '{category}'.");
                }
            }

            weapon.AntiMask = mask;
        }

        return weapon;
    }

    public static ArmorTemplate Materialize(ArmorRecord record, UnitMaterializationContext context)
    {
        var armor = new ArmorTemplate();
        armor.AssignImportedName("Armor", record.Id.Name);

        if (record.DefaultMultiplier.IsPresent)
        {
            Array.Fill(armor.Values, new Percentage((float)record.DefaultMultiplier.Value));
        }

        foreach (var (damageTypeName, multiplier) in record.Multipliers)
        {
            if (!EngineEnumNames.TryParse<DamageType>(damageTypeName, out var damageType))
            {
                context.Error(record.Id, $"Unknown damage type '{damageTypeName}'.");
                continue;
            }

            armor.Values[(int)damageType] = new Percentage((float)multiplier.Value);
        }

        return armor;
    }

    /// <summary>The unit's weapons: weapon-set slots plus weapons bound through Attack ability modules.</summary>
    public static IReadOnlyCollection<ContentId> WeaponIds(UnitRecord unit, UnitImportResolution resolution)
    {
        var abilityWeapons = unit.ResolvedModules
            .Where(module => module.Ability is not null)
            .Select(module => resolution.Closure.FirstOrDefault(record => record.Id.Equals(module.Ability!.Value)))
            .OfType<AbilityRecord>()
            .Where(ability => ability.Weapon.IsPresent)
            .Select(ability => ability.Weapon.Value);

        return unit.Weapons.Resolved.Concat(abilityWeapons).ToHashSet();
    }

    /// <summary>Card WI-X1-06: stamps the validated presentation-only effect damage type on the unit's own weapons.</summary>
    public static void ApplyImpactEffectDamageType(UnitRecord unit, UnitImportResolution resolution, UnitMaterializationContext context)
    {
        if (context.ImpactEffectDamageType(unit.Id) is not { } damageType)
        {
            return;
        }

        foreach (var weaponId in WeaponIds(unit, resolution))
        {
            if (context.Staged.TryGetValue(weaponId, out var asset) && asset is WeaponTemplate weapon)
            {
                foreach (var nugget in weapon.Nuggets.OfType<DamageNugget>())
                {
                    nugget.DamageFXOverride = damageType;
                }
            }
        }
    }

    /// <summary>Adds the default (unconditioned) weapon and armor sets; primary, secondary, tertiary slots in order.</summary>
    public static void AddSets(UnitRecord record, ObjectDefinition definition, UnitMaterializationContext context)
    {
        var weapons = record.Weapons.Resolved;
        if (weapons.Count > WeaponTemplateSet.NumWeaponSlots)
        {
            context.Error(record.Id, $"{weapons.Count} weapons exceed the {WeaponTemplateSet.NumWeaponSlots} native slots.");
        }
        else if (weapons.Count > 0)
        {
            var set = new WeaponTemplateSet { ObjectDefinition = definition };
            for (var i = 0; i < weapons.Count; i++)
            {
                set.Slots[i] = new WeaponSetSlot { Weapon = context.Reference<WeaponTemplate>(weapons[i]) };
            }

            definition.WeaponSets[set.Conditions] = set;
        }

        var damageFX = context.DamageEffectReference(record.Id);
        if (record.Armor.IsPresent || damageFX is not null)
        {
            definition.ArmorSets.Add(ArmorTemplateSet.CreateImported(
                record.Armor.IsPresent ? context.Reference<ArmorTemplate>(record.Armor.Value) : null!, damageFX));
        }
    }

    private static LogicFrameSpan ConvertFrames(SourceRecord record, string field, Quantity value, UnitMaterializationContext context)
    {
        var frames = context.Policy.ToLogicFrames(value);
        context.Convert(record.Id, field, value, frames, "logic frames");
        return new LogicFrameSpan(frames);
    }
}
