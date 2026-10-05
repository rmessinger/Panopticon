#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Checks a resolved closure against runtime capabilities and numeric constraints before anything
/// is materialized. Every finding is collected; any error blocks publication.
/// </summary>
public sealed class UnitImportValidator
{
    private static readonly QuantityUnit[] HealthUnits = [QuantityUnit.HitPoints, QuantityUnit.Scalar];
    private static readonly QuantityUnit[] DistanceUnits = [QuantityUnit.GameDistance, QuantityUnit.Meters];
    private static readonly QuantityUnit[] TimeUnits = [QuantityUnit.Seconds, QuantityUnit.Milliseconds, QuantityUnit.LogicFrames];
    private static readonly QuantityUnit[] SpeedUnits = [QuantityUnit.DistancePerSecond];
    private static readonly QuantityUnit[] AccelerationUnits = [QuantityUnit.DistancePerSecondSquared];
    private static readonly QuantityUnit[] TurnUnits = [QuantityUnit.DegreesPerSecond];
    private static readonly QuantityUnit[] CostUnits = [QuantityUnit.Currency];
    private static readonly QuantityUnit[] DamageUnits = [QuantityUnit.HitPoints, QuantityUnit.Scalar];

    private static readonly string[] UpgradeScopes = ["Player", "Object"];

    private readonly UnitCapabilityRegistry _registry;

    public UnitImportValidator(UnitCapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public IReadOnlyList<ImportDiagnostic> Validate(IEnumerable<SourceRecord> closure)
    {
        var context = new Context();

        foreach (var record in closure)
        {
            context.Record = record;
            ValidateUnclassified(context);

            if (record.Legacy is not null && !(record.Legacy.Owner.Equals(_registry.LegacySource) && record.Id.Source.Equals(_registry.LegacySource)))
            {
                context.Error(DiagnosticCodes.LegacyBindingNotAllowed, $"Record uses a legacy template binding outside {_registry.LegacySource}.");
            }

            switch (record)
            {
                case UnitRecord unit:
                    ValidateUnit(context, unit);
                    break;
                case WeaponRecord weapon:
                    ValidateWeapon(context, weapon);
                    break;
                case ArmorRecord armor:
                    ValidateArmor(context, armor);
                    break;
                case LocomotorRecord locomotor:
                    ValidateLocomotor(context, locomotor);
                    break;
                case ProjectileRecord projectile:
                    context.Optional(projectile.Speed, nameof(projectile.Speed), SpeedUnits, minimum: 0);
                    break;
                case AbilityRecord ability:
                    ValidateAbility(context, ability);
                    break;
                case UpgradeRecord upgrade:
                    ValidateUpgrade(context, upgrade);
                    break;
                case VisualStateRecord visual:
                    ValidateVisual(context, visual);
                    break;
            }
        }

        return context.Diagnostics;
    }

    private static void ValidateUnclassified(Context context)
    {
        foreach (var field in context.Record.Unclassified)
        {
            if (field.AffectsGameplay)
            {
                context.Error(DiagnosticCodes.UnsupportedMechanic, $"Gameplay field '{field.Name}' has no runtime mapping.", field.Provenance);
            }
            else
            {
                context.Add(ImportDiagnosticSeverity.Warning, DiagnosticCodes.NonGameplayMetadata, $"Ignored non-gameplay field '{field.Name}'.", field.Provenance);
            }
        }
    }

    private void ValidateUnit(Context context, UnitRecord unit)
    {
        context.Required(unit.MaxHealth, nameof(unit.MaxHealth), HealthUnits, minimum: 0, exclusive: true);
        context.Optional(unit.InitialHealth, nameof(unit.InitialHealth), HealthUnits, minimum: 0);
        context.Optional(unit.GeometryMajorRadius, nameof(unit.GeometryMajorRadius), DistanceUnits, minimum: 0);
        context.Optional(unit.GeometryHeight, nameof(unit.GeometryHeight), DistanceUnits, minimum: 0);
        context.Optional(unit.VisionRange, nameof(unit.VisionRange), DistanceUnits, minimum: 0);
        context.Optional(unit.BuildCost, nameof(unit.BuildCost), CostUnits, minimum: 0);
        context.Optional(unit.BuildTime, nameof(unit.BuildTime), TimeUnits, minimum: 0);

        if (unit.InitialHealth.IsPresent && unit.MaxHealth.IsPresent && unit.InitialHealth.Value.Value > unit.MaxHealth.Value.Value)
        {
            context.Error(DiagnosticCodes.InvalidNumber, "InitialHealth exceeds MaxHealth.");
        }

        if (!unit.Visual.IsPresent)
        {
            context.Add(ImportDiagnosticSeverity.Info, DiagnosticCodes.NoVisualBinding, "Unit has no visual binding.");
        }

        foreach (var module in unit.ResolvedModules)
        {
            ValidateModule(context, unit, module);
        }
    }

    private void ValidateModule(Context context, UnitRecord unit, UnitModule module)
    {
        if (module.Ability is not null)
        {
            return;
        }

        switch (_registry.GetModuleSupport(module.Family))
        {
            case ModuleSupport.Typed:
                return;

            case ModuleSupport.Legacy:
                if (module.Legacy is null)
                {
                    context.Error(DiagnosticCodes.UnsupportedModule, $"Module '{module.Tag}' ({module.Family}) needs a legacy binding.");
                }
                else if (!module.Legacy.Owner.Equals(_registry.LegacySource) || !unit.Id.Source.Equals(_registry.LegacySource))
                {
                    context.Error(DiagnosticCodes.LegacyBindingNotAllowed, $"Module '{module.Tag}' ({module.Family}) uses a legacy binding outside {_registry.LegacySource}.");
                }

                return;

            default:
                context.Error(DiagnosticCodes.UnsupportedModule, $"Module family '{module.Family}' (tag '{module.Tag}') is not supported.");
                return;
        }
    }

    private static void ValidateWeapon(Context context, WeaponRecord weapon)
    {
        context.Required(weapon.PrimaryDamage, nameof(weapon.PrimaryDamage), DamageUnits, minimum: 0);
        context.Optional(weapon.PrimaryDamageRadius, nameof(weapon.PrimaryDamageRadius), DistanceUnits, minimum: 0);
        context.Required(weapon.AttackRange, nameof(weapon.AttackRange), DistanceUnits, minimum: 0, exclusive: true);
        context.Optional(weapon.MinimumAttackRange, nameof(weapon.MinimumAttackRange), DistanceUnits, minimum: 0);
        context.Required(weapon.ReloadTime, nameof(weapon.ReloadTime), TimeUnits, minimum: 0, exclusive: true);
        context.Optional(weapon.ClipReloadTime, nameof(weapon.ClipReloadTime), TimeUnits, minimum: 0);

        if (weapon.ClipSize.IsPresent && weapon.ClipSize.Value < 0)
        {
            context.Error(DiagnosticCodes.InvalidNumber, "ClipSize must not be negative.");
        }

        if (weapon.MinimumAttackRange.IsPresent && weapon.AttackRange.IsPresent && weapon.MinimumAttackRange.Value.Value > weapon.AttackRange.Value.Value)
        {
            context.Error(DiagnosticCodes.InvalidNumber, "MinimumAttackRange exceeds AttackRange.");
        }
    }

    private static void ValidateArmor(Context context, ArmorRecord armor)
    {
        foreach (var (damageType, multiplier) in armor.Multipliers)
        {
            if (multiplier.IsPresent && !(double.IsFinite(multiplier.Value) && multiplier.Value >= 0))
            {
                context.Error(DiagnosticCodes.InvalidNumber, $"Armor multiplier for '{damageType}' must be finite and non-negative.");
            }
        }

        if (armor.DefaultMultiplier.IsPresent && !(double.IsFinite(armor.DefaultMultiplier.Value) && armor.DefaultMultiplier.Value >= 0))
        {
            context.Error(DiagnosticCodes.InvalidNumber, "Default armor multiplier must be finite and non-negative.");
        }
    }

    private static void ValidateLocomotor(Context context, LocomotorRecord locomotor)
    {
        context.Required(locomotor.Speed, nameof(locomotor.Speed), SpeedUnits, minimum: 0);
        context.Optional(locomotor.TurnRate, nameof(locomotor.TurnRate), TurnUnits, minimum: 0);
        context.Optional(locomotor.Acceleration, nameof(locomotor.Acceleration), AccelerationUnits, minimum: 0);
        context.Optional(locomotor.Braking, nameof(locomotor.Braking), AccelerationUnits, minimum: 0);
    }

    private void ValidateAbility(Context context, AbilityRecord ability)
    {
        if (!ability.Family.IsPresent)
        {
            context.Error(DiagnosticCodes.MissingRequiredValue, "Ability has no family.");
        }
        else if (!_registry.SupportsAbility(ability.Family.Value))
        {
            context.Error(DiagnosticCodes.UnsupportedAbility, $"Ability family '{ability.Family.Value}' is not supported.");
        }

        context.Optional(ability.Range, nameof(ability.Range), DistanceUnits, minimum: 0);
        context.Optional(ability.Cooldown, nameof(ability.Cooldown), TimeUnits, minimum: 0, exclusive: true);
    }

    private static void ValidateUpgrade(Context context, UpgradeRecord upgrade)
    {
        context.Optional(upgrade.BuildCost, nameof(upgrade.BuildCost), CostUnits, minimum: 0);
        context.Optional(upgrade.BuildTime, nameof(upgrade.BuildTime), TimeUnits, minimum: 0);

        if (upgrade.Scope.IsPresent && !UpgradeScopes.Contains(upgrade.Scope.Value, StringComparer.OrdinalIgnoreCase))
        {
            context.Error(DiagnosticCodes.InvalidEnumValue, $"Upgrade scope '{upgrade.Scope.Value}' must be Player or Object.");
        }
    }

    private static void ValidateVisual(Context context, VisualStateRecord visual)
    {
        if (!visual.ModelKey.IsPresent)
        {
            context.Error(DiagnosticCodes.MissingRequiredValue, "Visual has no model.");
        }

        foreach (var state in VisualStateRecord.RequiredStates)
        {
            if (!visual.StateAnimations.TryGetValue(state, out var animation) || !animation.IsPresent)
            {
                context.Error(DiagnosticCodes.MissingVisualState, $"Visual is missing required state '{state}'.");
            }
        }
    }

    private sealed class Context
    {
        public List<ImportDiagnostic> Diagnostics { get; } = [];

        public SourceRecord Record { get; set; } = null!;

        public void Add(ImportDiagnosticSeverity severity, string code, string message, Provenance? provenance = null)
        {
            Diagnostics.Add(new ImportDiagnostic(severity, ImportStage.Validate, code, $"{Record.Id}: {message}", Record.Id, provenance ?? Record.Provenance));
        }

        public void Error(string code, string message, Provenance? provenance = null) => Add(ImportDiagnosticSeverity.Error, code, message, provenance);

        public void Required(Authored<Quantity> value, string name, QuantityUnit[] units, double minimum, bool exclusive = false)
        {
            if (!value.IsPresent)
            {
                Error(DiagnosticCodes.MissingRequiredValue, $"{name} is required.");
                return;
            }

            Optional(value, name, units, minimum, exclusive);
        }

        public void Optional(Authored<Quantity> value, string name, QuantityUnit[] units, double minimum, bool exclusive = false)
        {
            if (!value.IsPresent)
            {
                return;
            }

            var quantity = value.Value;

            if (!units.Contains(quantity.Unit))
            {
                Error(DiagnosticCodes.UnitMismatch, $"{name} has unit {quantity.Unit}; expected {string.Join(" or ", units)}.");
            }

            if (!double.IsFinite(quantity.Value))
            {
                Error(DiagnosticCodes.InvalidNumber, $"{name} must be finite.");
            }
            else if (exclusive ? quantity.Value <= minimum : quantity.Value < minimum)
            {
                Error(DiagnosticCodes.InvalidNumber, $"{name} must be {(exclusive ? "greater than" : "at least")} {minimum}; got {quantity.Value}.");
            }
        }
    }
}
