using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Panopticon.Mathematics;

namespace Panopticon.Graphics.Rendering;

public sealed class RenderItemCollection
{
    // TODO: Should this just be 0? Or somewhere in the hundreds?
    // A map usually has thousands of objects.
    private const int PreAllocatedItems = 128;
    private const double GrowthFactor = 1.5;

    // The backing storage for render items.
    private RenderItem[] _items;

    // TODO: SphereBounds check?
    internal ref RenderItem this[int i] => ref _items[i];

    // An array of flags indicating if a render item should be included in the culling set.
    // Must have the same capacity and length as _items.
    private bool[] _culled;

    // Number of render items in the collection.
    public int Length { get; private set; }

    // All item indices ordered by render key. Computed once per frame (B5-3); per-pass
    // culling filters this order instead of re-sorting, because keys do not depend on the
    // frustum and passes only cull.
    private readonly List<int> _sortedIndices = [];

    // Sorted indices of objects that were chosen to be rendered in the last pass.
    // Indices refer to _items.
    private readonly List<int> _culledItemIndices;
    public IReadOnlyList<int> CulledItemIndices => _culledItemIndices;

    public RenderItemCollection()
    {
        _culledItemIndices = new List<int>();
        _items = new RenderItem[PreAllocatedItems];
        _culled = new bool[PreAllocatedItems];
        Length = 0;
    }

    internal void Add(RenderItem item)
    {
        if (Length == _items.Length)
        {
            var newCapacity = (int)((Length + 1) * GrowthFactor);
            Array.Resize(ref _items, newCapacity);
            Array.Resize(ref _culled, newCapacity);
        }

        _items[Length++] = item;
    }

    /// <summary>
    /// Sorts every item by render key, once per frame after the list is built (B5-3).
    /// Ties break by insertion index for a stable, flicker-free order.
    /// </summary>
    public void SortAll()
    {
        if (Length == 0)
        {
            return;
        }

        ClearCullingResults();
        _sortedIndices.Clear();
        for (var i = 0; i < Length; i++)
        {
            _sortedIndices.Add(i);
        }

        _sortedIndices.Sort((a, b) =>
        {
            var result = _items[a].Key.CompareTo(_items[b].Key);
            if (result == 0)
            {
                return a.CompareTo(b);
            }

            return result;
        });
    }

    /// <summary>
    /// Per-pass culling: filters the pre-sorted order (see <see cref="SortAll"/>) against a
    /// frustum and optional clipping planes, preserving sorted order in the visible list.
    /// </summary>
    public void FilterVisible(in BoundingFrustum cameraFrustum, in Plane? clippingPlane1, in Plane? clippingPlane2, int batchSize)
    {
        if (Length == 0 || _sortedIndices.Count == 0)
        {
            return;
        }

        ClearCullingResults();

        // Step 1: Compute visibility for each sorted item.

        if (batchSize == -1)
        {
            // Perform culling in the main thread.
            for (var s = 0; s < _sortedIndices.Count; s++)
            {
                Cull(_sortedIndices[s], cameraFrustum);
                if (clippingPlane1 != null)
                {
                    Cull(_sortedIndices[s], clippingPlane1.Value);
                }

                if (clippingPlane2 != null)
                {
                    Cull(_sortedIndices[s], clippingPlane2.Value);
                }
            }
        }
        else
        {
            // We need a copy of cameraFrustum, as we can't send in parameters to closures.
            var frustum = cameraFrustum;
            // We need a copy of clippingPlane, as we can't send in parameters to closures.
            var clip1 = clippingPlane1;
            var clip2 = clippingPlane2;

            // Perform culling using the thread pool, in batches of batchSize.
            Parallel.ForEach(Partitioner.Create(0, _sortedIndices.Count, batchSize), range =>
            {
                var (start, end) = range;
                for (var s = start; s < end; s++)
                {
                    var i = _sortedIndices[s];
                    Cull(i, frustum);
                    if (clip1 != null)
                    {
                        Cull(i, clip1.Value);
                    }

                    if (clip2 != null)
                    {
                        Cull(i, clip2.Value);
                    }
                }
            });
        }

        // Step 2: Go through _culled in sorted order, and store the indices of culled values
        // in _culledItemIndices, preserving the sorted order.
        for (var s = 0; s < _sortedIndices.Count; s++)
        {
            var i = _sortedIndices[s];
            if (_culled[i])
            {
                _culledItemIndices.Add(i);
            }
        }
    }

    // Performs frustum culling for a single render item in _items.
    // Increments the provided integer reference if the object was within the frustum.
    private void Cull(int i, in BoundingFrustum cameraFrustum)
    {
        _culled[i] = cameraFrustum.Intersects(_items[i].BoundingBox);
    }

    private void Cull(int i, in Plane clippingPlane)
    {
        _culled[i] = _culled[i] && _items[i].BoundingBox.Intersects(clippingPlane) != PlaneIntersectionType.Back;
    }

    private void ClearCullingResults()
    {
        _culledItemIndices.Clear();
    }

    public void Clear()
    {
        Length = 0;
        ClearCullingResults();
        _sortedIndices.Clear();

        // TODO: Should we provide a different method for actually clearing the item buffer?
        // Otherwise there might be memory leaks when switching between scenes.
    }
}
