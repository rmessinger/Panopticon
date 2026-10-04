#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;

namespace Panopticon.Content.Importing;

/// <summary>
/// Inclusive weight range a morph target accepts; default weights must fall inside it.
/// </summary>
public readonly record struct ImportedMorphWeightRange(float Minimum, float Maximum);

/// <summary>
/// Named morph targets for one primitive: stable primitive id, ordered target names, default
/// weights, allowed weight ranges, and the delta payloads. One primitive carries at most one
/// set; the runtime applies deltas to base positions before skinning.
/// </summary>
public sealed class ImportedMorphTargetSet
{
    public const int MaximumTargetCount = 8;

    public string PrimitiveId { get; }

    public IReadOnlyList<string> TargetNames { get; }

    public IReadOnlyList<float> DefaultWeights { get; }

    public IReadOnlyList<ImportedMorphWeightRange> AllowedWeightRanges { get; }

    public IReadOnlyList<ImportedMorphTarget> Targets { get; }

    public ImportedMorphTargetSet(
        string primitiveId,
        IReadOnlyList<string> targetNames,
        IReadOnlyList<float> defaultWeights,
        IReadOnlyList<ImportedMorphWeightRange> allowedWeightRanges,
        IReadOnlyList<ImportedMorphTarget> targets)
    {
        Ensure.NotNullOrEmpty(primitiveId, "Primitive id must not be null or empty.");

        ArgumentNullException.ThrowIfNull(targetNames);

        if (targetNames.Count == 0)
        {
            throw new ArgumentException("A morph target set must name at least one target.", nameof(targetNames));
        }

        if (targetNames.Count > MaximumTargetCount)
        {
            throw new NotSupportedException(
                $"Primitive '{primitiveId}' carries {targetNames.Count} morph targets; the limit is {MaximumTargetCount}.");
        }

        if (defaultWeights is null || defaultWeights.Count != targetNames.Count)
        {
            throw new ArgumentException(
                $"Default weights must supply one entry per target ({targetNames.Count}).",
                nameof(defaultWeights));
        }

        if (allowedWeightRanges is null || allowedWeightRanges.Count != targetNames.Count)
        {
            throw new ArgumentException(
                $"Allowed weight ranges must supply one entry per target ({targetNames.Count}).",
                nameof(allowedWeightRanges));
        }

        if (targets is null || targets.Count != targetNames.Count)
        {
            throw new ArgumentException(
                $"Targets must supply one entry per name ({targetNames.Count}).",
                nameof(targets));
        }

        Ensure.NoNullElements(targets, "Target");

        for (var i = 0; i < targetNames.Count; i++)
        {
            var name = targetNames[i];
            Ensure.NotNullOrEmpty(
                name,
                $"Target name at index {i} must not be null or empty.",
                nameof(targetNames));

            for (var j = i + 1; j < targetNames.Count; j++)
            {
                if (string.Equals(name, targetNames[j], StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Target name '{name}' appears more than once; target names must be unique.",
                        nameof(targetNames));
                }
            }

            var range = allowedWeightRanges[i];
            if (!float.IsFinite(range.Minimum) || !float.IsFinite(range.Maximum) || range.Minimum > range.Maximum)
            {
                throw new ArgumentException(
                    $"Allowed weight range for target {i} must be finite and ordered.",
                    nameof(allowedWeightRanges));
            }

            var weight = defaultWeights[i];
            if (!float.IsFinite(weight))
            {
                throw new ArgumentException($"Default weight for target {i} must be finite.", nameof(defaultWeights));
            }

            if (weight < range.Minimum || weight > range.Maximum)
            {
                throw new ArgumentException(
                    $"Default weight {weight} for target {i} falls outside its allowed range [{range.Minimum}, {range.Maximum}].",
                    nameof(defaultWeights));
            }
        }

        PrimitiveId = primitiveId;
        TargetNames = targetNames.ToArray();
        DefaultWeights = defaultWeights.ToArray();
        AllowedWeightRanges = allowedWeightRanges.ToArray();
        Targets = targets.ToArray();
    }
}
