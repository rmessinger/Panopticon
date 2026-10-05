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

        return new MaterializedUnitContent(resolution.Source, context.Staged.ToDictionary(), context.Conversions.ToArray(), context.Diagnostics.ToArray());
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
            case UnitRecord unit:
                var definition = UnitBodyMovementMaterializer.CreateDefinition(unit, context);
                UnitCombatMaterializer.AddSets(unit, definition, context);
                UnitAbilityMaterializer.AddModules(unit, definition, resolution, context);
                return definition;
            default:
                // Abilities, projectiles, and visual states shape other assets and have no runtime asset of their own.
                return null;
        }
    }
}
