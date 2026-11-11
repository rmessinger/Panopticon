using System;

namespace Panopticon.Terrain.Cdlod;

[Flags]
public enum CdlodEdges
{
    None = 0,

    /// <summary>Edge toward increasing lattice Y.</summary>
    North = 1 << 0,

    /// <summary>Edge toward increasing lattice X.</summary>
    East = 1 << 1,

    /// <summary>Edge toward decreasing lattice Y.</summary>
    South = 1 << 2,

    /// <summary>Edge toward decreasing lattice X.</summary>
    West = 1 << 3,
}
