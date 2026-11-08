#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using Panopticon.Content.Importing.Units;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Serializes one engine-owned record into its JSON payload. Shapes mirror the H4 reader: authored
/// values as <c>{value, origin}</c> objects, quantities as <c>{value, unit}</c> pairs with the
/// declared unit verbatim, list overrides with their mode, module edits ordered, and valued
/// unclassified fields with raw source text. Handles never appear in the payload.
/// </summary>
public static class UnitPackageRecordPayloadWriter
{
    public static bool TryWrite(
        SourceRecord record,
        ContentSource source,
        [NotNullWhen(true)] out byte[]? payload,
        [NotNullWhen(false)] out UnitPackageWriteDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                WriteCommon(writer, record, source);
                WriteKindFields(writer, record, source);
                WriteUnclassified(writer, record, source);
                writer.WriteEndObject();
            }

            payload = stream.ToArray();
            diagnostic = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            payload = null;
            diagnostic = new UnitPackageWriteDiagnostic(UnitPackageWriteDiagnosticCodes.QuantityInvalid, ex.Message, record.Id);
            return false;
        }
    }

    private static void WriteCommon(Utf8JsonWriter writer, SourceRecord record, ContentSource source)
    {
        writer.WriteString("name", record.Id.Name);
        writer.WriteString("kind", record.Id.Kind.ToString());

        if (record.Parent is { } parent)
        {
            writer.WritePropertyName("parent");
            WriteContentIdValue(writer, parent, source);
        }

        writer.WritePropertyName("provenance");
        WriteProvenanceValue(writer, record.Provenance);

        if (record.Legacy is not null)
        {
            writer.WriteBoolean("hasLegacyBinding", true);
        }
    }

    private static void WriteKindFields(Utf8JsonWriter writer, SourceRecord record, ContentSource source)
    {
        switch (record)
        {
            case UnitRecord unit:
                WriteUnit(writer, unit, source);
                break;
            case WeaponRecord weapon:
                WriteWeapon(writer, weapon, source);
                break;
            case ArmorRecord armor:
                WriteArmor(writer, armor, source);
                break;
            case LocomotorRecord locomotor:
                WriteLocomotor(writer, locomotor, source);
                break;
            case UpgradeRecord upgrade:
                WriteUpgrade(writer, upgrade, source);
                break;
            case AbilityRecord ability:
                WriteAbility(writer, ability, source);
                break;
            case ProjectileRecord projectile:
                WriteProjectile(writer, projectile, source);
                break;
            case VisualStateRecord visual:
                WriteVisualState(writer, visual, source);
                break;
            default:
                throw new InvalidOperationException($"Record '{record.Id}' uses unsupported type {record.GetType().Name}.");
        }
    }

    private static void WriteUnit(Utf8JsonWriter writer, UnitRecord unit, ContentSource source)
    {
        WriteAuthoredString(writer, "displayName", unit.DisplayName, source);
        WriteAuthoredQuantity(writer, "maxHealth", unit.MaxHealth, source);
        WriteAuthoredQuantity(writer, "initialHealth", unit.InitialHealth, source);
        WriteAuthoredQuantity(writer, "geometryMajorRadius", unit.GeometryMajorRadius, source);
        WriteAuthoredQuantity(writer, "geometryHeight", unit.GeometryHeight, source);
        WriteAuthoredQuantity(writer, "visionRange", unit.VisionRange, source);
        WriteAuthoredQuantity(writer, "deathDuration", unit.DeathDuration, source);
        WriteStringList(writer, "kindOf", unit.KindOf);
        WriteContentIdList(writer, "locomotors", unit.Locomotors, source);
        WriteContentIdList(writer, "weapons", unit.Weapons, source);
        WriteAuthoredContentId(writer, "armor", unit.Armor, source);
        WriteAuthoredQuantity(writer, "buildCost", unit.BuildCost, source);
        WriteAuthoredQuantity(writer, "buildTime", unit.BuildTime, source);
        WriteContentIdList(writer, "prerequisites", unit.Prerequisites, source);
        WriteContentIdList(writer, "produces", unit.Produces, source);
        WriteContentIdList(writer, "upgrades", unit.Upgrades, source);
        WriteAuthoredContentId(writer, "visual", unit.Visual, source);
        WriteContentIdList(writer, "presentation", unit.Presentation, source);
        WriteModules(writer, unit, source);
    }

    private static void WriteWeapon(Utf8JsonWriter writer, WeaponRecord weapon, ContentSource source)
    {
        WriteAuthoredQuantity(writer, "primaryDamage", weapon.PrimaryDamage, source);
        WriteAuthoredQuantity(writer, "primaryDamageRadius", weapon.PrimaryDamageRadius, source);
        WriteAuthoredString(writer, "damageType", weapon.DamageType, source);
        WriteAuthoredQuantity(writer, "attackRange", weapon.AttackRange, source);
        WriteAuthoredQuantity(writer, "minimumAttackRange", weapon.MinimumAttackRange, source);
        WriteAuthoredQuantity(writer, "reloadTime", weapon.ReloadTime, source);
        WriteAuthoredQuantity(writer, "preAttackDelay", weapon.PreAttackDelay, source);
        WriteAuthoredInt(writer, "clipSize", weapon.ClipSize, source);
        WriteAuthoredQuantity(writer, "clipReloadTime", weapon.ClipReloadTime, source);
        WriteStringList(writer, "targetCategories", weapon.TargetCategories);
        WriteAuthoredContentId(writer, "projectile", weapon.Projectile, source);
        WriteContentIdList(writer, "presentation", weapon.Presentation, source);
    }

    private static void WriteArmor(Utf8JsonWriter writer, ArmorRecord armor, ContentSource source)
    {
        writer.WriteStartObject("multipliers");

        foreach (var (damageType, multiplier) in OrderedEntries(armor.Multipliers))
        {
            WriteAuthoredDouble(writer, damageType, multiplier, source);
        }

        writer.WriteEndObject();
        WriteAuthoredDouble(writer, "defaultMultiplier", armor.DefaultMultiplier, source);
    }

    private static void WriteLocomotor(Utf8JsonWriter writer, LocomotorRecord locomotor, ContentSource source)
    {
        WriteAuthoredQuantity(writer, "speed", locomotor.Speed, source);
        WriteAuthoredQuantity(writer, "turnRate", locomotor.TurnRate, source);
        WriteAuthoredQuantity(writer, "acceleration", locomotor.Acceleration, source);
        WriteAuthoredQuantity(writer, "braking", locomotor.Braking, source);
        WriteAuthoredQuantity(writer, "preferredHeight", locomotor.PreferredHeight, source);
        WriteAuthoredBool(writer, "canHover", locomotor.CanHover, source);
        WriteAuthoredString(writer, "appearance", locomotor.Appearance, source);
        WriteStringList(writer, "surfaces", locomotor.Surfaces);
    }

    private static void WriteUpgrade(Utf8JsonWriter writer, UpgradeRecord upgrade, ContentSource source)
    {
        WriteAuthoredString(writer, "displayName", upgrade.DisplayName, source);
        WriteAuthoredQuantity(writer, "buildCost", upgrade.BuildCost, source);
        WriteAuthoredQuantity(writer, "buildTime", upgrade.BuildTime, source);
        WriteAuthoredString(writer, "scope", upgrade.Scope, source);
        WriteContentIdList(writer, "prerequisites", upgrade.Prerequisites, source);
    }

    private static void WriteAbility(Utf8JsonWriter writer, AbilityRecord ability, ContentSource source)
    {
        WriteAuthoredString(writer, "family", ability.Family, source);
        WriteAuthoredQuantity(writer, "range", ability.Range, source);
        WriteAuthoredQuantity(writer, "cooldown", ability.Cooldown, source);
        WriteAuthoredContentId(writer, "weapon", ability.Weapon, source);
        WriteAuthoredContentId(writer, "requiredUpgrade", ability.RequiredUpgrade, source);
    }

    private static void WriteProjectile(Utf8JsonWriter writer, ProjectileRecord projectile, ContentSource source)
    {
        WriteAuthoredQuantity(writer, "speed", projectile.Speed, source);
        WriteAuthoredContentId(writer, "visual", projectile.Visual, source);
    }

    private static void WriteVisualState(Utf8JsonWriter writer, VisualStateRecord visual, ContentSource source)
    {
        WriteAuthoredString(writer, "modelKey", visual.ModelKey, source);
        writer.WriteStartObject("stateAnimations");

        foreach (var (state, animation) in OrderedEntries(visual.StateAnimations))
        {
            WriteAuthoredString(writer, state, animation, source);
        }

        writer.WriteEndObject();
    }

    private static void WriteModules(Utf8JsonWriter writer, UnitRecord unit, ContentSource source)
    {
        if (unit.Modules.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("modules");

        foreach (var edit in unit.Modules)
        {
            writer.WriteStartObject();
            writer.WriteString("operation", edit.Operation.ToString());
            writer.WriteString("tag", edit.Tag);

            if (edit.Module is { } module)
            {
                writer.WriteStartObject("module");
                writer.WriteString("tag", module.Tag);
                writer.WriteString("family", module.Family);
                writer.WriteString("kinds", ModuleKindsNames(module.Kinds));
                writer.WriteString("inheritanceMode", module.InheritanceMode.ToString());

                if (module.Ability is { } ability)
                {
                    writer.WritePropertyName("ability");
                    WriteContentIdValue(writer, ability, source);
                }

                if (module.Legacy is not null)
                {
                    writer.WriteBoolean("hasLegacyBinding", true);
                }

                if (module.ExclusiveGroup is { } exclusiveGroup)
                {
                    writer.WriteString("exclusiveGroup", exclusiveGroup);
                }

                if (module.Inherited)
                {
                    writer.WriteBoolean("inherited", true);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteUnclassified(Utf8JsonWriter writer, SourceRecord record, ContentSource source)
    {
        if (record.Unclassified.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("unclassified");

        foreach (var field in record.Unclassified)
        {
            writer.WriteStartObject();
            writer.WriteString("name", field.Name);

            if (field.AffectsGameplay)
            {
                writer.WriteBoolean("affectsGameplay", true);
            }

            if (field.ExcludedByPolicy)
            {
                writer.WriteBoolean("excludedByPolicy", true);
            }

            if (field.Provenance is { } provenance)
            {
                writer.WritePropertyName("provenance");
                WriteProvenanceValue(writer, provenance);
            }

            if (field.RawValue is { } rawValue)
            {
                writer.WriteString("rawValue", rawValue);
            }

            if (field.RawBlock is { } rawBlock)
            {
                writer.WriteStartArray("rawBlock");

                foreach (var line in rawBlock)
                {
                    writer.WriteStringValue(line);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteAuthoredQuantity(Utf8JsonWriter writer, string name, Authored<Quantity> value, ContentSource source)
    {
        if (!value.IsPresent)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WritePropertyName("value");
        WriteQuantityValue(writer, value.Value, name);
        WriteOriginMember(writer, value.Origin, source);
        writer.WriteEndObject();
    }

    private static void WriteAuthoredString(Utf8JsonWriter writer, string name, Authored<string> value, ContentSource source)
    {
        if (!value.IsPresent)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("value", value.Value);
        WriteOriginMember(writer, value.Origin, source);
        writer.WriteEndObject();
    }

    private static void WriteAuthoredInt(Utf8JsonWriter writer, string name, Authored<int> value, ContentSource source)
    {
        if (!value.IsPresent)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteNumber("value", value.Value);
        WriteOriginMember(writer, value.Origin, source);
        writer.WriteEndObject();
    }

    private static void WriteAuthoredDouble(Utf8JsonWriter writer, string name, Authored<double> value, ContentSource source)
    {
        if (!value.IsPresent)
        {
            return;
        }

        EnsureFinite(value.Value, name);
        writer.WriteStartObject(name);
        writer.WriteNumber("value", value.Value);
        WriteOriginMember(writer, value.Origin, source);
        writer.WriteEndObject();
    }

    private static void WriteAuthoredBool(Utf8JsonWriter writer, string name, Authored<bool> value, ContentSource source)
    {
        if (!value.IsPresent)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteBoolean("value", value.Value);
        WriteOriginMember(writer, value.Origin, source);
        writer.WriteEndObject();
    }

    private static void WriteAuthoredContentId(Utf8JsonWriter writer, string name, Authored<ContentId> value, ContentSource source)
    {
        if (!value.IsPresent)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WritePropertyName("value");
        WriteContentIdValue(writer, value.Value, source);
        WriteOriginMember(writer, value.Origin, source);
        writer.WriteEndObject();
    }

    private static void WriteQuantityValue(Utf8JsonWriter writer, Quantity quantity, string fieldName)
    {
        EnsureFinite(quantity.Value, fieldName);
        writer.WriteStartObject();
        writer.WriteNumber("value", quantity.Value);
        writer.WriteString("unit", quantity.Unit.ToString());
        writer.WriteEndObject();
    }

    private static void WriteOriginMember(Utf8JsonWriter writer, ContentId? origin, ContentSource source)
    {
        if (origin is { } value)
        {
            writer.WritePropertyName("origin");
            WriteContentIdValue(writer, value, source);
        }
    }

    private static void WriteContentIdValue(Utf8JsonWriter writer, ContentId id, ContentSource source)
    {
        writer.WriteStartObject();
        writer.WriteString("name", id.Name);
        writer.WriteString("kind", id.Kind.ToString());

        if (!string.Equals(id.Source?.Id, source.Id, StringComparison.Ordinal))
        {
            writer.WriteString("source", id.Source?.Id ?? string.Empty);
        }

        writer.WriteEndObject();
    }

    private static void WriteProvenanceValue(Utf8JsonWriter writer, Provenance provenance)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceId", provenance.SourceId);
        writer.WriteString("filePath", provenance.FilePath);
        writer.WriteNumber("line", provenance.Line);

        if (provenance.ArchiveLayer is { } layer)
        {
            writer.WriteString("archiveLayer", layer);
        }

        writer.WriteEndObject();
    }

    private static void WriteStringList(Utf8JsonWriter writer, string name, ListOverride<string> list)
    {
        writer.WriteStartObject(name);
        writer.WriteString("mode", list.Mode.ToString());
        WriteListMembers(writer, list, static (Utf8JsonWriter w, string item) => w.WriteStringValue(item));
        writer.WriteEndObject();
    }

    private static void WriteContentIdList(Utf8JsonWriter writer, string name, ListOverride<ContentId> list, ContentSource source)
    {
        writer.WriteStartObject(name);
        writer.WriteString("mode", list.Mode.ToString());
        WriteListMembers(writer, list, (Utf8JsonWriter w, ContentId item) => WriteContentIdValue(w, item, source));
        writer.WriteEndObject();
    }

    private static void WriteListMembers<T>(Utf8JsonWriter writer, ListOverride<T> list, Action<Utf8JsonWriter, T> writeItem)
    {
        if (list.Mode == ListOverrideMode.Inherit)
        {
            return;
        }

        writer.WriteStartArray("items");

        foreach (var item in list.Items)
        {
            writeItem(writer, item);
        }

        writer.WriteEndArray();

        if (list.Mode != ListOverrideMode.Edit)
        {
            return;
        }

        writer.WriteStartArray("removals");

        foreach (var removal in list.Removals)
        {
            writeItem(writer, removal);
        }

        writer.WriteEndArray();
    }

    private static string ModuleKindsNames(ModuleKinds kinds)
    {
        var names = new List<string>();

        foreach (var value in Enum.GetValues<ModuleKinds>())
        {
            if (value != ModuleKinds.None && kinds.HasFlag(value))
            {
                names.Add(value.ToString());
            }
        }

        return names.Count > 0 ? string.Join(",", names) : ModuleKinds.None.ToString();
    }

    private static IEnumerable<KeyValuePair<string, T>> OrderedEntries<T>(IReadOnlyDictionary<string, T> dictionary)
    {
        var entries = new List<KeyValuePair<string, T>>(dictionary.Count);
        entries.AddRange(dictionary);
        entries.Sort(static (left, right) => string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static void EnsureFinite(double value, string fieldName)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException($"Field '{fieldName}' holds non-finite value {value}; packages cannot represent NaN or infinity.");
        }
    }
}
