#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Hashes checkpoint-equivalent bytes into world, bucket, and globals hashes at operational
/// boundaries. Each object is persisted through <see cref="StateWriter"/> on its own and hashed
/// with XxHash3, so any state that survives a checkpoint is verified. Bucket membership comes
/// from the T1 level-1 topology key (spatial cell for RTS, region keys for Western Front);
/// buckets fold their member hashes commutatively and the world hash combines bucket hashes in
/// ordinal key order.
/// </summary>
public static class OperationalStateHasher
{
    /// <summary>
    /// Hashes a host's state of record at a boundary exactly as the operational recorder hashed
    /// it when writing the bundle (WI-A1-21/WI-A1-22 share this path so the two can never
    /// drift). Bucket keys follow the domain's state surface: spatial cells over the world
    /// summary without a projection (T1's RTS strategy), state-record layers with one.
    /// </summary>
    internal static OperationalStateHashes HashHostState(
        SimulationHost host,
        IGame game,
        IDomainStateProjection? projection,
        IReconstructableDomain? reconstructable,
        WorldStateSummary summary)
    {
        if (projection is not null)
        {
            var schema = projection.StateSchema;
            var frame = projection.CaptureState(summary.Frame);
            var records = frame.Records.Select(r =>
                ("layer:" + schema.FindKind(r.Kind).Layer, HashPersisted(r, game)));
            return Build(records, HashHostSections(host, reconstructable));
        }

        var worldObjects = summary.Objects.Select(o =>
            (SignatureTreeBuilder.RtsCellKey(o.Position, SignatureTreeBuilder.DefaultCellSize), HashPersisted(o, game)));
        return Build(worldObjects, HashHostSections(host, reconstructable));
    }

    private static IEnumerable<(string Name, ulong Hash)> HashHostSections(
        SimulationHost host,
        IReconstructableDomain? reconstructable)
    {
        yield return (EngineCheckpointData.RandomSection, HashBytes(EngineCheckpoint.CaptureRandomSection(host)));
        yield return (EngineCheckpointData.HostSection, HashBytes(EngineCheckpoint.CaptureHostSection(host)));
        if (reconstructable is not null)
        {
            yield return (EngineCheckpointData.DomainRuntimeSection, HashBytes(EngineCheckpoint.CaptureDomainRuntimeSection(host, reconstructable)));
        }
    }

    public static ulong HashBytes(ReadOnlySpan<byte> bytes) => XxHash3.HashToUInt64(bytes);

    /// <summary>Hashes the bytes <paramref name="value"/> would contribute to a checkpoint section.</summary>
    public static ulong HashPersisted(IPersistableObject value, IGame game)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(game);

        using var stream = new MemoryStream();
        using (var writer = new StateWriter(stream, game))
        {
            writer.PersistObject(value, "State");
        }

        return XxHash3.HashToUInt64(stream.ToArray());
    }

    /// <summary>
    /// Combines per-object hashes into buckets and per-section hashes into the globals hash,
    /// then composes the world hash from the bucket hashes in ordinal key order.
    /// </summary>
    /// <param name="bucketedObjects">
    /// Pairs of T1 level-1 bucket key and per-object hash, in any order.
    /// </param>
    /// <param name="globalsSections">
    /// Pairs of section name and section hash (random, host, domain runtime, non-object chunks),
    /// in any order.
    /// </param>
    public static OperationalStateHashes Build(
        IEnumerable<(string BucketKey, ulong ObjectHash)> bucketedObjects,
        IEnumerable<(string SectionName, ulong SectionHash)> globalsSections)
    {
        var buckets = new Dictionary<string, (ulong Sum, int Count)>();
        foreach (var (key, objectHash) in bucketedObjects)
        {
            var (sum, count) = buckets.TryGetValue(key, out var existing) ? existing : (0UL, 0);
            buckets[key] = (sum + Mix(objectHash), count + 1);
        }

        var globalsSum = 0UL;
        var globalsCount = 0;
        foreach (var (name, sectionHash) in globalsSections)
        {
            globalsSum += Mix(sectionHash ^ HashString(name));
            globalsCount++;
        }

        var globalsHash = Mix(globalsSum + (ulong)globalsCount * 0xA24BAED4963EE407UL + HashString("globals"));

        // The world hash folds the globals hash first, then bucket hashes in ordinal key order,
        // so any changed section or bucket changes it.
        var worldHash = Mix(globalsHash ^ 0x9E3779B97F4A7C15UL);
        var bucketHashes = new Dictionary<string, ulong>();
        foreach (var key in buckets.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var (sum, count) = buckets[key];
            var bucketHash = Mix(sum + (ulong)count * 0x9E3779B97F4A7C15UL + HashString(key));
            bucketHashes[key] = bucketHash;
            worldHash = Mix(worldHash * 0x9E3779B97F4A7C15UL ^ Mix(bucketHash ^ HashString(key)));
        }

        return new OperationalStateHashes
        {
            WorldHash = worldHash,
            GlobalsHash = globalsHash,
            Buckets = bucketHashes,
        };
    }

    /// <summary>FNV-1a 64, matching the T1 signature tree's string hashing.</summary>
    private static ulong HashString(string value)
    {
        var hash = 0xcbf29ce484222325UL;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 0x100000001b3UL;
        }

        return hash;
    }

    /// <summary>Murmur3 64-bit finalizer: full avalanche over the combined bits.</summary>
    private static ulong Mix(ulong h)
    {
        h ^= h >> 33;
        h *= 0xff51afd7ed558ccdUL;
        h ^= h >> 33;
        h *= 0xc4ceb9fe1a85ec53UL;
        h ^= h >> 33;
        return h;
    }
}
