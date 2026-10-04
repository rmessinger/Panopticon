#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Simulation;

/// <summary>
/// Rebuilds the domain state at an arbitrary frame from the latest snapshot at or before it
/// plus the deltas that follow, up to and including the frame.
/// </summary>
public static class DomainStateReconstruction
{
    public static DomainStateFrame Reconstruct(
        IReadOnlyList<DomainStateFrame> snapshots,
        IReadOnlyList<DomainStateDelta> deltas,
        uint frame)
    {
        var baselineIndex = -1;
        for (var i = 0; i < snapshots.Count; i++)
        {
            if (snapshots[i].Frame <= frame)
            {
                baselineIndex = i;
            }
            else
            {
                break;
            }
        }

        if (baselineIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), $"No snapshot at or before frame {frame}.");
        }

        var baseline = snapshots[baselineIndex];
        var records = new SortedDictionary<string, DomainStateRecord>(StringComparer.Ordinal);
        foreach (var record in baseline.Records)
        {
            records[record.Key] = record;
        }

        foreach (var delta in deltas)
        {
            if (delta.Frame <= baseline.Frame)
            {
                continue;
            }

            if (delta.Frame > frame)
            {
                break;
            }

            delta.Apply(records);
        }

        var result = new DomainStateFrame
        {
            Frame = frame,
        };
        result.Records.AddRange(records.Values);
        return result;
    }
}
