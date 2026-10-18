#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>One diverged bucket at the first divergent frame (WI-A5-05).</summary>
/// <param name="Path">Node path from the root, e.g. <c>world/cell:2,3</c>.</param>
/// <param name="Reason">Whether the bucket hash differs, or the bucket is missing on one side.</param>
public sealed record DivergenceLocus(
    uint Frame,
    string Path,
    ulong ReferenceHash,
    ulong CurrentHash,
    string Reason);

/// <summary>
/// First-divergence report for two recorded runs (WI-A5-05). Comparison is diagnostic only,
/// never a correctness gate; incompatible recording configurations make runs uncomparable
/// rather than divergent.
/// </summary>
public sealed record RunDivergenceReport(
    bool Comparable,
    string? IncompatibilityReason,
    uint? FirstDivergentFrame,
    IReadOnlyList<DivergenceLocus> Loci,
    uint FramesCompared,
    uint ReferenceLastFrame,
    uint CurrentLastFrame)
{
    public static RunDivergenceReport NoDivergence(uint framesCompared, uint referenceLastFrame, uint currentLastFrame)
        => new(true, null, null, Array.Empty<DivergenceLocus>(), framesCompared, referenceLastFrame, currentLastFrame);
}

/// <summary>
/// WI-A5-05: walks two runs' stored T1 signature trees frame by frame and reports the first
/// frame and leaf-most buckets whose hashes differ. Verifies recording configuration
/// compatibility first; a missing or extra bucket under a compatible schema is a divergence.
/// </summary>
public static class RunDivergenceComparator
{
    public static RunDivergenceReport Compare(RunOutput reference, RunOutput current)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(current);

        var referenceLast = reference.SignatureTrees.Count == 0 ? 0 : reference.SignatureTrees[^1].Frame;
        var currentLast = current.SignatureTrees.Count == 0 ? 0 : current.SignatureTrees[^1].Frame;

        try
        {
            reference.ValidateSignatureConfigurationCompatible(current);
        }
        catch (InvalidOperationException ex)
        {
            return new RunDivergenceReport(false, ex.Message, null, Array.Empty<DivergenceLocus>(), 0, referenceLast, currentLast);
        }

        if (!string.Equals(reference.DomainId ?? "rts", current.DomainId ?? "rts", StringComparison.Ordinal))
        {
            return new RunDivergenceReport(
                false,
                $"Runs {reference.RunId} and {current.RunId} recorded different domains "
                + $"('{reference.DomainId ?? "rts"}' vs '{current.DomainId ?? "rts"}'); their trees are not comparable.",
                null,
                Array.Empty<DivergenceLocus>(),
                0,
                referenceLast,
                currentLast);
        }

        var referenceByFrame = IndexByFrame(reference);
        var currentByFrame = IndexByFrame(current);

        foreach (var frame in referenceByFrame.Keys.Union(currentByFrame.Keys).Order())
        {
            var hasReference = referenceByFrame.TryGetValue(frame, out var referenceTree);
            var hasCurrent = currentByFrame.TryGetValue(frame, out var currentTree);

            if (hasReference && hasCurrent)
            {
                var loci = CompareTrees(frame, referenceTree!, currentTree!);
                if (loci.Count > 0)
                {
                    return new RunDivergenceReport(true, null, frame, loci, frame + 1, referenceLast, currentLast);
                }

                continue;
            }

            // Frame recorded on only one side: compare as diverged at the world root.
            var missingSide = hasReference ? current.RunId : reference.RunId;
            var rootLocus = new DivergenceLocus(
                frame,
                "world",
                hasReference ? RootHashOf(referenceTree!) : 0UL,
                hasCurrent ? RootHashOf(currentTree!) : 0UL,
                $"frame recorded only in the other run (missing from {missingSide})");
            return new RunDivergenceReport(
                true,
                null,
                frame,
                new[] { rootLocus },
                frame,
                referenceLast,
                currentLast);
        }

        return RunDivergenceReport.NoDivergence(
            (uint)referenceByFrame.Keys.Intersect(currentByFrame.Keys).Count(),
            referenceLast,
            currentLast);
    }

    private static SortedDictionary<uint, SignatureTreeSample> IndexByFrame(RunOutput run)
    {
        var index = new SortedDictionary<uint, SignatureTreeSample>();
        foreach (var sample in run.SignatureTrees)
        {
            index[sample.Frame] = sample;
        }

        return index;
    }

    private static List<DivergenceLocus> CompareTrees(uint frame, SignatureTreeSample reference, SignatureTreeSample current)
    {
        var referenceByKey = reference.Nodes.ToDictionary(n => n.Key);
        var currentByKey = current.Nodes.ToDictionary(n => n.Key);
        var loci = new List<DivergenceLocus>();

        foreach (var (key, node) in currentByKey)
        {
            if (!referenceByKey.TryGetValue(key, out var referenceNode))
            {
                loci.Add(new DivergenceLocus(frame, PathOf(node), 0UL, node.Hash, "bucket missing in reference run"));
            }
            else if (referenceNode.Hash != node.Hash)
            {
                loci.Add(new DivergenceLocus(frame, PathOf(node), referenceNode.Hash, node.Hash, "bucket hash differs"));
            }
        }

        foreach (var (key, node) in referenceByKey)
        {
            if (!currentByKey.ContainsKey(key))
            {
                loci.Add(new DivergenceLocus(frame, PathOf(node), node.Hash, 0UL, "bucket missing in current run"));
            }
        }

        // Report the leaf-most paths: when bucket nodes diverged, the root divergence is implied.
        if (loci.Any(locus => locus.Path != "world"))
        {
            loci.RemoveAll(locus => locus.Path == "world");
        }

        loci.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return loci;
    }

    private static string PathOf(SignatureNodeRecord node) => node.Level == 0 ? node.Key : $"world/{node.Key}";

    private static ulong RootHashOf(SignatureTreeSample tree) =>
        tree.Nodes.FirstOrDefault(n => n.Level == 0)?.Hash ?? 0UL;
}
