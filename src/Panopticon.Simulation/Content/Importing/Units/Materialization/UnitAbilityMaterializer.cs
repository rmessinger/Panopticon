#nullable enable

using System;
using System.Linq;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Card WI-H2a-09: typed abilities and legacy-bound native modules. The typed <c>Attack</c> family
/// binds its weapon into the unit's default weapon set. Legacy module data is attached as built by
/// the Generals adapter and is never mutated, because the adapter may share it between definitions.
/// </summary>
internal static class UnitAbilityMaterializer
{
    public static void AddModules(UnitRecord record, ObjectDefinition definition, UnitImportResolution resolution, UnitMaterializationContext context)
    {
        foreach (var module in record.ResolvedModules)
        {
            if (module.Legacy is not null)
            {
                AddLegacyModule(record, definition, module, context);
            }
            else if (module.Ability is { } abilityId)
            {
                AddAbility(record, definition, resolution.Get<AbilityRecord>(abilityId), context);
            }
        }
    }

    private static void AddLegacyModule(UnitRecord record, ObjectDefinition definition, UnitModule module, UnitMaterializationContext context)
    {
        var data = context.GetLegacy<ModuleData>(record.Id, module.Legacy!);
        if (data is null)
        {
            return;
        }

        var modules = (module.Kinds & ModuleKinds.Draw) != 0
            ? definition.Draws
            : (module.Kinds & ModuleKinds.ClientUpdate) != 0
                ? definition.ClientUpdates
                : definition.Behaviors;

        UnitBodyMovementMaterializer.AddResolved(modules, new ModuleDataContainer(module.Tag, data, module.InheritanceMode, module.Inherited));
    }

    private static void AddAbility(UnitRecord record, ObjectDefinition definition, AbilityRecord ability, UnitMaterializationContext context)
    {
        switch (ability.Family.Value)
        {
            case "Attack":
                if (!ability.Weapon.IsPresent)
                {
                    context.Error(record.Id, $"Attack ability '{ability.Id.Name}' has no weapon.");
                    return;
                }

                BindWeapon(record, definition, ability.Weapon.Value, context);
                return;

            default:
                context.Error(record.Id, $"Ability family '{ability.Family.Value}' has no typed materializer.");
                return;
        }
    }

    private static void BindWeapon(UnitRecord record, ObjectDefinition definition, ContentId weaponId, UnitMaterializationContext context)
    {
        var set = definition.WeaponSets.Values.FirstOrDefault(s => !s.Conditions.AnyBitSet);
        if (set is null)
        {
            set = new WeaponTemplateSet { ObjectDefinition = definition };
            definition.WeaponSets[set.Conditions] = set;
        }

        if (set.Slots.Any(slot => slot?.Weapon?.Name == weaponId.Name))
        {
            return;
        }

        var free = Array.FindIndex(set.Slots, slot => slot is null);
        if (free < 0)
        {
            context.Error(record.Id, $"No free weapon slot for attack weapon '{weaponId.Name}'.");
            return;
        }

        set.Slots[free] = new WeaponSetSlot { Weapon = context.Reference<WeaponTemplate>(weaponId) };
    }
}
