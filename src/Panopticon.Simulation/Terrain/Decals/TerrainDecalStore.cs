using System;
using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Terrain.Decals;

public sealed class TerrainDecalStore
{
    private readonly List<TerrainDecal> _decals = new();
    private int _limit;

    public IReadOnlyList<TerrainDecal> Decals { get; }
    public int Count => _decals.Count;
    public int Version { get; private set; }

    public int Limit
    {
        get => _limit;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _limit = value;
            if (_decals.Count > value)
            {
                _decals.RemoveRange(0, _decals.Count - value);
                Version++;
            }
        }
    }

    public TerrainDecalStore(int limit = 500)
    {
        Decals = _decals.AsReadOnly();
        Limit = limit;
    }

    public bool Add(TerrainDecalTemplate template, Vector3 position, float radius, int cell)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (Limit == 0 || !float.IsFinite(radius) || radius <= 0
            || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
        {
            return false;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        // The original evicts before duplicate detection, including when the new mark is rejected.
        if (_decals.Count == Limit)
        {
            _decals.RemoveAt(0);
            Version++;
        }

        var tolerance = radius / 4f;
        foreach (var decal in _decals)
        {
            if (decal.Template == template && decal.Cell == cell
                && MathF.Abs(decal.Position.X - position.X) < tolerance
                && MathF.Abs(decal.Position.Y - position.Y) < tolerance
                && MathF.Abs(decal.Radius - radius) < tolerance)
            {
                return false;
            }
        }

        _decals.Add(new TerrainDecal(template, position, radius, cell));
        Version++;
        return true;
    }

    public void Clear()
    {
        if (_decals.Count == 0)
        {
            return;
        }

        _decals.Clear();
        Version++;
    }
}
