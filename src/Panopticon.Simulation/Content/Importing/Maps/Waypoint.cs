#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>A named waypoint at engine XY and Z positions.</summary>
public sealed record Waypoint(string StableId, string Name, MapQuantity X, MapQuantity Y, MapQuantity Z);
