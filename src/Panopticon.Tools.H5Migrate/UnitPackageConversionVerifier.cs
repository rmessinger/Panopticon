#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;

namespace Panopticon.Tools.H5Migrate;

/// <summary>One diagnostic produced during unit package verification.</summary>
public sealed record UnitPackageConversionDiagnostic(string Code, string Message);

/// <summary>Outcome of verifying one converted unit package: success plus named diagnostics when false.</summary>
public sealed record UnitPackageConversionResult(bool Success, IReadOnlyList<UnitPackageConversionDiagnostic> Diagnostics);

/// <summary>
/// Conversion gates over a written H4 unit package (WI-H5-02): re-reads into a UnitContentSet
/// that resolves to an equal closure (records field-by-field including valued unclassified fields,
/// provenance, and retained blocks present with exact bytes; handles never compared); the roster
/// closure record count matches the resolution's.
/// </summary>
public static class UnitPackageConversionVerifier
{
    public static UnitPackageConversionResult Verify(
        UnitImportResolution expectedResolution,
        UnitContentSet reloadedSet,
        UnitImportResolution reloadedResolution,
        ILegacyNativeTable? reloadedNatives,
        IUnitPackageRetainedPayloadProvider provider,
        string packageDirectory,
        bool allowProvenanceAllowlist = false)
    {
        ArgumentNullException.ThrowIfNull(expectedResolution);
        ArgumentNullException.ThrowIfNull(reloadedSet);
        ArgumentNullException.ThrowIfNull(reloadedResolution);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var diagnostics = new List<UnitPackageConversionDiagnostic>();

        if (expectedResolution.Closure.Count != reloadedResolution.Closure.Count)
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Resolution closure count mismatch: expected {expectedResolution.Closure.Count}, got {reloadedResolution.Closure.Count}."));
        }

        if (expectedResolution.Closure.Count != reloadedSet.Records.Count)
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Reloaded set records count mismatch: expected {expectedResolution.Closure.Count}, got {reloadedSet.Records.Count}."));
        }

        foreach (var expectedRecord in expectedResolution.Closure)
        {
            var actualRecord = reloadedSet.Records.FirstOrDefault(r =>
                r.Id.Kind == expectedRecord.Id.Kind
                && expectedResolution.Source.NameComparer.Equals(r.Id.Name, expectedRecord.Id.Name));

            if (actualRecord is null)
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"Record '{expectedRecord.Id}' is missing from reloaded package."));
                continue;
            }

            VerifyRecord(expectedRecord, actualRecord, reloadedNatives, allowProvenanceAllowlist, diagnostics);
        }

        VerifyRetainedBlocks(expectedResolution, provider, packageDirectory, diagnostics);

        return new UnitPackageConversionResult(diagnostics.Count == 0, diagnostics);
    }

    private static void VerifyRecord(
        SourceRecord expected,
        SourceRecord actual,
        ILegacyNativeTable? natives,
        bool allowProvenanceAllowlist,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        if (!expected.Id.Equals(actual.Id))
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Record id mismatch: expected {expected.Id}, got {actual.Id}."));
        }

        if (!ProvenanceMatches(expected.Provenance, actual.Provenance, allowProvenanceAllowlist))
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Provenance mismatch for '{expected.Id}': expected {expected.Provenance}, got {actual.Provenance}."));
        }

        if (expected.Parent != actual.Parent)
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Parent mismatch for '{expected.Id}': expected {expected.Parent}, got {actual.Parent}."));
        }

        if ((expected.Legacy is not null) != (actual.Legacy is not null))
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Legacy binding presence mismatch for '{expected.Id}'."));
        }

        if (actual.Legacy is not null)
        {
            if (natives is null || natives.Get(actual.Legacy.Handle) is null)
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"Rebuilt legacy native missing for record '{expected.Id}'."));
            }
        }

        VerifyUnclassified(expected, actual, diagnostics);

        switch (expected)
        {
            case UnitRecord unit:
                if (actual is UnitRecord actualUnit)
                {
                    VerifyUnit(unit, actualUnit, natives, diagnostics);
                }
                else
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Record type mismatch for '{expected.Id}': expected UnitRecord, got {actual.GetType().Name}."));
                }

                break;

            case WeaponRecord weapon:
                if (actual is WeaponRecord actualWeapon)
                {
                    VerifyWeapon(weapon, actualWeapon, diagnostics);
                }
                else
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Record type mismatch for '{expected.Id}': expected WeaponRecord, got {actual.GetType().Name}."));
                }

                break;

            case ArmorRecord armor:
                if (actual is ArmorRecord actualArmor)
                {
                    VerifyArmor(armor, actualArmor, diagnostics);
                }
                else
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Record type mismatch for '{expected.Id}': expected ArmorRecord, got {actual.GetType().Name}."));
                }

                break;

            case LocomotorRecord locomotor:
                if (actual is LocomotorRecord actualLocomotor)
                {
                    VerifyLocomotor(locomotor, actualLocomotor, diagnostics);
                }
                else
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Record type mismatch for '{expected.Id}': expected LocomotorRecord, got {actual.GetType().Name}."));
                }

                break;

            case UpgradeRecord upgrade:
                if (actual is UpgradeRecord actualUpgrade)
                {
                    VerifyUpgrade(upgrade, actualUpgrade, diagnostics);
                }
                else
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Record type mismatch for '{expected.Id}': expected UpgradeRecord, got {actual.GetType().Name}."));
                }

                break;

            case AbilityRecord ability:
                if (actual is AbilityRecord actualAbility)
                {
                    VerifyAbility(ability, actualAbility, diagnostics);
                }
                else
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Record type mismatch for '{expected.Id}': expected AbilityRecord, got {actual.GetType().Name}."));
                }

                break;

            case ProjectileRecord projectile:
                if (actual is ProjectileRecord actualProjectile)
                {
                    VerifyProjectile(projectile, actualProjectile, diagnostics);
                }
                else
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Record type mismatch for '{expected.Id}': expected ProjectileRecord, got {actual.GetType().Name}."));
                }

                break;

            case VisualStateRecord visual:
                if (actual is VisualStateRecord actualVisual)
                {
                    VerifyVisualState(visual, actualVisual, diagnostics);
                }
                else
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Record type mismatch for '{expected.Id}': expected VisualStateRecord, got {actual.GetType().Name}."));
                }

                break;
        }
    }

    private static bool ProvenanceMatches(Provenance? expected, Provenance? actual, bool allowProvenanceAllowlist)
    {
        if (Equals(expected, actual))
        {
            return true;
        }

        return allowProvenanceAllowlist
            && expected is not null
            && actual is not null
            && RoundTripProvenanceAllowlist.Permits(expected, actual);
    }

    private static void VerifyUnclassified(
        SourceRecord expected,
        SourceRecord actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        if (expected.Unclassified.Count != actual.Unclassified.Count)
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Unclassified count mismatch for '{expected.Id}': expected {expected.Unclassified.Count}, got {actual.Unclassified.Count}."));
            return;
        }

        for (var i = 0; i < expected.Unclassified.Count; i++)
        {
            var exp = expected.Unclassified[i];
            var act = actual.Unclassified[i];

            if (exp.Name != act.Name
                || exp.AffectsGameplay != act.AffectsGameplay
                || exp.ExcludedByPolicy != act.ExcludedByPolicy
                || exp.RawValue != act.RawValue
                || exp.RawBlock != act.RawBlock
                || exp.Provenance != act.Provenance)
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"Unclassified field '{exp.Name}' mismatch for '{expected.Id}'."));
            }
        }
    }

    private static void VerifyUnit(
        UnitRecord expected,
        UnitRecord actual,
        ILegacyNativeTable? natives,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        VerifyAuthored(expected.Id, "DisplayName", expected.DisplayName, actual.DisplayName, diagnostics);
        VerifyAuthored(expected.Id, "MaxHealth", expected.MaxHealth, actual.MaxHealth, diagnostics);
        VerifyAuthored(expected.Id, "InitialHealth", expected.InitialHealth, actual.InitialHealth, diagnostics);
        VerifyAuthored(expected.Id, "GeometryMajorRadius", expected.GeometryMajorRadius, actual.GeometryMajorRadius, diagnostics);
        VerifyAuthored(expected.Id, "GeometryHeight", expected.GeometryHeight, actual.GeometryHeight, diagnostics);
        VerifyAuthored(expected.Id, "VisionRange", expected.VisionRange, actual.VisionRange, diagnostics);
        VerifyAuthored(expected.Id, "DeathDuration", expected.DeathDuration, actual.DeathDuration, diagnostics);
        VerifyListOverride(expected.Id, "KindOf", expected.KindOf, actual.KindOf, diagnostics);
        VerifyListOverride(expected.Id, "Locomotors", expected.Locomotors, actual.Locomotors, diagnostics);
        VerifyListOverride(expected.Id, "Weapons", expected.Weapons, actual.Weapons, diagnostics);
        VerifyAuthored(expected.Id, "Armor", expected.Armor, actual.Armor, diagnostics);
        VerifyAuthored(expected.Id, "BuildCost", expected.BuildCost, actual.BuildCost, diagnostics);
        VerifyAuthored(expected.Id, "BuildTime", expected.BuildTime, actual.BuildTime, diagnostics);
        VerifyListOverride(expected.Id, "Prerequisites", expected.Prerequisites, actual.Prerequisites, diagnostics);
        VerifyListOverride(expected.Id, "Produces", expected.Produces, actual.Produces, diagnostics);
        VerifyListOverride(expected.Id, "Upgrades", expected.Upgrades, actual.Upgrades, diagnostics);
        VerifyAuthored(expected.Id, "Visual", expected.Visual, actual.Visual, diagnostics);
        VerifyListOverride(expected.Id, "Presentation", expected.Presentation, actual.Presentation, diagnostics);
        VerifyAuthored(expected.Id, "CollisionLayer", expected.CollisionLayer, actual.CollisionLayer, diagnostics);
        VerifyListOverride(expected.Id, "CollisionMask", expected.CollisionMask, actual.CollisionMask, diagnostics);
        VerifyAuthored(expected.Id, "PhysicsMaterial", expected.PhysicsMaterial, actual.PhysicsMaterial, diagnostics);

        if (expected.Modules.Count != actual.Modules.Count)
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Module count mismatch for '{expected.Id}': expected {expected.Modules.Count}, got {actual.Modules.Count}."));
            return;
        }

        for (var i = 0; i < expected.Modules.Count; i++)
        {
            var expEdit = expected.Modules[i];
            var actEdit = actual.Modules[i];

            if (expEdit.Operation != actEdit.Operation || expEdit.Tag != actEdit.Tag)
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"ModuleEdit mismatch at index {i} for '{expected.Id}'."));
                continue;
            }

            if ((expEdit.Module is null) != (actEdit.Module is null))
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"Module nullability mismatch at index {i} for '{expected.Id}'."));
                continue;
            }

            if (expEdit.Module is { } expMod && actEdit.Module is { } actMod)
            {
                if (expMod.Tag != actMod.Tag
                    || expMod.Family != actMod.Family
                    || expMod.Kinds != actMod.Kinds
                    || expMod.InheritanceMode != actMod.InheritanceMode
                    || expMod.ExclusiveGroup != actMod.ExclusiveGroup
                    || expMod.Inherited != actMod.Inherited
                    || expMod.Ability != actMod.Ability
                    || (expMod.Legacy is not null) != (actMod.Legacy is not null))
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Module '{expMod.Tag}' mismatch for '{expected.Id}'."));
                }

                if (actMod.Legacy is not null && (natives is null || natives.Get(actMod.Legacy.Handle) is null))
                {
                    diagnostics.Add(new UnitPackageConversionDiagnostic(
                        H5MigrateCliDiagnostics.VerificationFailed,
                        $"Rebuilt legacy native missing for module '{actMod.Tag}' on '{expected.Id}'."));
                }
            }
        }
    }

    private static void VerifyWeapon(
        WeaponRecord expected,
        WeaponRecord actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        VerifyAuthored(expected.Id, "PrimaryDamage", expected.PrimaryDamage, actual.PrimaryDamage, diagnostics);
        VerifyAuthored(expected.Id, "PrimaryDamageRadius", expected.PrimaryDamageRadius, actual.PrimaryDamageRadius, diagnostics);
        VerifyAuthored(expected.Id, "DamageType", expected.DamageType, actual.DamageType, diagnostics);
        VerifyAuthored(expected.Id, "AttackRange", expected.AttackRange, actual.AttackRange, diagnostics);
        VerifyAuthored(expected.Id, "MinimumAttackRange", expected.MinimumAttackRange, actual.MinimumAttackRange, diagnostics);
        VerifyAuthored(expected.Id, "ReloadTime", expected.ReloadTime, actual.ReloadTime, diagnostics);
        VerifyAuthored(expected.Id, "PreAttackDelay", expected.PreAttackDelay, actual.PreAttackDelay, diagnostics);
        VerifyAuthored(expected.Id, "ClipSize", expected.ClipSize, actual.ClipSize, diagnostics);
        VerifyAuthored(expected.Id, "ClipReloadTime", expected.ClipReloadTime, actual.ClipReloadTime, diagnostics);
        VerifyListOverride(expected.Id, "TargetCategories", expected.TargetCategories, actual.TargetCategories, diagnostics);
        VerifyAuthored(expected.Id, "Projectile", expected.Projectile, actual.Projectile, diagnostics);
        VerifyListOverride(expected.Id, "Presentation", expected.Presentation, actual.Presentation, diagnostics);
    }

    private static void VerifyArmor(
        ArmorRecord expected,
        ArmorRecord actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        if (expected.Multipliers.Count != actual.Multipliers.Count)
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Armor multiplier count mismatch for '{expected.Id}': expected {expected.Multipliers.Count}, got {actual.Multipliers.Count}."));
            return;
        }

        foreach (var (damageType, expMult) in expected.Multipliers)
        {
            if (!actual.Multipliers.TryGetValue(damageType, out var actMult))
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"Armor multiplier for '{damageType}' missing on '{expected.Id}'."));
                continue;
            }

            VerifyAuthored(expected.Id, $"Multiplier:{damageType}", expMult, actMult, diagnostics);
        }

        VerifyAuthored(expected.Id, "DefaultMultiplier", expected.DefaultMultiplier, actual.DefaultMultiplier, diagnostics);
    }

    private static void VerifyLocomotor(
        LocomotorRecord expected,
        LocomotorRecord actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        VerifyAuthored(expected.Id, "Speed", expected.Speed, actual.Speed, diagnostics);
        VerifyAuthored(expected.Id, "TurnRate", expected.TurnRate, actual.TurnRate, diagnostics);
        VerifyAuthored(expected.Id, "Acceleration", expected.Acceleration, actual.Acceleration, diagnostics);
        VerifyAuthored(expected.Id, "Braking", expected.Braking, actual.Braking, diagnostics);
        VerifyAuthored(expected.Id, "PreferredHeight", expected.PreferredHeight, actual.PreferredHeight, diagnostics);
        VerifyAuthored(expected.Id, "CanHover", expected.CanHover, actual.CanHover, diagnostics);
        VerifyAuthored(expected.Id, "Appearance", expected.Appearance, actual.Appearance, diagnostics);
        VerifyListOverride(expected.Id, "Surfaces", expected.Surfaces, actual.Surfaces, diagnostics);
    }

    private static void VerifyUpgrade(
        UpgradeRecord expected,
        UpgradeRecord actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        VerifyAuthored(expected.Id, "DisplayName", expected.DisplayName, actual.DisplayName, diagnostics);
        VerifyAuthored(expected.Id, "BuildCost", expected.BuildCost, actual.BuildCost, diagnostics);
        VerifyAuthored(expected.Id, "BuildTime", expected.BuildTime, actual.BuildTime, diagnostics);
        VerifyAuthored(expected.Id, "Scope", expected.Scope, actual.Scope, diagnostics);
        VerifyListOverride(expected.Id, "Prerequisites", expected.Prerequisites, actual.Prerequisites, diagnostics);
    }

    private static void VerifyAbility(
        AbilityRecord expected,
        AbilityRecord actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        VerifyAuthored(expected.Id, "Family", expected.Family, actual.Family, diagnostics);
        VerifyAuthored(expected.Id, "Range", expected.Range, actual.Range, diagnostics);
        VerifyAuthored(expected.Id, "Cooldown", expected.Cooldown, actual.Cooldown, diagnostics);
        VerifyAuthored(expected.Id, "Weapon", expected.Weapon, actual.Weapon, diagnostics);
        VerifyAuthored(expected.Id, "RequiredUpgrade", expected.RequiredUpgrade, actual.RequiredUpgrade, diagnostics);
    }

    private static void VerifyProjectile(
        ProjectileRecord expected,
        ProjectileRecord actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        VerifyAuthored(expected.Id, "Speed", expected.Speed, actual.Speed, diagnostics);
        VerifyAuthored(expected.Id, "Visual", expected.Visual, actual.Visual, diagnostics);
    }

    private static void VerifyVisualState(
        VisualStateRecord expected,
        VisualStateRecord actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        VerifyAuthored(expected.Id, "ModelKey", expected.ModelKey, actual.ModelKey, diagnostics);

        if (expected.StateAnimations.Count != actual.StateAnimations.Count)
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"StateAnimation count mismatch for '{expected.Id}': expected {expected.StateAnimations.Count}, got {actual.StateAnimations.Count}."));
            return;
        }

        foreach (var (state, expAnim) in expected.StateAnimations)
        {
            if (!actual.StateAnimations.TryGetValue(state, out var actAnim))
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"StateAnimation for '{state}' missing on '{expected.Id}'."));
                continue;
            }

            VerifyAuthored(expected.Id, $"StateAnimation:{state}", expAnim, actAnim, diagnostics);
        }
    }

    private static void VerifyAuthored<T>(
        ContentId recordId,
        string fieldName,
        Authored<T> expected,
        Authored<T> actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        if (expected.IsPresent != actual.IsPresent)
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"Authored field '{fieldName}' presence mismatch for '{recordId}'."));
            return;
        }

        if (expected.IsPresent)
        {
            if (!EqualityComparer<T>.Default.Equals(expected.Value, actual.Value))
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"Authored field '{fieldName}' value mismatch for '{recordId}': expected {expected.Value}, got {actual.Value}."));
            }
        }
    }

    private static void VerifyListOverride<T>(
        ContentId recordId,
        string fieldName,
        ListOverride<T> expected,
        ListOverride<T> actual,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        if (expected.Items.Count != actual.Items.Count)
        {
            diagnostics.Add(new UnitPackageConversionDiagnostic(
                H5MigrateCliDiagnostics.VerificationFailed,
                $"ListOverride '{fieldName}' count mismatch for '{recordId}': expected {expected.Items.Count}, got {actual.Items.Count}."));
            return;
        }

        for (var i = 0; i < expected.Items.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected.Items[i], actual.Items[i]))
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"ListOverride '{fieldName}' item mismatch at index {i} for '{recordId}'."));
            }
        }
    }

    private static void VerifyRetainedBlocks(
        UnitImportResolution resolution,
        IUnitPackageRetainedPayloadProvider provider,
        string packageDirectory,
        List<UnitPackageConversionDiagnostic> diagnostics)
    {
        foreach (var record in resolution.Closure)
        {
            if (!RecordNeedsRetention(record))
            {
                continue;
            }

            var block = provider.GetBlock(new UnitRetainedBlockRequest(record.Id));
            if (block is null || block.Bytes is null || block.Bytes.Length == 0)
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.RetainedBlockMissing,
                    $"Retained block could not be sourced by provider for record '{record.Id}'."));
                continue;
            }

            var relativePath = Path.Combine("retained", KindFolder(record.Id.Kind), $"{record.Id.Name}.ini");
            var fullPath = Path.Combine(packageDirectory, relativePath);

            if (!File.Exists(fullPath))
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"Retained INI file missing from package at '{relativePath}' for record '{record.Id}'."));
                continue;
            }

            byte[] packageBytes;
            try
            {
                packageBytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"Failed to read retained INI file at '{relativePath}': {ex.Message}"));
                continue;
            }

            var expectedHash = Convert.ToHexString(SHA256.HashData(block.Bytes)).ToLowerInvariant();
            var actualHash = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();

            if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.RetainedDigestMismatch,
                    $"Retained block SHA-256 mismatch for record '{record.Id}': expected {expectedHash}, got {actualHash}."));
            }
            else if (!block.Bytes.SequenceEqual(packageBytes))
            {
                diagnostics.Add(new UnitPackageConversionDiagnostic(
                    H5MigrateCliDiagnostics.VerificationFailed,
                    $"Retained block byte mismatch for record '{record.Id}'."));
            }
        }
    }

    public static bool RecordNeedsRetention(SourceRecord record)
    {
        if (record.Legacy is not null)
        {
            return true;
        }

        return record is UnitRecord unit && unit.Modules.Any(edit => edit.Module?.Legacy is not null);
    }

    public static string KindFolder(ContentKind kind) => kind switch
    {
        ContentKind.Unit => "units",
        ContentKind.Weapon => "weapons",
        ContentKind.Armor => "armors",
        ContentKind.Locomotor => "locomotors",
        ContentKind.Upgrade => "upgrades",
        ContentKind.Ability => "abilities",
        ContentKind.Projectile => "projectiles",
        ContentKind.VisualState => "visuals",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
