using System.Collections.Generic;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-N11-01: memoizes cross-section widths per (cell, direction octant) for the
/// passage finder. A cached value of null means the ray cast ran open (hit the width cap);
/// the cache clears whenever the passability version changes.
/// </summary>
internal sealed class CrossSectionCache
{
    private readonly Dictionary<int, int?> _widths = new();
    private int _version;

    public int Version => _version;

    internal CrossSectionCache(int passabilityVersion)
    {
        _version = passabilityVersion;
    }

    public bool TryGetWidth(int x, int y, int octant, out int? width)
    {
        var key = ComputeKey(x, y, octant);
        if (_widths.TryGetValue(key, out var cached))
        {
            PassageFinder.CacheHits++;
            width = cached;
            return true;
        }

        width = null;
        return false;
    }

    public void Store(int x, int y, int octant, int? width)
    {
        PassageFinder.Casts++;
        _widths[ComputeKey(x, y, octant)] = width;
    }

    /// <summary>Clears the memoized widths when the passability version changed.</summary>
    public void Reset(int passabilityVersion)
    {
        if (_version == passabilityVersion)
        {
            return;
        }

        _widths.Clear();
        _version = passabilityVersion;
    }

    private static int ComputeKey(int x, int y, int octant)
    {
        return (octant << 16) | (y << 8) | x;
    }
}
