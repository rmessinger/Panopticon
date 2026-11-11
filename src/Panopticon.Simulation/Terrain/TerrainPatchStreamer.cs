#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Mathematics;
using Panopticon.Terrain.Cdlod;

namespace Panopticon.Terrain;

public readonly record struct TerrainPatchKey(CdlodNode Node, int MorphBand, CdlodEdges Edges);

internal readonly record struct PatchGeometrySignature(
    CdlodNode Node,
    int MorphBand,
    CdlodEdges Edges,
    int NorthNeighbourBand,
    int EastNeighbourBand,
    int SouthNeighbourBand,
    int WestNeighbourBand);

public class TerrainPatchStreamer<TPatch> : IDisposable where TPatch : class
{
    public const int MorphBands = 8;
    public const int BytesPerResidentNode = 9248;

    private readonly Func<CdlodSelectedNode, int, TPatch> _createPatch;
    private readonly Action<TPatch> _releasePatch;
    private readonly Dictionary<CdlodNode, ResidentEntry> _resident = new();
    private bool _disposed;

    public int ResidentCount => _resident.Count;

    public long ResidentVertexBytes => (long)_resident.Count * BytesPerResidentNode;

    public int CreatedTotal { get; private set; }

    public int ReleasedTotal { get; private set; }

    public TerrainPatchStreamer(
        Func<CdlodSelectedNode, int, TPatch> createPatch,
        Action<TPatch> releasePatch)
    {
        ArgumentNullException.ThrowIfNull(createPatch);
        ArgumentNullException.ThrowIfNull(releasePatch);

        _createPatch = createPatch;
        _releasePatch = releasePatch;
    }

    public static int QuantizeMorphBand(float morphFactor)
    {
        var clamped = Math.Clamp(morphFactor, 0f, 1f);
        return (int)MathF.Round(clamped * MorphBands);
    }

    public static float QuantizeMorph(int band) => band / (float)MorphBands;

    public void Update(CdlodSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }

        var selectedNodes = selection.Nodes;

        var bandMap = new Dictionary<CdlodNode, int>(selectedNodes.Count);
        for (var i = 0; i < selectedNodes.Count; i++)
        {
            var item = selectedNodes[i];
            bandMap[item.Node] = QuantizeMorphBand(item.MorphFactor);
        }

        var desiredNodes = new HashSet<CdlodNode>(selectedNodes.Count);

        for (var i = 0; i < selectedNodes.Count; i++)
        {
            var selected = selectedNodes[i];
            desiredNodes.Add(selected.Node);

            var band = bandMap[selected.Node];

            var northBand = -1;
            var eastBand = -1;
            var southBand = -1;
            var westBand = -1;

            if (selected.CoarserEdges.HasFlag(CdlodEdges.North))
            {
                var coarseNode = TerrainPatchGeometry.GetCoarseNeighbour(selected.Node, CdlodEdges.North);
                if (bandMap.TryGetValue(coarseNode, out var b))
                {
                    northBand = b;
                }
            }

            if (selected.CoarserEdges.HasFlag(CdlodEdges.East))
            {
                var coarseNode = TerrainPatchGeometry.GetCoarseNeighbour(selected.Node, CdlodEdges.East);
                if (bandMap.TryGetValue(coarseNode, out var b))
                {
                    eastBand = b;
                }
            }

            if (selected.CoarserEdges.HasFlag(CdlodEdges.South))
            {
                var coarseNode = TerrainPatchGeometry.GetCoarseNeighbour(selected.Node, CdlodEdges.South);
                if (bandMap.TryGetValue(coarseNode, out var b))
                {
                    southBand = b;
                }
            }

            if (selected.CoarserEdges.HasFlag(CdlodEdges.West))
            {
                var coarseNode = TerrainPatchGeometry.GetCoarseNeighbour(selected.Node, CdlodEdges.West);
                if (bandMap.TryGetValue(coarseNode, out var b))
                {
                    westBand = b;
                }
            }

            var key = new TerrainPatchKey(selected.Node, band, selected.CoarserEdges);
            var signature = new PatchGeometrySignature(
                selected.Node,
                band,
                selected.CoarserEdges,
                northBand,
                eastBand,
                southBand,
                westBand);

            if (_resident.TryGetValue(selected.Node, out var existing))
            {
                if (existing.Signature == signature)
                {
                    continue;
                }

                _releasePatch(existing.Patch);
                ReleasedTotal++;

                var rebuiltPatch = _createPatch(selected, band);
                CreatedTotal++;
                _resident[selected.Node] = new ResidentEntry(rebuiltPatch, key, signature);
            }
            else
            {
                var newPatch = _createPatch(selected, band);
                CreatedTotal++;
                _resident[selected.Node] = new ResidentEntry(newPatch, key, signature);
            }
        }

        List<CdlodNode>? exited = null;
        foreach (var (node, entry) in _resident)
        {
            if (!desiredNodes.Contains(node))
            {
                exited ??= new List<CdlodNode>();
                exited.Add(node);
            }
        }

        if (exited != null)
        {
            for (var i = 0; i < exited.Count; i++)
            {
                var node = exited[i];
                var entry = _resident[node];
                _resident.Remove(node);
                _releasePatch(entry.Patch);
                ReleasedTotal++;
            }
        }
    }

    public void ForgetIntersecting(Rectangle latticeBounds, int latticeWidth, int latticeHeight)
    {
        List<CdlodNode>? forgotten = null;
        foreach (var (node, _) in _resident)
        {
            var extent = node.ClippedExtent(latticeWidth, latticeHeight);
            if (extent.Left < latticeBounds.Right &&
                latticeBounds.Left < extent.Right &&
                extent.Top < latticeBounds.Bottom &&
                latticeBounds.Top < extent.Bottom)
            {
                forgotten ??= new List<CdlodNode>();
                forgotten.Add(node);
            }
        }

        if (forgotten == null)
        {
            return;
        }

        for (var i = 0; i < forgotten.Count; i++)
        {
            var entry = _resident[forgotten[i]];
            _resident.Remove(forgotten[i]);
            _releasePatch(entry.Patch);
            ReleasedTotal++;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _resident.Values)
        {
            _releasePatch(entry.Patch);
            ReleasedTotal++;
        }

        _resident.Clear();
    }

    private readonly record struct ResidentEntry(TPatch Patch, TerrainPatchKey Key, PatchGeometrySignature Signature);
}

public sealed class TerrainPatchStreamer : TerrainPatchStreamer<TerrainPatch>
{
    public TerrainPatchStreamer(
        Func<CdlodSelectedNode, int, TerrainPatch> createPatch,
        Action<TerrainPatch> releasePatch)
        : base(createPatch, releasePatch)
    {
    }
}

public sealed class CachedHeightRangeProvider : ICdlodErrorProvider
{
    private readonly HeightMap _heightMap;
    private readonly Dictionary<CdlodNode, (float Min, float Max)> _cache = new();

    public int CachedCount => _cache.Count;

    public int CacheCount => _cache.Count;

    public CachedHeightRangeProvider(HeightMap heightMap)
    {
        ArgumentNullException.ThrowIfNull(heightMap);
        _heightMap = heightMap;
    }

    public (float Min, float Max) GetHeightRange(CdlodNode node)
    {
        if (_cache.TryGetValue(node, out var range))
        {
            return range;
        }

        var extent = node.ClippedExtent(_heightMap.Width, _heightMap.Height);
        if (extent.Width <= 0 || extent.Height <= 0)
        {
            return (0f, 0f);
        }

        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;

        var startX = extent.X;
        var endX = extent.Right;
        var startY = extent.Y;
        var endY = extent.Bottom;

        for (var y = startY; y < endY; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                var h = _heightMap.GetHeight(x, y);
                if (h < min)
                {
                    min = h;
                }

                if (h > max)
                {
                    max = h;
                }
            }
        }

        if (float.IsInfinity(min))
        {
            min = 0f;
            max = 0f;
        }

        var result = (min, max);
        _cache[node] = result;
        return result;
    }

    /// <summary>
    /// Invalidates cached height ranges for nodes whose footprint intersects <paramref name="latticeBounds"/>.
    /// In a quadtree, any ancestor of an intersecting node contains that node's footprint and therefore also
    /// intersects <paramref name="latticeBounds"/>, so dropping intersecting nodes drops all affected descendants
    /// and ancestors conservatively without extra tree traversal.
    /// </summary>
    public void Invalidate(Rectangle latticeBounds)
    {
        List<CdlodNode>? toRemove = null;
        foreach (var (node, _) in _cache)
        {
            var extent = node.ClippedExtent(_heightMap.Width, _heightMap.Height);
            if (Intersects(extent, latticeBounds))
            {
                toRemove ??= new List<CdlodNode>();
                toRemove.Add(node);
            }
        }

        if (toRemove != null)
        {
            for (var i = 0; i < toRemove.Count; i++)
            {
                _cache.Remove(toRemove[i]);
            }
        }
    }

    public void Clear()
    {
        _cache.Clear();
    }

    private static bool Intersects(in Rectangle a, in Rectangle b)
    {
        if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
        {
            return a.Left <= b.Right &&
                   b.Left <= a.Right &&
                   a.Top <= b.Bottom &&
                   b.Top <= a.Bottom;
        }

        return a.Left < b.Right &&
               b.Left < a.Right &&
               a.Top < b.Bottom &&
               b.Top < a.Bottom;
    }
}
