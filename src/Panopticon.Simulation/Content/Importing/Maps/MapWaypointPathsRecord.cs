#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed interchange home for WaypointsList waypoint path records.</summary>
public sealed record MapWaypointPathsRecord(
    ushort Version,
    IReadOnlyList<MapWaypointPathRecord> Paths);
