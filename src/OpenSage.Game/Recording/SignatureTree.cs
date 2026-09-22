#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Simulation;

namespace OpenSage.Recording;

/// <summary>
/// Per-quantity comparison tolerances for T1 signatures
/// ([a5-visualization.md §5.2](../../docs/roadmap/deep-dives/a5-visualization.md): "divergence
/// is first place drift exceeds a configured tolerance, not first non-identical bit"). In strict
/// mode values hash exactly, approaching bit-exactness for same-platform debugging.
/// </summary>
public sealed record SignatureTolerances
{
    /// <summary>World units; positions quantize to multiples of this before hashing.</summary>
    public float PositionEpsilon { get; init; } = 0.5f;

    /// <summary>Health points; health quantizes to multiples of this before hashing.</summary>
    public float HealthEpsilon { get; init; } = 1.0f;

    /// <summary>Opt-in strict-hash mode (A2 strict-hash scope Decision): no quantization.</summary>
    public bool Strict { get; init; }

    public static readonly SignatureTolerances Default = new();
}

/// <summary>
/// One node of a T1 signature tree. Level 0 is the world root; level 1 nodes are entity
/// buckets keyed by stable domain-topology addresses (spatial cells for the RTS grid domain:
/// "cell:x,y"). Node hashes are order-independent combines of member signatures, so runs that
/// iterate or partition differently still produce comparable trees.
/// </summary>
public sealed class SignatureNode
{
    public string Key { get; set; } = "";
    public int Level { get; set; }
    public ulong Hash { get; set; }
    public int MemberCount { get; set; }
}

/// <summary>
/// The T1 signature tree for one logic frame: the root node followed by its bucket nodes.
/// </summary>
public sealed class SignatureTreeFrame
{
    public uint Frame { get; set; }
    public List<SignatureNode> Nodes { get; set; } = new();

    public ulong RootHash => Nodes.Count > 0 ? Nodes[0].Hash : 0;
}

/// <summary>
/// Builds T1 signature trees from world-state summaries. Bucket keys come from the A4-1
/// domain model's topology contract; the spatial-cell strategy anchored at the world origin
/// (key "cell:x,y") is the RTS reference implementation and stays available as the legacy
/// path. Member hashes combine commutatively, so bucket and root hashes are independent of
/// member order.
/// </summary>
public static class SignatureTreeBuilder
{
    public const float DefaultCellSize = 100.0f;

    /// <summary>Domain-keyed build: bucket keys come from <paramref name="domain.TopologyKey"/>.</summary>
    public static SignatureTreeFrame Build(
        uint frame,
        WorldStateSummary summary,
        ISimulationDomainModel domain,
        SignatureTolerances? tolerances = null)
    {
        return Build(frame, ToEntities(summary), entity => domain.TopologyKey(entity), tolerances);
    }

    /// <summary>Legacy RTS build: origin-anchored spatial cells of edge <paramref name="cellSize"/>.</summary>
    public static SignatureTreeFrame Build(
        uint frame,
        WorldStateSummary summary,
        SignatureTolerances? tolerances = null,
        float cellSize = DefaultCellSize)
    {
        if (cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        return Build(
            frame,
            ToEntities(summary),
            entity => RtsCellKey(entity.Position, cellSize),
            tolerances);
    }

    /// <summary>Generic core: stable bucket keys supplied by <paramref name="topologyKeyOf"/>.</summary>
    public static SignatureTreeFrame Build(
        uint frame,
        IReadOnlyList<DomainEntitySummary> entities,
        Func<DomainEntitySummary, string> topologyKeyOf,
        SignatureTolerances? tolerances = null)
    {
        tolerances ??= SignatureTolerances.Default;

        // Bucket member hashes combine commutatively (sum of mixed hashes), so bucket and root
        // hashes are independent of member order.
        var buckets = new Dictionary<string, (ulong Sum, int Count)>();

        foreach (var entity in entities)
        {
            var entityHash = EntitySignature(entity, tolerances);
            var key = topologyKeyOf(entity);
            var (sum, count) = buckets.TryGetValue(key, out var existing)
                ? existing
                : (0UL, 0);
            buckets[key] = (sum + Mix(entityHash), count + 1);
        }

        var tree = new SignatureTreeFrame { Frame = frame };
        var rootSum = 0UL;
        var rootCount = 0;
        // Deterministic node order: sort bucket keys, not dictionary order.
        var root = new SignatureNode { Key = "world", Level = 0 };
        foreach (var ((sum, count), key) in buckets
            .Select(kv => (kv.Value, kv.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var bucketHash = Mix(sum + (ulong)count * 0x9E3779B97F4A7C15UL + HashString(key));
            tree.Nodes.Add(new SignatureNode
            {
                Key = key,
                Level = 1,
                Hash = bucketHash,
                MemberCount = count,
            });
            rootSum += Mix(bucketHash);
            rootCount++;
        }

        root.Hash = Mix(rootSum + (ulong)rootCount * 0x9E3779B97F4A7C15UL + HashString("world"));
        root.MemberCount = rootCount;
        tree.Nodes.Insert(0, root);
        return tree;
    }

    internal static string RtsCellKey(Vector3 position, float cellSize) =>
        $"cell:{(int)MathF.Floor(position.X / cellSize)},{(int)MathF.Floor(position.Y / cellSize)}";

    private static IReadOnlyList<DomainEntitySummary> ToEntities(WorldStateSummary summary)
    {
        var entities = new List<DomainEntitySummary>(summary.Objects.Count);
        foreach (var obj in summary.Objects)
        {
            entities.Add(new DomainEntitySummary(
                obj.ObjectId,
                obj.DefinitionName,
                obj.OwnerIndex,
                obj.Position,
                obj.Health,
                obj.MaxHealth));
        }

        return entities;
    }

    /// <summary>
    /// Quantized entity signature: identity plus position/scalars quantized to the configured
    /// tolerances. An extra or reordered entity draw changes its state, not the signature scheme.
    /// Field order and quantization match the pre-domain implementation exactly.
    /// </summary>
    private static ulong EntitySignature(DomainEntitySummary entity, SignatureTolerances t)
    {
        var hash = HashString(entity.KindName);
        hash = Combine(hash, (ulong)(entity.OwnerIndex + 2));
        hash = Combine(hash, Quantize(entity.Position.X, t.PositionEpsilon, t.Strict));
        hash = Combine(hash, Quantize(entity.Position.Y, t.PositionEpsilon, t.Strict));
        hash = Combine(hash, Quantize(entity.Position.Z, t.PositionEpsilon, t.Strict));
        hash = Combine(hash, Quantize(entity.ScalarA, t.HealthEpsilon, t.Strict));
        hash = Combine(hash, Quantize(entity.ScalarB, t.HealthEpsilon, t.Strict));
        return hash;
    }

    private static ulong Quantize(float value, float epsilon, bool strict)
    {
        if (strict || epsilon <= 0)
        {
            return (ulong)BitConverter.SingleToUInt32Bits(value);
        }

        return (ulong)MathF.Round(value / epsilon);
    }

    private static ulong HashString(string value)
    {
        // FNV-1a 64.
        var hash = 0xcbf29ce484222325UL;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 0x100000001b3UL;
        }

        return hash;
    }

    private static ulong Combine(ulong a, ulong b) => Mix(a * 0x9E3779B97F4A7C15UL ^ b);

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

/// <summary>
/// Compares two T1 signature trees and reports the diverged subtree keys
/// (the emission-side half of divergence localization; the comparator UI is A5-4).
/// </summary>
public static class SignatureTreeComparer
{
    /// <summary>
    /// Returns the keys of nodes whose hashes differ between the two frames at the same logic
    /// frame, or an empty list when the frames agree within tolerance. Frames missing on one
    /// side compare as diverged at the world root.
    /// </summary>
    public static IReadOnlyList<string> FindDivergedSubtrees(SignatureTreeFrame? reference, SignatureTreeFrame? current)
    {
        if (reference is null || current is null)
        {
            return new[] { "world" };
        }

        var referenceByKey = new Dictionary<string, ulong>();
        foreach (var node in reference.Nodes)
        {
            referenceByKey[node.Key] = node.Hash;
        }

        var diverged = new List<string>();
        foreach (var node in current.Nodes)
        {
            if (!referenceByKey.TryGetValue(node.Key, out var referenceHash) || referenceHash != node.Hash)
            {
                diverged.Add(node.Key);
            }
        }

        foreach (var node in reference.Nodes)
        {
            if (current.Nodes.All(n => n.Key != node.Key) && !diverged.Contains(node.Key))
            {
                diverged.Add(node.Key);
            }
        }

        diverged.Sort(StringComparer.Ordinal);
        return diverged;
    }
}
