#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// World-space rectangle used to decide which replay regions are visible (WI-A5-24).
/// </summary>
public readonly record struct WorldViewport(float MinX, float MinY, float Width, float Height)
{
    public float MaxX => MinX + Width;

    public float MaxY => MinY + Height;

    public bool Contains(float x, float y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    public WorldViewport Expanded(float margin) => new(MinX - margin, MinY - margin, Width + 2 * margin, Height + 2 * margin);
}

/// <summary>Result of one streaming query: visible primitives plus bounded-decode accounting.</summary>
public sealed record ReplayStreamingQueryResult(
    uint Frame,
    IReadOnlyList<DomainVisualPrimitive> Primitives,
    IReadOnlyCollection<string> VisibleRegionKeys,
    IReadOnlyCollection<string> DecodedRegionKeys,
    int VisibleEntityCount,
    int DecodedEntityCount,
    int CachedFrames)
{
    /// <summary>True when the query fell back to a whole-frame decode (v1 artifact without a region index).</summary>
    public bool UsedWholeFrameFallback => DecodedRegionKeys.Count == 0 && Primitives.Count > 0;
}

/// <summary>
/// Card WI-A5-24: decodes and projects only the visible topology regions plus a prefetch ring
/// through the region-indexed artifact. Draw-primitive and decoded-entity counts stay bounded by
/// the viewport, not the run length. The same region-key API serves spatial viewports (cells
/// derived from the viewport rectangle) and graph topologies (explicit focus keys).
/// </summary>
public sealed class ReplayStreamingService
{
    private readonly WorldStateArtifactReader _reader;
    private readonly float _cellSize;
    private readonly int _cacheCapacity;

    // LRU of frame -> decoded region states keyed by the requested key set, so pan/zoom that
    // revisits the same visible set does not re-decode, while any change evicts on capacity.
    private readonly Dictionary<uint, LinkedListNode<CacheEntry>> _frames = [];
    private readonly LinkedList<CacheEntry> _lru = new();

    private sealed record CacheEntry(uint Frame, IReadOnlyDictionary<string, WorldStateSummary> StatesByKeys);

    public ReplayStreamingService(WorldStateArtifactReader reader, float cellSize, int cacheCapacity = 4)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _cellSize = cellSize > 0 ? cellSize : 100f;
        _cacheCapacity = cacheCapacity > 0 ? cacheCapacity : 1;
    }

    public int CachedFrameCount => _frames.Count;

    public IReadOnlyCollection<string> KnownRegions => _reader.KnownRegions;

    /// <summary>
    /// Region keys whose cells intersect the viewport expanded by the prefetch margin, in
    /// cell:x,y form. This is the viewport-to-topology visibility query for spatial domains.
    /// </summary>
    public IReadOnlyList<string> VisibleRegionKeys(WorldViewport viewport, int prefetchCells = 1)
    {
        var margin = MathF.Max(0, prefetchCells) * _cellSize;
        var expanded = viewport.Expanded(margin);
        var minCellX = (int)MathF.Floor(expanded.MinX / _cellSize);
        var maxCellX = (int)MathF.Floor(expanded.MaxX / _cellSize);
        var minCellY = (int)MathF.Floor(expanded.MinY / _cellSize);
        var maxCellY = (int)MathF.Floor(expanded.MaxY / _cellSize);
        var keys = new List<string>();
        for (var cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (var cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                keys.Add($"cell:{cellX},{cellY}");
            }
        }

        return keys;
    }

    /// <summary>
    /// Streams one frame for a spatial viewport: decodes visible + prefetch regions only,
    /// culls to the exact viewport rectangle, and projects survivors into draw primitives.
    /// Off-screen entities produce no primitives.
    /// </summary>
    public ReplayStreamingQueryResult Query(uint frame, WorldViewport viewport, int prefetchCells = 1)
    {
        return Query(frame, VisibleRegionKeys(viewport, prefetchCells), visible =>
            visible.Where(o => viewport.Contains(o.Position.X, o.Position.Y)).ToList());
    }

    /// <summary>
    /// Streams one frame through explicit region keys: the same visibility API for non-spatial
    /// domains such as transport graphs, where "visible" is the selected focus set rather than
    /// a rectangle. All entities of the selected regions are visible.
    /// </summary>
    public ReplayStreamingQueryResult Query(uint frame, IReadOnlyCollection<string> visibleRegionKeys)
    {
        return Query(frame, visibleRegionKeys, visible => visible);
    }

    private ReplayStreamingQueryResult Query(
        uint frame,
        IReadOnlyCollection<string> requestedKeys,
        Func<List<WorldObjectState>, List<WorldObjectState>> cull)
    {
        if (_reader.HasRegionIndex)
        {
            var decoded = ResolveRegionStates(frame, requestedKeys);
            var decodedEntities = decoded.Values.Sum(s => s.Objects.Count);
            var visibleObjects = new List<WorldObjectState>();
            var buckets = new Dictionary<string, List<WorldObjectState>>();
            foreach (var key in requestedKeys)
            {
                if (!decoded.TryGetValue(key, out var state))
                {
                    continue;
                }

                foreach (var obj in state.Objects)
                {
                    if (RecordedStateProjector.BucketKey(obj.Position.X, obj.Position.Y, _cellSize) == key)
                    {
                        visibleObjects.Add(obj);
                    }
                }
            }

            var visible = cull(visibleObjects);
            var primitives = Project(visible);
            return new ReplayStreamingQueryResult(
                frame,
                primitives,
                requestedKeys,
                requestedKeys,
                visible.Count,
                decodedEntities,
                CachedFrameCount);
        }

        // v1 artifacts carry no region index: fall back to a whole-frame decode, keep the
        // culling so primitive counts stay viewport-bounded even though the decode is not.
        var whole = _reader.ReconstructStateAt(frame);
        var wholeVisible = cull([.. whole.Objects]);
        return new ReplayStreamingQueryResult(
            frame,
            Project(wholeVisible),
            requestedKeys,
            [],
            wholeVisible.Count,
            whole.Objects.Count,
            CachedFrameCount);
    }

    private IReadOnlyDictionary<string, WorldStateSummary> ResolveRegionStates(uint frame, IReadOnlyCollection<string> keys)
    {
        if (_frames.TryGetValue(frame, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            if (node.Value.StatesByKeys.Keys.SequenceEqual(keys))
            {
                return node.Value.StatesByKeys;
            }
        }

        var states = new Dictionary<string, WorldStateSummary>();
        foreach (var key in keys)
        {
            states[key] = _reader.ReconstructRegionStateAt(frame, [key]);
        }

        var entry = new CacheEntry(frame, states);
        if (_frames.TryGetValue(frame, out var existing))
        {
            _lru.Remove(existing);
        }

        var fresh = _lru.AddFirst(entry);
        _frames[frame] = fresh;
        while (_lru.Count > _cacheCapacity)
        {
            var evicted = _lru.Last!;
            _lru.RemoveLast();
            _frames.Remove(evicted.Value.Frame);
        }

        return states;
    }

    private static IReadOnlyList<DomainVisualPrimitive> Project(List<WorldObjectState> objects)
    {
        if (objects.Count == 0)
        {
            return [];
        }

        var summary = new WorldStateSummary { Frame = 0 };
        summary.Objects.AddRange(objects.OrderBy(o => o.ObjectId));
        var projection = RecordedStateProjector.Project(summary, null);
        return projection.Primitives;
    }
}
