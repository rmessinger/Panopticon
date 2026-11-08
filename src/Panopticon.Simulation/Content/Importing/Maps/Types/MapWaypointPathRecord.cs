#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>One waypoint path edge connecting two waypoints by ID.</summary>
public sealed record MapWaypointPathRecord(int StartWaypointId, int EndWaypointId);
