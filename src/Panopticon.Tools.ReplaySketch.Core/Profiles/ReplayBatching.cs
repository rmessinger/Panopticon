#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Simulation;

namespace Panopticon.Tools.ReplaySketch.Profiles;

/// <summary>Visual detail level for a replay point, selected by projected screen size (WI-A5-25).</summary>
public enum ReplayLodLevel
{
    /// <summary>Full point with the current sizing; eligible for the selection ring.</summary>
    Full,

    /// <summary>Small fixed dot.</summary>
    Dot,

    /// <summary>Single-pixel marker for dense far-out views.</summary>
    Pixel,
}

/// <summary>
/// Documented LOD thresholds. Screen size is the point's projected diameter in pixels. The
/// screen-space gate mirrors B5's ModelLod culling contract (a size below the floor drops to the
/// cheapest representation); thresholds are replay-specific and recorded here.
/// </summary>
public sealed record ReplayLodOptions(float FullMinPx = 6f, float DotMinPx = 2f)
{
    public static ReplayLodOptions Default { get; } = new();

    public ReplayLodLevel Select(float screenPx)
    {
        if (screenPx >= FullMinPx)
        {
            return ReplayLodLevel.Full;
        }

        return screenPx >= DotMinPx ? ReplayLodLevel.Dot : ReplayLodLevel.Pixel;
    }
}

/// <summary>
/// One draw submission for a group of repeated entities. Mirrors B5's InstancePool contract:
/// every entity of a (owner, kind, LOD) group appends into its batch and the batch records one
/// submission; no B5 GPU infrastructure is forked.
/// </summary>
public sealed record ReplayPrimitiveBatch(string Key, ReplayLodLevel Lod, IReadOnlyList<DomainVisualPoint> Points);

/// <summary>Headlessly assertable batch and LOD accounting.</summary>
public sealed record ReplayBatchStats(int BatchCount, int FullCount, int DotCount, int PixelCount);

/// <summary>Batch variant carrying point indices so renderers keep marker alignment.</summary>
internal sealed record ReplayIndexedBatch(string Key, ReplayLodLevel Lod, IReadOnlyList<DomainVisualPoint> Points, IReadOnlyList<int> Indices);

public static class ReplayPrimitiveBatcher
{
    public static (IReadOnlyList<ReplayPrimitiveBatch> Batches, ReplayBatchStats Stats) BuildBatches(
        IReadOnlyList<DomainVisualPoint> points,
        float worldToScreenScale,
        ReplayLodOptions? options = null)
    {
        var (indexedBatches, stats) = BuildIndexedBatches(points, worldToScreenScale, options);
        var batches = indexedBatches.Select(b => new ReplayPrimitiveBatch(b.Key, b.Lod, b.Points)).ToList();
        return (batches, stats);
    }

    internal static (IReadOnlyList<ReplayIndexedBatch> Batches, ReplayBatchStats Stats) BuildIndexedBatches(
        IReadOnlyList<DomainVisualPoint> points,
        float worldToScreenScale,
        ReplayLodOptions? options = null)
    {
        options ??= ReplayLodOptions.Default;
        var scale = worldToScreenScale > 0 ? worldToScreenScale : 1f;
        var batches = new List<ReplayIndexedBatch>();
        var byKey = new Dictionary<string, (List<DomainVisualPoint> Points, List<int> Indices)>();
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var screenPx = MathF.Max(0.5f, point.Size) * scale * 2f;
            var lod = options.Select(screenPx);
            var key = $"{point.OwnerIndex}|{point.KindName}|{lod}";
            if (!byKey.TryGetValue(key, out var entry))
            {
                entry = (new List<DomainVisualPoint>(), new List<int>());
                byKey[key] = entry;
                batches.Add(new ReplayIndexedBatch(key, lod, entry.Points, entry.Indices));
            }

            entry.Points.Add(point);
            entry.Indices.Add(i);
        }

        var stats = new ReplayBatchStats(
            batches.Count,
            batches.Count(b => b.Lod == ReplayLodLevel.Full),
            batches.Count(b => b.Lod == ReplayLodLevel.Dot),
            batches.Count(b => b.Lod == ReplayLodLevel.Pixel));
        return (batches, stats);
    }
}
