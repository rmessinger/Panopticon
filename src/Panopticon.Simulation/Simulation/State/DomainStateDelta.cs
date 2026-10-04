#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Simulation;

/// <summary>
/// Frame-to-frame change set between two <see cref="DomainStateFrame"/>s: full records for
/// every key whose content changed or appeared, keys alone for records that disappeared.
/// Reconstruction is additive: apply deltas to a baseline snapshot's records.
/// </summary>
public sealed class DomainStateDelta : IPersistableObject
{
    public uint Frame { get; set; }

    public List<DomainStateRecord> Upserted { get; } = new();

    public List<string> Removed { get; } = new();

    public static DomainStateDelta Diff(uint frame, DomainStateFrame previous, DomainStateFrame current)
    {
        var delta = new DomainStateDelta
        {
            Frame = frame,
        };

        // Both lists are sorted ascending by key (frames are ordinal by key).
        int i = 0, j = 0;
        while (i < previous.Records.Count && j < current.Records.Count)
        {
            var before = previous.Records[i];
            var after = current.Records[j];

            var order = string.CompareOrdinal(before.Key, after.Key);
            if (order == 0)
            {
                if (HasChanged(before, after))
                {
                    delta.Upserted.Add(after);
                }

                i++;
                j++;
            }
            else if (order < 0)
            {
                delta.Removed.Add(before.Key);
                i++;
            }
            else
            {
                delta.Upserted.Add(after);
                j++;
            }
        }
        while (i < previous.Records.Count)
        {
            delta.Removed.Add(previous.Records[i].Key);
            i++;
        }
        while (j < current.Records.Count)
        {
            delta.Upserted.Add(current.Records[j]);
            j++;
        }

        return delta;
    }

    private static bool HasChanged(DomainStateRecord before, DomainStateRecord after) =>
        before.Kind != after.Kind
        || before.OwnerIndex != after.OwnerIndex
        || !IntegersEqual(before.Integers, after.Integers)
        || !RealsEqual(before.Reals, after.Reals)
        || !ReferencesEqual(before.References, after.References);

    private static bool IntegersEqual(long[] before, long[] after)
    {
        if (before.Length != after.Length)
        {
            return false;
        }

        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool RealsEqual(double[] before, double[] after)
    {
        if (before.Length != after.Length)
        {
            return false;
        }

        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReferencesEqual(string[] before, string[] after)
    {
        if (before.Length != after.Length)
        {
            return false;
        }

        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies this delta to a record dictionary being reconstructed.
    /// </summary>
    internal void Apply(SortedDictionary<string, DomainStateRecord> records)
    {
        foreach (var key in Removed)
        {
            records.Remove(key);
        }

        foreach (var upserted in Upserted)
        {
            records[upserted.Key] = upserted;
        }
    }

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var frame = Frame;
        persister.PersistUInt32(ref frame, "Frame");
        Frame = frame;

        var upsertedCount = (uint)Upserted.Count;
        persister.PersistUInt32(ref upsertedCount, "UpsertedCount");

        if (persister.Mode == StatePersistMode.Read)
        {
            Upserted.Clear();
            for (var i = 0; i < upsertedCount; i++)
            {
                var record = new DomainStateRecord();
                persister.PersistObject(record, "Upserted");
                Upserted.Add(record);
            }
        }
        else
        {
            foreach (var record in Upserted)
            {
                persister.PersistObject(record, "Upserted");
            }
        }

        var removedCount = (uint)Removed.Count;
        persister.PersistUInt32(ref removedCount, "RemovedCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            Removed.Clear();
            for (var i = 0; i < removedCount; i++)
            {
                var key = string.Empty;
                persister.PersistAsciiString(ref key, "Removed");
                Removed.Add(key);
            }
        }
        else
        {
            foreach (var key in Removed)
            {
                var value = key;
                persister.PersistAsciiString(ref value, "Removed");
            }
        }
    }
}
