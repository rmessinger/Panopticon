#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Named waypoints of a map.</summary>
public sealed record WaypointSet(IReadOnlyList<Waypoint> Waypoints);
