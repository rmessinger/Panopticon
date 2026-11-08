#nullable enable

using System;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Explicit stored quantity with value, declared unit name, and frame name.</summary>
public sealed record MapPackageQuantity(double Value, string Unit, string Frame)
{
    public static MapPackageQuantity FromMapQuantity(MapQuantity quantity) =>
        new(quantity.Value, quantity.Unit.ToString(), quantity.Frame.ToString());

    public bool TryToMapQuantity(out MapQuantity quantity)
    {
        if (Enum.TryParse<MapQuantityUnit>(Unit, false, out var unit) &&
            Enum.TryParse<MapQuantityFrame>(Frame, false, out var frame))
        {
            quantity = new MapQuantity(Value, unit, frame);
            return true;
        }

        quantity = default;
        return false;
    }
}
