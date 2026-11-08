#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Explicit stored quantity with value, declared unit name, and frame name.</summary>
public sealed record MapPackageQuantity(double Value, string Unit, string Frame)
{
    private static readonly HashSet<string> UnitNames = new(Enum.GetNames<MapQuantityUnit>(), StringComparer.Ordinal);

    private static readonly HashSet<string> FrameNames = new(Enum.GetNames<MapQuantityFrame>(), StringComparer.Ordinal);

    public static MapPackageQuantity FromMapQuantity(MapQuantity quantity) =>
        new(quantity.Value, quantity.Unit.ToString(), quantity.Frame.ToString());

    /// <summary>
    /// Accepts exact declared enum member names only, case-sensitive: numeric strings parse as
    /// enum values but are not names, so they reject here and read back as the typed diagnostic.
    /// </summary>
    public bool TryToMapQuantity(out MapQuantity quantity)
    {
        if (Unit is not null && Frame is not null &&
            UnitNames.Contains(Unit) && FrameNames.Contains(Frame))
        {
            quantity = new MapQuantity(Value, Enum.Parse<MapQuantityUnit>(Unit), Enum.Parse<MapQuantityFrame>(Frame));
            return true;
        }

        quantity = default;
        return false;
    }
}
