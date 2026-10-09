#nullable enable

using System;
using System.Linq;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Builds detached native runtime assets for a validated closure. Dependencies are materialized
/// before units; nothing is registered with an <see cref="AssetStore"/>.
/// </summary>
public static class UnitRuntimeMaterializer
{
    public static MaterializedUnitContent Materialize(UnitImportResolution resolution, UnitMaterializationContext context)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(context);

        if (!resolution.Succeeded)
        {
            throw new InvalidOperationException("Only a resolution without errors can be materialized.");
        }

        foreach (var record in resolution.Closure.OrderBy(r => r is UnitRecord))
        {
            try
            {
                var asset = record.Legacy is not null
                    ? context.GetLegacy<BaseAsset>(record.Id, record.Legacy)
                    : MaterializeTyped(record, resolution, context);

                if (asset is not null)
                {
                    context.Stage(record.Id, asset);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                context.Error(record.Id, exception.Message);
            }
        }

        return new MaterializedUnitContent(context, resolution.Source, context.Staged.ToDictionary(), context.Conversions.ToArray(), context.Diagnostics.ToArray(), context.VisualAssets.ToArray());
    }

    /// <summary>
    /// Card WI-M2-02: aircraft with an authored clip rearm at their base (Generals aircraft
    /// configure RETURN_TO_BASE), so a sortie ends with the return flight instead of a mid-air
    /// reload. Ground units keep the default auto reload.
    /// </summary>
    private static void ArmAircraftWeaponsAtBase(UnitRecord unit, ObjectDefinition definition, UnitImportResolution resolution, UnitMaterializationContext context)
    {
        if (!unit.KindOf.Resolved.Contains("AIRCRAFT", StringComparer.Ordinal))
        {
            return;
        }

        // Weapons bind through the unit's Attack ability modules; an authored clip means the
        // aircraft rearms at its base.
        var weaponIds = unit.ResolvedModules
            .Where(module => module.Ability is not null)
            .Select(module => resolution.Closure.FirstOrDefault(record => record.Id.Equals(module.Ability!.Value)))
            .OfType<AbilityRecord>()
            .Where(ability => ability.Weapon.IsPresent)
            .Select(ability => ability.Weapon.Value);

        foreach (var weaponId in weaponIds)
        {
            if (context.Staged.TryGetValue(weaponId, out var asset) && asset is WeaponTemplate weapon && weapon.ClipSize > 0)
            {
                weapon.AutoReloadsClip = WeaponReloadType.ReturnToBase;
            }
        }
    }

    private static BaseAsset? MaterializeTyped(SourceRecord record, UnitImportResolution resolution, UnitMaterializationContext context)
    {
        switch (record)
        {
            case LocomotorRecord locomotor:
                return UnitBodyMovementMaterializer.Materialize(locomotor, context);
            case WeaponRecord weapon:
                return UnitCombatMaterializer.Materialize(weapon, resolution, context);
            case ArmorRecord armor:
                return UnitCombatMaterializer.Materialize(armor, context);
            case UpgradeRecord upgrade:
                return UnitProductionMaterializer.Materialize(upgrade, context);
            case UnitRecord unit:
                var definition = UnitBodyMovementMaterializer.CreateDefinition(unit, resolution, context);
                UnitCombatMaterializer.AddSets(unit, definition, context);
                ArmAircraftWeaponsAtBase(unit, definition, resolution, context);
                UnitAbilityMaterializer.AddModules(unit, definition, resolution, context);
                UnitProductionMaterializer.Apply(unit, definition, context);
                UnitVisualMaterializer.AddDraw(unit, definition, resolution, context);
                return definition;
            default:
                // Abilities, projectiles, and visual states shape other assets and have no runtime asset of their own.
                return null;
        }
    }
}
