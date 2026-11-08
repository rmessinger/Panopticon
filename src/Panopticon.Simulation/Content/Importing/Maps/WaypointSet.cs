#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Named waypoints of a map. Waypoints are copied at construction.</summary>
public sealed record WaypointSet
{
    public WaypointSet(IReadOnlyList<Waypoint> waypoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        Waypoints = waypoints.ToArray();
    }

    public IReadOnlyList<Waypoint> Waypoints { get; }
}
