#nullable enable

using System;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>One terrain border rectangle in lattice coordinates.</summary>
public sealed record MapPackageTerrainBorder(uint Corner1X, uint Corner1Y, uint X, uint Y)
{
    public static MapPackageTerrainBorder FromModel(MapTerrainBorder border)
    {
        ArgumentNullException.ThrowIfNull(border);
        return new MapPackageTerrainBorder(border.Corner1X, border.Corner1Y, border.X, border.Y);
    }

    public MapTerrainBorder ToModel() => new(Corner1X, Corner1Y, X, Y);
}
