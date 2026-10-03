#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>Allowed morph-weight range for one named target.</summary>
public readonly record struct ExternalMorphWeightRange(float Minimum, float Maximum);

/// <summary>
/// Immutable importer options for one external model session: unit scale, named C6 feature
/// gates (all off by default), and per-target allowed morph-weight ranges. These values
/// affect interchange data and appear in reports; they never modify source files.
/// </summary>
public sealed class ExternalModelImportOptions
{
    public const string MorphTargetsGate = "MorphTargets";

    public float UnitsPerMeter { get; }

    public IReadOnlyDictionary<string, bool> FeatureGates { get; }

    public IReadOnlyDictionary<string, ExternalMorphWeightRange> MorphWeightRanges { get; }

    public static ExternalModelImportOptions Default { get; } = new(
        1f / 0.3048f,
        null,
        null);

    public ExternalModelImportOptions(
        float unitsPerMeter,
        IReadOnlyDictionary<string, bool>? featureGates,
        IReadOnlyDictionary<string, ExternalMorphWeightRange>? morphWeightRanges)
    {
        Ensure.FinitePositive(unitsPerMeter, "Units per meter must be a positive finite value.");

        foreach (var (name, enabled) in featureGates ?? new Dictionary<string, bool>())
        {
            Ensure.NotNullOrWhiteSpace(name, "Feature gate names must not be empty.", nameof(featureGates));
        }

        foreach (var (name, range) in morphWeightRanges ?? new Dictionary<string, ExternalMorphWeightRange>())
        {
            Ensure.NotNullOrWhiteSpace(name, "Morph target names must not be empty.", nameof(morphWeightRanges));

            if (!IsFinite(range.Minimum) || !IsFinite(range.Maximum) ||
                range.Minimum < 0 || range.Maximum > 1 || range.Minimum > range.Maximum)
            {
                throw new ArgumentException(
                    $"Morph weight range for '{name}' must be finite and ordered within [0, 1].",
                    nameof(morphWeightRanges));
            }
        }

        UnitsPerMeter = unitsPerMeter;
        FeatureGates = (featureGates ?? new Dictionary<string, bool>()).ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        MorphWeightRanges = (morphWeightRanges ?? new Dictionary<string, ExternalMorphWeightRange>()).ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    public bool IsFeatureGateEnabled(string name)
    {
        return FeatureGates.TryGetValue(name, out var enabled) && enabled;
    }

    public ExternalModelImportOptions WithUnitsPerMeter(float unitsPerMeter)
    {
        return new ExternalModelImportOptions(unitsPerMeter, FeatureGates, MorphWeightRanges);
    }

    public ExternalModelImportOptions WithFeatureGate(string name, bool enabled)
    {
        var gates = FeatureGates.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        gates[name] = enabled;
        return new ExternalModelImportOptions(UnitsPerMeter, gates, MorphWeightRanges);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
