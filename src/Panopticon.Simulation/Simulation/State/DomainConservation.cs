#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Simulation;

/// <summary>One (family, owner) conservation total: holdings plus sinks minus sources.</summary>
public sealed record DomainConservationBalance(string Family, int OwnerIndex, long Balance);

/// <summary>A non-zero conservation difference between two frames; an empty list proves conservation.</summary>
public sealed record DomainConservationResidual(string Family, int OwnerIndex, long Difference);

/// <summary>
/// Computes per-family, per-owner conservation totals over a <see cref="DomainStateFrame"/>.
/// Schema validation guarantees every conservation field is an Integer field, so totals are exact.
/// </summary>
public static class DomainConservation
{
    /// <summary>
    /// Sums, per (family, owner), every Integer field value weighted by its role: Holding and
    /// Sink add, Source subtracts. Results are ordinal by family then owner index.
    /// </summary>
    public static IReadOnlyList<DomainConservationBalance> Balances(DomainStateSchema schema, DomainStateFrame frame)
    {
        var balances = new SortedDictionary<(string Family, int OwnerIndex), long>(BalanceKeyComparer.Instance);

        foreach (var record in frame.Records)
        {
            var kind = schema.FindKind(record.Kind);
            for (var fieldIndex = 0; fieldIndex < kind.Fields.Count; fieldIndex++)
            {
                var field = kind.Fields[fieldIndex];
                if (field.Role == DomainConservationRole.None)
                {
                    continue;
                }

                var key = (field.Family!, record.OwnerIndex);
                balances.TryGetValue(key, out var balance);
                balances[key] = field.Role switch
                {
                    DomainConservationRole.Source => balance - record.Integers[fieldIndex],
                    _ => balance + record.Integers[fieldIndex],
                };
            }
        }

        var result = new List<DomainConservationBalance>(balances.Count);
        foreach (var (key, balance) in balances)
        {
            result.Add(new DomainConservationBalance(key.Family, key.OwnerIndex, balance));
        }

        return result;
    }

    /// <summary>
    /// Returns the (family, owner) totals that changed between <paramref name="baseline"/> and
    /// <paramref name="frame"/>. An empty result means the conserved quantities are identical.
    /// </summary>
    public static IReadOnlyList<DomainConservationResidual> Residuals(
        DomainStateSchema schema,
        DomainStateFrame baseline,
        DomainStateFrame frame)
    {
        var baselineBalances = Balances(schema, baseline);
        var frameBalances = Balances(schema, frame);

        var residuals = new List<DomainConservationResidual>();

        int i = 0, j = 0;
        while (i < baselineBalances.Count && j < frameBalances.Count)
        {
            var before = baselineBalances[i];
            var after = frameBalances[j];

            var order = Compare(before, after);
            if (order == 0)
            {
                if (before.Balance != after.Balance)
                {
                    residuals.Add(new DomainConservationResidual(after.Family, after.OwnerIndex, after.Balance - before.Balance));
                }

                i++;
                j++;
            }
            else if (order < 0)
            {
                residuals.Add(new DomainConservationResidual(before.Family, before.OwnerIndex, -before.Balance));
                i++;
            }
            else
            {
                residuals.Add(new DomainConservationResidual(after.Family, after.OwnerIndex, after.Balance));
                j++;
            }
        }
        while (i < baselineBalances.Count)
        {
            var before = baselineBalances[i];
            residuals.Add(new DomainConservationResidual(before.Family, before.OwnerIndex, -before.Balance));
            i++;
        }
        while (j < frameBalances.Count)
        {
            var after = frameBalances[j];
            residuals.Add(new DomainConservationResidual(after.Family, after.OwnerIndex, after.Balance));
            j++;
        }

        return residuals;
    }

    private static int Compare(DomainConservationBalance left, DomainConservationBalance right)
    {
        var familyOrder = string.CompareOrdinal(left.Family, right.Family);
        return familyOrder != 0 ? familyOrder : left.OwnerIndex.CompareTo(right.OwnerIndex);
    }

    private sealed class BalanceKeyComparer : IComparer<(string Family, int OwnerIndex)>
    {
        public static readonly BalanceKeyComparer Instance = new();

        public int Compare((string Family, int OwnerIndex) left, (string Family, int OwnerIndex) right)
        {
            var familyOrder = string.CompareOrdinal(left.Family, right.Family);
            return familyOrder != 0 ? familyOrder : left.OwnerIndex.CompareTo(right.OwnerIndex);
        }
    }
}
