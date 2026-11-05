using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Panopticon.Geospatial.Features;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>Generated-feature count for one <see cref="GeneratedFeatureKind"/>.</summary>
public readonly record struct PayloadFeatureKindCount(GeneratedFeatureKind Kind, long Count);

/// <summary>
/// Generated-feature summary: the total count plus one count per feature kind in enum
/// order, summing to the total.
/// </summary>
public sealed record PayloadFeatureGenerationSummary
{
    public PayloadFeatureGenerationSummary(long totalCount, IReadOnlyList<PayloadFeatureKindCount> countsByKind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentNullException.ThrowIfNull(countsByKind);

        var expectedKinds = Enum.GetValues<GeneratedFeatureKind>();
        if (countsByKind.Count != expectedKinds.Length)
        {
            throw new ArgumentException(
                $"The kind count list must hold exactly one entry per feature kind ({expectedKinds.Length}); found {countsByKind.Count}.",
                nameof(countsByKind));
        }

        var copy = new PayloadFeatureKindCount[countsByKind.Count];
        var sum = 0L;
        for (var index = 0; index < countsByKind.Count; index++)
        {
            var entry = countsByKind[index];
            if (!Enum.IsDefined(entry.Kind))
            {
                throw new ArgumentException($"Undefined feature kind '{entry.Kind}'.", nameof(countsByKind));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(entry.Count, nameof(countsByKind));

            if (index > 0 && (int)entry.Kind <= (int)copy[index - 1].Kind)
            {
                throw new ArgumentException(
                    "The kind count list must list every feature kind exactly once, in enum order.",
                    nameof(countsByKind));
            }

            // Difference guard instead of a running sum: long.MaxValue counts must not wrap.
            if (entry.Count > totalCount - sum)
            {
                throw new ArgumentException(
                    $"The per-kind counts exceed the declared total {totalCount}.",
                    nameof(countsByKind));
            }

            copy[index] = entry;
            sum += entry.Count;
        }

        if (sum != totalCount)
        {
            throw new ArgumentException(
                $"The per-kind counts sum to {sum}, not the declared total {totalCount}.",
                nameof(countsByKind));
        }

        TotalCount = totalCount;
        CountsByKind = new ReadOnlyCollection<PayloadFeatureKindCount>(copy);
    }

    public long TotalCount { get; }

    /// <summary>One entry per <see cref="GeneratedFeatureKind"/> in enum order.</summary>
    public IReadOnlyList<PayloadFeatureKindCount> CountsByKind { get; }
}
