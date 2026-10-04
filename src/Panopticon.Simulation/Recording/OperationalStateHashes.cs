#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Panopticon.Recording;

/// <summary>
/// The state hashes for one operational boundary: per-bucket hashes keyed by T1 level-1 topology
/// keys, a globals hash over non-object sections (random, host, domain runtime), and the world
/// hash that combines bucket hashes in ordinal key order
/// ([a1-6-operational-recording.md §5.5](../../docs/roadmap/deep-dives/a1-6-operational-recording.md)).
/// </summary>
public sealed record OperationalStateHashes
{
    public ulong WorldHash { get; init; }

    public ulong GlobalsHash { get; init; }

    public IReadOnlyDictionary<string, ulong> Buckets { get; init; } =
        new Dictionary<string, ulong>();
}
