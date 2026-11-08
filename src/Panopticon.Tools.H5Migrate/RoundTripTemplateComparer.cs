#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Logic.Object;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// Materializes the expected and reloaded resolutions and compares each template field by field (class 6).
/// Both resolutions must have no error diagnostics before materialization.
/// </summary>
internal static class RoundTripTemplateComparer
{
    public static IReadOnlyList<RoundTripFinding> Compare(
        UnitImportResolution expected,
        ILegacyNativeTable? expectedNatives,
        UnitImportResolution reloaded,
        ILegacyNativeTable? reloadedNatives)
    {
        var expectedContent = UnitRuntimeMaterializer.Materialize(expected, Context(expectedNatives));

        if (!expectedContent.Succeeded)
        {
            return OperationalFindings(expectedContent.Diagnostics);
        }

        var reloadedContent = UnitRuntimeMaterializer.Materialize(reloaded, Context(reloadedNatives));

        if (!reloadedContent.Succeeded)
        {
            return OperationalFindings(reloadedContent.Diagnostics);
        }

        var findings = new List<RoundTripFinding>();

        foreach (var (id, expectedAsset) in expectedContent.Assets.OrderBy(pair => pair.Key.Name, StringComparer.Ordinal))
        {
            if (!reloadedContent.Assets.TryGetValue(id, out var reloadedAsset))
            {
                continue;
            }

            var differences = TemplateDifferences(expectedAsset, reloadedAsset);

            if (differences.Count > 0)
            {
                findings.Add(new RoundTripFinding(
                    RoundTripFailureClassifier.CodeFor(RoundTripFailureClass.MaterializedTemplateMismatch),
                    RoundTripFailureClass.MaterializedTemplateMismatch,
                    "templates",
                    id.Name,
                    $"Materialized {expectedAsset.GetType().Name} template '{id.Name}' differs: {string.Join("; ", differences)}."));
            }
        }

        return findings;
    }

    private static UnitMaterializationContext Context(ILegacyNativeTable? natives) =>
        new(new UnitConversionPolicy(SageGame.CncGenerals), legacyNatives: natives);

    private static IReadOnlyList<RoundTripFinding> OperationalFindings(IEnumerable<ImportDiagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.IsError)
            .Select(diagnostic => RoundTripFailureClassifier.Operational(diagnostic.Code, "templates", diagnostic.Message))
            .ToList();

    private static List<string> TemplateDifferences(BaseAsset expected, BaseAsset actual)
    {
        var differences = new List<string>();

        switch (expected)
        {
            case ObjectDefinition expectedObject when actual is ObjectDefinition actualObject:
                ObjectDifferences(expectedObject, actualObject, differences);
                break;
            case WeaponTemplate expectedWeapon when actual is WeaponTemplate actualWeapon:
                WeaponDifferences(expectedWeapon, actualWeapon, differences);
                break;
            case ArmorTemplate expectedArmor when actual is ArmorTemplate actualArmor:
                ArmorDifferences(expectedArmor, actualArmor, differences);
                break;
            case LocomotorTemplate expectedLocomotor when actual is LocomotorTemplate actualLocomotor:
                LocomotorDifferences(expectedLocomotor, actualLocomotor, differences);
                break;
            case UpgradeTemplate expectedUpgrade when actual is UpgradeTemplate actualUpgrade:
                UpgradeDifferences(expectedUpgrade, actualUpgrade, differences);
                break;
            default:
                Check(differences, "type", expected.GetType().Name, actual.GetType().Name);
                break;
        }

        return differences;
    }

    private static void ObjectDifferences(ObjectDefinition expected, ObjectDefinition actual, List<string> differences)
    {
        Check(differences, "Name", expected.Name, actual.Name);
        Check(differences, "KindOf", expected.KindOf, actual.KindOf);
        Check(differences, "BuildCost", expected.BuildCost, actual.BuildCost);
        Check(differences, "BuildTime", expected.BuildTime, actual.BuildTime);
        Check(differences, "RefundValue", expected.RefundValue, actual.RefundValue);
        Check(differences, "VisionRange", expected.VisionRange, actual.VisionRange);
        Check(differences, "ShroudClearingRange", expected.ShroudClearingRange, actual.ShroudClearingRange);
        Check(differences, "GeometryMajorRadius", expected.Geometry.MajorRadius, actual.Geometry.MajorRadius);
        Check(
            differences,
            "GeometryShapeHeights",
            expected.Geometry.Shapes.Select(shape => shape.Height).ToArray(),
            actual.Geometry.Shapes.Select(shape => shape.Height).ToArray());

        Check(differences, "BehaviorCount", expected.Behaviors.Count, actual.Behaviors.Count);

        foreach (var (key, expectedModule) in expected.Behaviors)
        {
            if (!actual.Behaviors.TryGetValue(key, out var actualModule))
            {
                differences.Add($"behavior '{key}' missing");
                continue;
            }

            Check(differences, $"behavior '{key}' data", expectedModule.Data.GetType().Name, actualModule.Data.GetType().Name);
            Check(differences, $"behavior '{key}' inheritance", expectedModule.InheritanceMode, actualModule.InheritanceMode);
            Check(differences, $"behavior '{key}' kinds", expectedModule.Data.ModuleKinds, actualModule.Data.ModuleKinds);

            if (expectedModule.Data is ActiveBodyModuleData expectedBody && actualModule.Data is ActiveBodyModuleData actualBody)
            {
                Check(differences, $"behavior '{key}' max health", expectedBody.MaxHealth, actualBody.MaxHealth);
                Check(differences, $"behavior '{key}' initial health", expectedBody.InitialHealth, actualBody.InitialHealth);
            }
        }

        Check(differences, "WeaponSetCount", expected.WeaponSets.Count, actual.WeaponSets.Count);

        foreach (var (condition, expectedSet) in expected.WeaponSets)
        {
            if (!actual.WeaponSets.TryGetValue(condition, out var actualSet) || actualSet is null)
            {
                differences.Add($"weapon set '{condition}' missing");
                continue;
            }

            Check(
                differences,
                $"weapon set '{condition}' weapons",
                expectedSet.Slots.Select(slot => slot?.Weapon?.Name).ToArray(),
                actualSet.Slots.Select(slot => slot?.Weapon?.Name).ToArray());
        }

        Check(differences, "ArmorSetCount", expected.ArmorSets.Count, actual.ArmorSets.Count);
        Check(
            differences,
            "ArmorSets",
            expected.ArmorSets.Select(set => $"{set.Conditions}|{set.Armor?.Name}").ToArray(),
            actual.ArmorSets.Select(set => $"{set.Conditions}|{set.Armor?.Name}").ToArray());

        Check(differences, "LocomotorSetCount", expected.LocomotorSets.Count, actual.LocomotorSets.Count);

        foreach (var (type, expectedSet) in expected.LocomotorSets)
        {
            if (!actual.LocomotorSets.TryGetValue(type, out var actualSet) || actualSet is null)
            {
                differences.Add($"locomotor set '{type}' missing");
                continue;
            }

            Check(differences, $"locomotor set '{type}' speed", expectedSet.Speed, actualSet.Speed);
            Check(
                differences,
                $"locomotor set '{type}' locomotors",
                expectedSet.Locomotors.Select(locomotor => locomotor.Name).ToArray(),
                actualSet.Locomotors.Select(locomotor => locomotor.Name).ToArray());
        }
    }

    private static void WeaponDifferences(WeaponTemplate expected, WeaponTemplate actual, List<string> differences)
    {
        Check(differences, "Name", expected.Name, actual.Name);
        Check(differences, "AttackRange", expected.AttackRange, actual.AttackRange);
        Check(differences, "MinimumAttackRange", expected.MinimumAttackRange, actual.MinimumAttackRange);
        Check(differences, "CoolDownDelayMin", expected.CoolDownDelayBetweenShots.Min, actual.CoolDownDelayBetweenShots.Min);
        Check(differences, "CoolDownDelayMax", expected.CoolDownDelayBetweenShots.Max, actual.CoolDownDelayBetweenShots.Max);
        Check(differences, "ClipSize", expected.ClipSize, actual.ClipSize);
        Check(differences, "ClipReloadMin", expected.ClipReloadTime.Min, actual.ClipReloadTime.Min);
        Check(differences, "ClipReloadMax", expected.ClipReloadTime.Max, actual.ClipReloadTime.Max);
        Check(differences, "AutoReloadMin", expected.AutoReloadWhenIdle.Min, actual.AutoReloadWhenIdle.Min);
        Check(differences, "AutoReloadMax", expected.AutoReloadWhenIdle.Max, actual.AutoReloadWhenIdle.Max);
        Check(differences, "ContinuousFireOne", expected.ContinuousFireOne, actual.ContinuousFireOne);
        Check(differences, "WeaponSpeed", expected.WeaponSpeed, actual.WeaponSpeed);
        Check(differences, "WeaponRecoil", expected.WeaponRecoil, actual.WeaponRecoil);
        Check(differences, "LeechRangeWeapon", expected.LeechRangeWeapon, actual.LeechRangeWeapon);
        Check(
            differences,
            "DamageNuggets",
            expected.Nuggets.OfType<DamageNugget>().Select(nugget => $"{nugget.Damage}/{nugget.Radius}/{nugget.DamageType}").ToArray(),
            actual.Nuggets.OfType<DamageNugget>().Select(nugget => $"{nugget.Damage}/{nugget.Radius}/{nugget.DamageType}").ToArray());
    }

    private static void ArmorDifferences(ArmorTemplate expected, ArmorTemplate actual, List<string> differences)
    {
        Check(differences, "Name", expected.Name, actual.Name);
        Check(differences, "DamageScalar", expected.DamageScalar, actual.DamageScalar);
        Check(differences, "FlankedPenalty", expected.FlankedPenalty, actual.FlankedPenalty);
        Check(differences, "Values", expected.Values, actual.Values);
    }

    private static void LocomotorDifferences(LocomotorTemplate expected, LocomotorTemplate actual, List<string> differences)
    {
        Check(differences, "Name", expected.Name, actual.Name);
        Check(differences, "Surfaces", expected.Surfaces, actual.Surfaces);
        Check(differences, "MaxSpeed", expected.MaxSpeed, actual.MaxSpeed);
        Check(differences, "MaxSpeedDamaged", expected.MaxSpeedDamaged, actual.MaxSpeedDamaged);
        Check(differences, "MinSpeed", expected.MinSpeed, actual.MinSpeed);
        Check(differences, "MaxTurnRate", expected.MaxTurnRate, actual.MaxTurnRate);
        Check(differences, "MaxTurnRateDamaged", expected.MaxTurnRateDamaged, actual.MaxTurnRateDamaged);
        Check(differences, "Acceleration", expected.Acceleration, actual.Acceleration);
        Check(differences, "Braking", expected.Braking, actual.Braking);
        Check(differences, "Lift", expected.Lift, actual.Lift);
        Check(differences, "PreferredHeight", expected.PreferredHeight, actual.PreferredHeight);
        Check(differences, "Appearance", expected.Appearance, actual.Appearance);
        Check(differences, "BehaviorZ", expected.BehaviorZ, actual.BehaviorZ);
        Check(differences, "AllowAirborneMotiveForce", expected.AllowAirborneMotiveForce, actual.AllowAirborneMotiveForce);
    }

    private static void UpgradeDifferences(UpgradeTemplate expected, UpgradeTemplate actual, List<string> differences)
    {
        Check(differences, "Name", expected.Name, actual.Name);
        Check(differences, "Type", expected.Type, actual.Type);
        Check(differences, "BuildCost", expected.BuildCost, actual.BuildCost);
        Check(differences, "BuildTime", expected.BuildTime, actual.BuildTime);
    }

    private static void Check<T>(List<string> differences, string field, T expected, T actual)
    {
        if (!SameValue(expected, actual))
        {
            differences.Add($"{field} expected {Describe(expected)}, got {Describe(actual)}");
        }
    }

    private static bool SameValue<T>(T expected, T actual)
    {
        if (expected is string || actual is string || expected is not IEnumerable expectedItems || actual is not IEnumerable actualItems)
        {
            return EqualityComparer<T>.Default.Equals(expected, actual);
        }

        return expectedItems.Cast<object?>().SequenceEqual(actualItems.Cast<object?>());
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string text => text,
        IEnumerable items => "[" + string.Join(", ", items.Cast<object?>()) + "]",
        _ => value.ToString() ?? "null",
    };
}
