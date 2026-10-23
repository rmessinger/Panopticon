using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Logic;

// Separate from the end-of-tick collision partitions so live movement queries do not
// change collision-pair ownership or delivery order.
internal sealed class GroundFootprintIndex(float cellSize)
{
    private readonly Dictionary<(int X, int Y), HashSet<PartitionObject>> _cells = new();
    private readonly Dictionary<PartitionObject, CellBounds> _bounds = new();
    private readonly HashSet<PartitionObject> _seen = new();

    internal void Add(PartitionObject obj)
    {
        var bounds = GetObjectBounds(obj);
        _bounds.Add(obj, bounds);
        AddToCells(obj, bounds);
    }

    internal void Update(PartitionObject obj)
    {
        if (!_bounds.TryGetValue(obj, out var previous))
        {
            return;
        }

        var bounds = GetObjectBounds(obj);
        if (bounds == previous)
        {
            return;
        }

        RemoveFromCells(obj, previous);
        AddToCells(obj, bounds);
        _bounds[obj] = bounds;
    }

    internal void Remove(PartitionObject obj)
    {
        if (_bounds.Remove(obj, out var bounds))
        {
            RemoveFromCells(obj, bounds);
        }
    }

    internal void Collect(Vector2 start, Vector2 end, float radius, List<PartitionObject> results)
    {
        results.Clear();
        _seen.Clear();
        var bounds = GetBounds(start, end, radius);
        for (var y = bounds.MinY; y <= bounds.MaxY; y++)
        {
            for (var x = bounds.MinX; x <= bounds.MaxX; x++)
            {
                if (!_cells.TryGetValue((x, y), out var objects))
                {
                    continue;
                }

                foreach (var obj in objects)
                {
                    if (_seen.Add(obj))
                    {
                        results.Add(obj);
                    }
                }
            }
        }
    }

    private CellBounds GetObjectBounds(PartitionObject obj)
    {
        var position = obj.GameObject.Translation.Vector2XY();
        return GetBounds(position, position, obj.GameObject.Geometry.BoundingCircleRadius);
    }

    private CellBounds GetBounds(Vector2 start, Vector2 end, float radius)
    {
        var min = Vector2.Min(start, end) - new Vector2(radius);
        var max = Vector2.Max(start, end) + new Vector2(radius);
        // Unclipped world cells retain coverage at map edges and for off-map spawns.
        return new CellBounds((int)MathF.Floor(min.X / cellSize), (int)MathF.Floor(min.Y / cellSize),
            (int)MathF.Floor(max.X / cellSize), (int)MathF.Floor(max.Y / cellSize));
    }

    private void AddToCells(PartitionObject obj, CellBounds bounds)
    {
        for (var y = bounds.MinY; y <= bounds.MaxY; y++)
        {
            for (var x = bounds.MinX; x <= bounds.MaxX; x++)
            {
                if (!_cells.TryGetValue((x, y), out var objects))
                {
                    objects = new HashSet<PartitionObject>();
                    _cells.Add((x, y), objects);
                }

                objects.Add(obj);
            }
        }
    }

    private void RemoveFromCells(PartitionObject obj, CellBounds bounds)
    {
        for (var y = bounds.MinY; y <= bounds.MaxY; y++)
        {
            for (var x = bounds.MinX; x <= bounds.MaxX; x++)
            {
                var objects = _cells[(x, y)];
                objects.Remove(obj);
                if (objects.Count == 0)
                {
                    _cells.Remove((x, y));
                }
            }
        }
    }

    private readonly record struct CellBounds(int MinX, int MinY, int MaxX, int MaxY);
}
