#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing;

/// <summary>
/// One morph weight track: primitive id, ordered target names, and sampled weight vectors.
/// Times are seconds; each weight vector holds one weight per target name in that order.
/// </summary>
public sealed class ImportedMorphAnimationChannel
{
    public string PrimitiveId { get; }

    public IReadOnlyList<string> TargetNames { get; }

    public IReadOnlyList<float> Times { get; }

    public IReadOnlyList<float[]> WeightVectors { get; }

    public ImportedAnimationInterpolation Interpolation { get; }

    public ImportedMorphAnimationChannel(
        string primitiveId,
        IReadOnlyList<string> targetNames,
        IReadOnlyList<float> times,
        IReadOnlyList<float[]> weightVectors,
        ImportedAnimationInterpolation interpolation = ImportedAnimationInterpolation.Linear)
    {
        if (string.IsNullOrEmpty(primitiveId))
        {
            throw new ArgumentException("Primitive id must not be null or empty.", nameof(primitiveId));
        }

        if (!Enum.IsDefined(interpolation))
        {
            throw new ArgumentOutOfRangeException(nameof(interpolation));
        }

        if (targetNames is null || targetNames.Count == 0)
        {
            throw new ArgumentException("A morph channel must name at least one target.", nameof(targetNames));
        }

        for (var i = 0; i < targetNames.Count; i++)
        {
            var name = targetNames[i];
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException($"Target name at index {i} must not be null or empty.", nameof(targetNames));
            }

            for (var j = i + 1; j < targetNames.Count; j++)
            {
                if (string.Equals(name, targetNames[j], StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Target name '{name}' appears more than once; target names must be unique.",
                        nameof(targetNames));
                }
            }
        }

        if (times is null || weightVectors is null)
        {
            throw new ArgumentNullException(times is null ? nameof(times) : nameof(weightVectors));
        }

        if (times.Count == 0 || times.Count != weightVectors.Count)
        {
            throw new ArgumentException(
                "Times and weight vectors must be non-empty and share one length.",
                nameof(times));
        }

        var previousTime = float.NegativeInfinity;
        for (var i = 0; i < times.Count; i++)
        {
            var time = times[i];
            if (!float.IsFinite(time))
            {
                throw new ArgumentException($"Time {i} must be finite.", nameof(times));
            }

            if (time < previousTime)
            {
                throw new ArgumentException($"Time {i} ({time}) precedes its predecessor; times must not decrease.", nameof(times));
            }

            previousTime = time;

            var weights = weightVectors[i];
            if (weights is null)
            {
                throw new ArgumentException($"Weight vector {i} must not be null.", nameof(weightVectors));
            }

            if (weights.Length != targetNames.Count)
            {
                throw new ArgumentException(
                    $"Weight vector {i} has {weights.Length} entries, expected one per target ({targetNames.Count}).",
                    nameof(weightVectors));
            }

            for (var j = 0; j < weights.Length; j++)
            {
                if (!float.IsFinite(weights[j]))
                {
                    throw new ArgumentException($"Weight {j} in vector {i} must be finite.", nameof(weightVectors));
                }
            }
        }

        PrimitiveId = primitiveId;
        TargetNames = targetNames.ToArray();
        Times = times.ToArray();
        WeightVectors = weightVectors.Select(vector => vector.ToArray()).ToArray();
        Interpolation = interpolation;
    }
}
