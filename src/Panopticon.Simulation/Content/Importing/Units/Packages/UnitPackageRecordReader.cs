#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Deserializes record JSON payloads into engine-owned SourceRecord instances.</summary>
public static class UnitPackageRecordReader
{
    private static readonly HashSet<string> CommonRecordProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "kind",
        "parent",
        "provenance",
        "hasLegacyBinding",
        "legacy",
        "unclassified",
    };

    private static readonly HashSet<string> KnownUnitProperties = new(CommonRecordProperties, StringComparer.OrdinalIgnoreCase)
    {
        "displayName",
        "maxHealth",
        "initialHealth",
        "geometryMajorRadius",
        "geometryHeight",
        "visionRange",
        "deathDuration",
        "kindOf",
        "locomotors",
        "weapons",
        "armor",
        "buildCost",
        "buildTime",
        "prerequisites",
        "produces",
        "upgrades",
        "visual",
        "presentation",
        "modules",
    };

    private static readonly HashSet<string> KnownWeaponProperties = new(CommonRecordProperties, StringComparer.OrdinalIgnoreCase)
    {
        "primaryDamage",
        "primaryDamageRadius",
        "damageType",
        "attackRange",
        "minimumAttackRange",
        "reloadTime",
        "preAttackDelay",
        "clipSize",
        "clipReloadTime",
        "targetCategories",
        "projectile",
        "presentation",
    };

    private static readonly HashSet<string> KnownArmorProperties = new(CommonRecordProperties, StringComparer.OrdinalIgnoreCase)
    {
        "multipliers",
        "defaultMultiplier",
    };

    private static readonly HashSet<string> KnownLocomotorProperties = new(CommonRecordProperties, StringComparer.OrdinalIgnoreCase)
    {
        "speed",
        "turnRate",
        "acceleration",
        "braking",
        "preferredHeight",
        "canHover",
        "appearance",
        "surfaces",
    };

    private static readonly HashSet<string> KnownUpgradeProperties = new(CommonRecordProperties, StringComparer.OrdinalIgnoreCase)
    {
        "displayName",
        "buildCost",
        "buildTime",
        "scope",
        "prerequisites",
    };

    private static readonly HashSet<string> KnownAbilityProperties = new(CommonRecordProperties, StringComparer.OrdinalIgnoreCase)
    {
        "family",
        "range",
        "cooldown",
        "weapon",
        "requiredUpgrade",
    };

    private static readonly HashSet<string> KnownProjectileProperties = new(CommonRecordProperties, StringComparer.OrdinalIgnoreCase)
    {
        "speed",
        "visual",
    };

    private static readonly HashSet<string> KnownVisualStateProperties = new(CommonRecordProperties, StringComparer.OrdinalIgnoreCase)
    {
        "modelKey",
        "stateAnimations",
    };

    public static SourceRecord? ReadRecord(
        byte[] payloadBytes,
        string expectedName,
        ContentKind expectedKind,
        ContentSource source,
        RetainedIniRebuildResult? retainedNatives,
        List<ImportDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(payloadBytes);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        JsonDocument doc;

        try
        {
            doc = JsonDocument.Parse(payloadBytes);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportStage.Read,
                UnitPackageDiagnosticCodes.PayloadInvalid,
                $"Record '{expectedName}' JSON parsing failed: {ex.Message}"));
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportStage.Read,
                    UnitPackageDiagnosticCodes.PayloadInvalid,
                    $"Record '{expectedName}' root must be a JSON object."));
                return null;
            }

            var recordId = new ContentId(source, expectedKind, expectedName);

            var provenance = new Provenance(source.Id, $"records/{expectedName}.json");

            if (root.TryGetProperty("provenance", out var provProp) && provProp.ValueKind == JsonValueKind.Object)
            {
                var srcId = provProp.TryGetProperty("sourceId", out var sidProp) ? sidProp.GetString() : source.Id;
                var fPath = provProp.TryGetProperty("filePath", out var fpProp) ? fpProp.GetString() : $"records/{expectedName}.json";
                var line = provProp.TryGetProperty("line", out var lineProp) && lineProp.TryGetInt32(out var ln) ? ln : 0;
                var layer = provProp.TryGetProperty("archiveLayer", out var alProp) ? alProp.GetString() : null;
                provenance = new Provenance(srcId ?? source.Id, fPath ?? string.Empty, line, layer);
            }

            ContentId? parent = null;

            if (root.TryGetProperty("parent", out var parentProp) && parentProp.ValueKind != JsonValueKind.Null)
            {
                if (UnitPackageListOverrideDto.TryParseContentId(parentProp, source, expectedKind, out var pId))
                {
                    parent = pId;
                }
            }

            var hasLegacy = root.TryGetProperty("hasLegacyBinding", out var hlbProp) && hlbProp.GetBoolean();

            if (!hasLegacy && root.TryGetProperty("legacy", out var lProp) && lProp.ValueKind == JsonValueKind.True)
            {
                hasLegacy = true;
            }

            var unclassifiedList = new List<UnclassifiedField>();

            if (root.TryGetProperty("unclassified", out var uncProp) && uncProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in uncProp.EnumerateArray())
                {
                    unclassifiedList.Add(UnitPackageUnclassifiedFieldDto.Parse(item, source));
                }
            }

            var knownProperties = GetKnownProperties(expectedKind);

            foreach (var prop in root.EnumerateObject())
            {
                if (!knownProperties.Contains(prop.Name))
                {
                    string? rawVal = null;
                    List<string>? rawBlock = null;

                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        rawVal = prop.Value.GetString();
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        rawBlock = new List<string>();
                        var allStrings = true;

                        foreach (var arrItem in prop.Value.EnumerateArray())
                        {
                            if (arrItem.ValueKind == JsonValueKind.String)
                            {
                                rawBlock.Add(arrItem.GetString() ?? string.Empty);
                            }
                            else
                            {
                                allStrings = false;
                                break;
                            }
                        }

                        if (!allStrings)
                        {
                            rawBlock = null;
                            rawVal = prop.Value.GetRawText();
                        }
                    }
                    else
                    {
                        rawVal = prop.Value.GetRawText();
                    }

                    unclassifiedList.Add(new UnclassifiedField(
                        prop.Name,
                        AffectsGameplay: false,
                        provenance,
                        ExcludedByPolicy: false,
                        rawVal,
                        rawBlock));

                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Warning,
                        ImportStage.Read,
                        UnitPackageDiagnosticCodes.UnknownField,
                        $"Unknown record field '{prop.Name}' was retained as an unclassified field.",
                        recordId,
                        provenance));
                }
            }

            LegacyBinding? legacyBinding = null;

            if (hasLegacy)
            {
                if (retainedNatives is null || !retainedNatives.RebuiltRecords.TryGetValue(recordId, out var rebuiltRecord))
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Error,
                        ImportStage.Read,
                        UnitPackageDiagnosticCodes.MissingRetainedPayload,
                        $"Record '{recordId}' requires a legacy binding, but no matching retained payload was found.",
                        recordId,
                        provenance));
                }
                else
                {
                    legacyBinding = rebuiltRecord.Legacy;
                }
            }

            SourceRecord createdRecord = expectedKind switch
            {
                ContentKind.Unit => ReadUnit(root, recordId, provenance, parent, legacyBinding, unclassifiedList, source, retainedNatives, diagnostics),
                ContentKind.Weapon => ReadWeapon(root, recordId, provenance, parent, legacyBinding, unclassifiedList, source),
                ContentKind.Armor => ReadArmor(root, recordId, provenance, parent, legacyBinding, unclassifiedList, source),
                ContentKind.Locomotor => ReadLocomotor(root, recordId, provenance, parent, legacyBinding, unclassifiedList, source),
                ContentKind.Upgrade => ReadUpgrade(root, recordId, provenance, parent, legacyBinding, unclassifiedList, source),
                ContentKind.Ability => ReadAbility(root, recordId, provenance, parent, legacyBinding, unclassifiedList, source),
                ContentKind.Projectile => ReadProjectile(root, recordId, provenance, parent, legacyBinding, unclassifiedList, source),
                ContentKind.VisualState => ReadVisualState(root, recordId, provenance, parent, legacyBinding, unclassifiedList, source),
                _ => throw new ArgumentOutOfRangeException(nameof(expectedKind), expectedKind, null),
            };

            return createdRecord;
        }
    }

    private static HashSet<string> GetKnownProperties(ContentKind kind) => kind switch
    {
        ContentKind.Unit => KnownUnitProperties,
        ContentKind.Weapon => KnownWeaponProperties,
        ContentKind.Armor => KnownArmorProperties,
        ContentKind.Locomotor => KnownLocomotorProperties,
        ContentKind.Upgrade => KnownUpgradeProperties,
        ContentKind.Ability => KnownAbilityProperties,
        ContentKind.Projectile => KnownProjectileProperties,
        ContentKind.VisualState => KnownVisualStateProperties,
        _ => CommonRecordProperties,
    };

    private static UnitRecord ReadUnit(
        JsonElement root,
        ContentId id,
        Provenance provenance,
        ContentId? parent,
        LegacyBinding? legacyBinding,
        IReadOnlyList<UnclassifiedField> unclassified,
        ContentSource source,
        RetainedIniRebuildResult? retainedNatives,
        List<ImportDiagnostic> diagnostics)
    {
        var modulesList = new List<ModuleEdit>();

        if (root.TryGetProperty("modules", out var modProp) && modProp.ValueKind == JsonValueKind.Array)
        {
            SourceRecord? rebuiltRecord = null;
            retainedNatives?.RebuiltRecords.TryGetValue(id, out rebuiltRecord);
            var rebuiltUnit = rebuiltRecord as UnitRecord;

            foreach (var item in modProp.EnumerateArray())
            {
                var (edit, hasModuleLegacy, error) = UnitPackageModuleEditDto.TryParse(item, source);

                if (error is not null)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Error,
                        ImportStage.Read,
                        UnitPackageDiagnosticCodes.PayloadInvalid,
                        $"Unit '{id}' module error: {error}",
                        id,
                        provenance));
                    continue;
                }

                if (edit is not null)
                {
                    if (hasModuleLegacy && edit.Module is not null)
                    {
                        var rebuiltModule = rebuiltUnit?.Modules
                            .Select(m => m.Module)
                            .FirstOrDefault(m => m is not null && string.Equals(m.Tag, edit.Module.Tag, StringComparison.OrdinalIgnoreCase));

                        if (rebuiltModule?.Legacy is null)
                        {
                            diagnostics.Add(new ImportDiagnostic(
                                ImportDiagnosticSeverity.Error,
                                ImportStage.Read,
                                UnitPackageDiagnosticCodes.MissingRetainedPayload,
                                $"Module '{edit.Module.Tag}' on unit '{id}' requires a legacy binding, but no matching module was found in the retained payload.",
                                id,
                                provenance));
                        }
                        else
                        {
                            edit = edit with { Module = edit.Module with { Legacy = rebuiltModule.Legacy } };
                        }
                    }

                    modulesList.Add(edit);
                }
            }
        }

        return new UnitRecord(id, provenance)
        {
            Parent = parent,
            Legacy = legacyBinding,
            Unclassified = unclassified,
            DisplayName = root.TryGetProperty("displayName", out var dn) ? UnitPackageAuthoredHelper.ParseString(dn, source) : default,
            MaxHealth = root.TryGetProperty("maxHealth", out var mh) ? UnitPackageAuthoredHelper.ParseQuantity(mh, source) : default,
            InitialHealth = root.TryGetProperty("initialHealth", out var ih) ? UnitPackageAuthoredHelper.ParseQuantity(ih, source) : default,
            GeometryMajorRadius = root.TryGetProperty("geometryMajorRadius", out var gmr) ? UnitPackageAuthoredHelper.ParseQuantity(gmr, source) : default,
            GeometryHeight = root.TryGetProperty("geometryHeight", out var gh) ? UnitPackageAuthoredHelper.ParseQuantity(gh, source) : default,
            VisionRange = root.TryGetProperty("visionRange", out var vr) ? UnitPackageAuthoredHelper.ParseQuantity(vr, source) : default,
            DeathDuration = root.TryGetProperty("deathDuration", out var dd) ? UnitPackageAuthoredHelper.ParseQuantity(dd, source) : default,
            KindOf = root.TryGetProperty("kindOf", out var ko) ? UnitPackageListOverrideDto.ParseStringList(ko) : ListOverride<string>.Inherit,
            Locomotors = root.TryGetProperty("locomotors", out var lm) ? UnitPackageListOverrideDto.ParseContentIdList(lm, source, ContentKind.Locomotor) : ListOverride<ContentId>.Inherit,
            Weapons = root.TryGetProperty("weapons", out var wp) ? UnitPackageListOverrideDto.ParseContentIdList(wp, source, ContentKind.Weapon) : ListOverride<ContentId>.Inherit,
            Armor = root.TryGetProperty("armor", out var ar) ? UnitPackageAuthoredHelper.ParseContentId(ar, source, ContentKind.Armor) : default,
            BuildCost = root.TryGetProperty("buildCost", out var bc) ? UnitPackageAuthoredHelper.ParseQuantity(bc, source) : default,
            BuildTime = root.TryGetProperty("buildTime", out var bt) ? UnitPackageAuthoredHelper.ParseQuantity(bt, source) : default,
            Prerequisites = root.TryGetProperty("prerequisites", out var pr) ? UnitPackageListOverrideDto.ParseContentIdList(pr, source, ContentKind.Unit) : ListOverride<ContentId>.Inherit,
            Produces = root.TryGetProperty("produces", out var pd) ? UnitPackageListOverrideDto.ParseContentIdList(pd, source, ContentKind.Unit) : ListOverride<ContentId>.Inherit,
            Upgrades = root.TryGetProperty("upgrades", out var ug) ? UnitPackageListOverrideDto.ParseContentIdList(ug, source, ContentKind.Upgrade) : ListOverride<ContentId>.Inherit,
            Visual = root.TryGetProperty("visual", out var vs) ? UnitPackageAuthoredHelper.ParseContentId(vs, source, ContentKind.VisualState) : default,
            Presentation = root.TryGetProperty("presentation", out var ps) ? UnitPackageListOverrideDto.ParseContentIdList(ps, source, ContentKind.Unit) : ListOverride<ContentId>.Inherit,
            Modules = modulesList,
        };
    }

    private static WeaponRecord ReadWeapon(
        JsonElement root,
        ContentId id,
        Provenance provenance,
        ContentId? parent,
        LegacyBinding? legacyBinding,
        IReadOnlyList<UnclassifiedField> unclassified,
        ContentSource source)
    {
        return new WeaponRecord(id, provenance)
        {
            Parent = parent,
            Legacy = legacyBinding,
            Unclassified = unclassified,
            PrimaryDamage = root.TryGetProperty("primaryDamage", out var pd) ? UnitPackageAuthoredHelper.ParseQuantity(pd, source) : default,
            PrimaryDamageRadius = root.TryGetProperty("primaryDamageRadius", out var pdr) ? UnitPackageAuthoredHelper.ParseQuantity(pdr, source) : default,
            DamageType = root.TryGetProperty("damageType", out var dt) ? UnitPackageAuthoredHelper.ParseString(dt, source) : default,
            AttackRange = root.TryGetProperty("attackRange", out var ar) ? UnitPackageAuthoredHelper.ParseQuantity(ar, source) : default,
            MinimumAttackRange = root.TryGetProperty("minimumAttackRange", out var mar) ? UnitPackageAuthoredHelper.ParseQuantity(mar, source) : default,
            ReloadTime = root.TryGetProperty("reloadTime", out var rt) ? UnitPackageAuthoredHelper.ParseQuantity(rt, source) : default,
            PreAttackDelay = root.TryGetProperty("preAttackDelay", out var pad) ? UnitPackageAuthoredHelper.ParseQuantity(pad, source) : default,
            ClipSize = root.TryGetProperty("clipSize", out var cs) ? UnitPackageAuthoredHelper.ParseInt(cs, source) : default,
            ClipReloadTime = root.TryGetProperty("clipReloadTime", out var crt) ? UnitPackageAuthoredHelper.ParseQuantity(crt, source) : default,
            TargetCategories = root.TryGetProperty("targetCategories", out var tc) ? UnitPackageListOverrideDto.ParseStringList(tc) : ListOverride<string>.Inherit,
            Projectile = root.TryGetProperty("projectile", out var pj) ? UnitPackageAuthoredHelper.ParseContentId(pj, source, ContentKind.Projectile) : default,
            Presentation = root.TryGetProperty("presentation", out var ps) ? UnitPackageListOverrideDto.ParseContentIdList(ps, source, ContentKind.Unit) : ListOverride<ContentId>.Inherit,
        };
    }

    private static ArmorRecord ReadArmor(
        JsonElement root,
        ContentId id,
        Provenance provenance,
        ContentId? parent,
        LegacyBinding? legacyBinding,
        IReadOnlyList<UnclassifiedField> unclassified,
        ContentSource source)
    {
        var multipliers = new Dictionary<string, Authored<double>>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("multipliers", out var mProp) && mProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in mProp.EnumerateObject())
            {
                multipliers[prop.Name] = UnitPackageAuthoredHelper.ParseDouble(prop.Value, source);
            }
        }

        return new ArmorRecord(id, provenance)
        {
            Parent = parent,
            Legacy = legacyBinding,
            Unclassified = unclassified,
            Multipliers = multipliers,
            DefaultMultiplier = root.TryGetProperty("defaultMultiplier", out var dm) ? UnitPackageAuthoredHelper.ParseDouble(dm, source) : default,
        };
    }

    private static LocomotorRecord ReadLocomotor(
        JsonElement root,
        ContentId id,
        Provenance provenance,
        ContentId? parent,
        LegacyBinding? legacyBinding,
        IReadOnlyList<UnclassifiedField> unclassified,
        ContentSource source)
    {
        return new LocomotorRecord(id, provenance)
        {
            Parent = parent,
            Legacy = legacyBinding,
            Unclassified = unclassified,
            Speed = root.TryGetProperty("speed", out var sp) ? UnitPackageAuthoredHelper.ParseQuantity(sp, source) : default,
            TurnRate = root.TryGetProperty("turnRate", out var tr) ? UnitPackageAuthoredHelper.ParseQuantity(tr, source) : default,
            Acceleration = root.TryGetProperty("acceleration", out var ac) ? UnitPackageAuthoredHelper.ParseQuantity(ac, source) : default,
            Braking = root.TryGetProperty("braking", out var br) ? UnitPackageAuthoredHelper.ParseQuantity(br, source) : default,
            PreferredHeight = root.TryGetProperty("preferredHeight", out var ph) ? UnitPackageAuthoredHelper.ParseQuantity(ph, source) : default,
            CanHover = root.TryGetProperty("canHover", out var ch) ? UnitPackageAuthoredHelper.ParseBool(ch, source) : default,
            Appearance = root.TryGetProperty("appearance", out var ap) ? UnitPackageAuthoredHelper.ParseString(ap, source) : default,
            Surfaces = root.TryGetProperty("surfaces", out var sf) ? UnitPackageListOverrideDto.ParseStringList(sf) : ListOverride<string>.Inherit,
        };
    }

    private static UpgradeRecord ReadUpgrade(
        JsonElement root,
        ContentId id,
        Provenance provenance,
        ContentId? parent,
        LegacyBinding? legacyBinding,
        IReadOnlyList<UnclassifiedField> unclassified,
        ContentSource source)
    {
        return new UpgradeRecord(id, provenance)
        {
            Parent = parent,
            Legacy = legacyBinding,
            Unclassified = unclassified,
            DisplayName = root.TryGetProperty("displayName", out var dn) ? UnitPackageAuthoredHelper.ParseString(dn, source) : default,
            BuildCost = root.TryGetProperty("buildCost", out var bc) ? UnitPackageAuthoredHelper.ParseQuantity(bc, source) : default,
            BuildTime = root.TryGetProperty("buildTime", out var bt) ? UnitPackageAuthoredHelper.ParseQuantity(bt, source) : default,
            Scope = root.TryGetProperty("scope", out var sc) ? UnitPackageAuthoredHelper.ParseString(sc, source) : default,
            Prerequisites = root.TryGetProperty("prerequisites", out var pr) ? UnitPackageListOverrideDto.ParseContentIdList(pr, source, ContentKind.Upgrade) : ListOverride<ContentId>.Inherit,
        };
    }

    private static AbilityRecord ReadAbility(
        JsonElement root,
        ContentId id,
        Provenance provenance,
        ContentId? parent,
        LegacyBinding? legacyBinding,
        IReadOnlyList<UnclassifiedField> unclassified,
        ContentSource source)
    {
        return new AbilityRecord(id, provenance)
        {
            Parent = parent,
            Legacy = legacyBinding,
            Unclassified = unclassified,
            Family = root.TryGetProperty("family", out var fm) ? UnitPackageAuthoredHelper.ParseString(fm, source) : default,
            Range = root.TryGetProperty("range", out var rg) ? UnitPackageAuthoredHelper.ParseQuantity(rg, source) : default,
            Cooldown = root.TryGetProperty("cooldown", out var cd) ? UnitPackageAuthoredHelper.ParseQuantity(cd, source) : default,
            Weapon = root.TryGetProperty("weapon", out var wp) ? UnitPackageAuthoredHelper.ParseContentId(wp, source, ContentKind.Weapon) : default,
            RequiredUpgrade = root.TryGetProperty("requiredUpgrade", out var ru) ? UnitPackageAuthoredHelper.ParseContentId(ru, source, ContentKind.Upgrade) : default,
        };
    }

    private static ProjectileRecord ReadProjectile(
        JsonElement root,
        ContentId id,
        Provenance provenance,
        ContentId? parent,
        LegacyBinding? legacyBinding,
        IReadOnlyList<UnclassifiedField> unclassified,
        ContentSource source)
    {
        return new ProjectileRecord(id, provenance)
        {
            Parent = parent,
            Legacy = legacyBinding,
            Unclassified = unclassified,
            Speed = root.TryGetProperty("speed", out var sp) ? UnitPackageAuthoredHelper.ParseQuantity(sp, source) : default,
            Visual = root.TryGetProperty("visual", out var vs) ? UnitPackageAuthoredHelper.ParseContentId(vs, source, ContentKind.VisualState) : default,
        };
    }

    private static VisualStateRecord ReadVisualState(
        JsonElement root,
        ContentId id,
        Provenance provenance,
        ContentId? parent,
        LegacyBinding? legacyBinding,
        IReadOnlyList<UnclassifiedField> unclassified,
        ContentSource source)
    {
        var animations = new Dictionary<string, Authored<string>>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("stateAnimations", out var saProp) && saProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in saProp.EnumerateObject())
            {
                animations[prop.Name] = UnitPackageAuthoredHelper.ParseString(prop.Value, source);
            }
        }

        return new VisualStateRecord(id, provenance)
        {
            Parent = parent,
            Legacy = legacyBinding,
            Unclassified = unclassified,
            ModelKey = root.TryGetProperty("modelKey", out var mk) ? UnitPackageAuthoredHelper.ParseString(mk, source) : default,
            StateAnimations = animations,
        };
    }
}
