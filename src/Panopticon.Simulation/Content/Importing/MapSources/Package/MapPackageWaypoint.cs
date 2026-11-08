#nullable enable

using System;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>A named waypoint in the package manifest.</summary>
public sealed record MapPackageWaypoint(
    string StableId,
    string Name,
    MapPackageQuantity X,
    MapPackageQuantity Y,
    MapPackageQuantity Z)
{
    public static MapPackageWaypoint FromModel(Waypoint waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return new MapPackageWaypoint(
            waypoint.StableId,
            waypoint.Name,
            MapPackageQuantity.FromMapQuantity(waypoint.X),
            MapPackageQuantity.FromMapQuantity(waypoint.Y),
            MapPackageQuantity.FromMapQuantity(waypoint.Z));
    }

    public Waypoint ToModel() => new(
        StableId,
        Name,
        X.TryToMapQuantity(out var x) ? x : default,
        Y.TryToMapQuantity(out var y) ? y : default,
        Z.TryToMapQuantity(out var z) ? z : default);
}
