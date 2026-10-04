#nullable enable

using System;

namespace Panopticon.Simulation;

/// <summary>
/// One keyed domain state observation at one frame. <see cref="Integers"/>, <see cref="Reals"/>,
/// and <see cref="References"/> hold the kind's fields in declaration order; a record whose
/// vector lengths disagree with its kind is invalid and the recorder throws on capture.
/// </summary>
public sealed class DomainStateRecord : IPersistableObject
{
    public string Key { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    /// <summary>The per-player (or per-owner) index the record belongs to.</summary>
    public int OwnerIndex { get; set; }

    public long[] Integers { get; set; } = Array.Empty<long>();

    public double[] Reals { get; set; } = Array.Empty<double>();

    /// <summary>Stable keys named by the kind's <see cref="DomainRecordKind.ReferenceNames"/>.</summary>
    public string[] References { get; set; } = Array.Empty<string>();

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var key = Key;
        persister.PersistAsciiString(ref key, "Key");
        Key = key;

        var kind = Kind;
        persister.PersistAsciiString(ref kind, "Kind");
        Kind = kind;

        var ownerIndex = OwnerIndex;
        persister.PersistInt32(ref ownerIndex, "OwnerIndex");
        OwnerIndex = ownerIndex;

        var integers = Integers;
        PersistIntegers(persister, ref integers);
        Integers = integers;

        var reals = Reals;
        PersistReals(persister, ref reals);
        Reals = reals;

        var references = References;
        PersistReferences(persister, ref references);
        References = references;
    }

    // Read mode replaces the whole array so a shorter persisted vector cannot
    // leave stale elements from a previously populated record behind.
    private static void PersistIntegers(StatePersister persister, ref long[] values)
    {
        var count = (uint)values.Length;
        persister.PersistUInt32(ref count, "IntegerCount");

        if (persister.Mode == StatePersistMode.Read)
        {
            var read = new long[count];
            for (var i = 0; i < count; i++)
            {
                var value = 0L;
                persister.PersistInt64(ref value, "Integer");
                read[i] = value;
            }

            values = read;
        }
        else
        {
            foreach (var element in values)
            {
                var value = element;
                persister.PersistInt64(ref value, "Integer");
            }
        }
    }

    private static void PersistReals(StatePersister persister, ref double[] values)
    {
        var count = (uint)values.Length;
        persister.PersistUInt32(ref count, "RealCount");

        if (persister.Mode == StatePersistMode.Read)
        {
            var read = new double[count];
            for (var i = 0; i < count; i++)
            {
                var value = 0d;
                persister.PersistDouble(ref value, "Real");
                read[i] = value;
            }

            values = read;
        }
        else
        {
            foreach (var element in values)
            {
                var value = element;
                persister.PersistDouble(ref value, "Real");
            }
        }
    }

    private static void PersistReferences(StatePersister persister, ref string[] values)
    {
        var count = (uint)values.Length;
        persister.PersistUInt32(ref count, "ReferenceCount");

        if (persister.Mode == StatePersistMode.Read)
        {
            var read = new string[count];
            for (var i = 0; i < count; i++)
            {
                var value = string.Empty;
                persister.PersistAsciiString(ref value, "Reference");
                read[i] = value;
            }

            values = read;
        }
        else
        {
            foreach (var element in values)
            {
                var value = element;
                persister.PersistAsciiString(ref value, "Reference");
            }
        }
    }
}
