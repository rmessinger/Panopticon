#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Mathematics;
using Panopticon.Terrain.Cdlod;

namespace Panopticon.Terrain;

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
