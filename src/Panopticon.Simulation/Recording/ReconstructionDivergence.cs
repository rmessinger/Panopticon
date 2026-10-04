#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Recording;

/// <summary>
/// The first reconstructed boundary whose world hash failed verification (WI-A1-22 §6.2).
/// Frames at and after this boundary were not delivered to the sink.
/// </summary>
public sealed class ReconstructionDivergence : Exception
{
    public uint BoundaryFrame { get; }

    public ulong RecordedWorldHash { get; }

    public ulong ReconstructedWorldHash { get; }

    /// <summary>
    /// Bucket keys whose recorded and reconstructed hashes differ, plus "globals" when the
    /// globals sections diverge; ordinal-sorted.
    /// </summary>
    public IReadOnlyList<string> DivergedBucketKeys { get; }

    public ReconstructionDivergence(
        uint boundaryFrame,
        ulong recordedWorldHash,
        ulong reconstructedWorldHash,
        IReadOnlyList<string> divergedBucketKeys)
        : base(
            $"Reconstruction diverged at boundary {boundaryFrame}: recorded world hash {recordedWorldHash:X16},"
            + $" reconstructed {reconstructedWorldHash:X16}; diverged: {string.Join(", ", divergedBucketKeys)}")
    {
        BoundaryFrame = boundaryFrame;
        RecordedWorldHash = recordedWorldHash;
        ReconstructedWorldHash = reconstructedWorldHash;
        DivergedBucketKeys = divergedBucketKeys;
    }

    /// <summary>Bucket-level differences between a recorded and a reconstructed hash set.</summary>
    internal static IReadOnlyList<string> FindDivergedBuckets(OperationalStateHashes recorded, OperationalStateHashes reconstructed)
    {
        var diverged = new SortedSet<string>();
        if (recorded.GlobalsHash != reconstructed.GlobalsHash)
        {
            diverged.Add("globals");
        }

        foreach (var key in recorded.Buckets.Keys.Concat(reconstructed.Buckets.Keys).Distinct())
        {
            var recordedHash = recorded.Buckets.TryGetValue(key, out var recordedValue) ? recordedValue : 0UL;
            var reconstructedHash = reconstructed.Buckets.TryGetValue(key, out var reconstructedValue) ? reconstructedValue : 0UL;
            if (recordedHash != reconstructedHash)
            {
                diverged.Add(key);
            }
        }

        return diverged.ToList();
    }
}
