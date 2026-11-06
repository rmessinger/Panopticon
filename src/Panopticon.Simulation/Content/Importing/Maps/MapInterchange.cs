#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Engine-owned map interchange root. Terrain, objects, and waypoints are components; provenance and the import report sit beside them.</summary>
/// <param name="SchemaVersion">Interchange schema version, independent of source payload and legacy section versions.</param>
public sealed record MapInterchange(
    int SchemaVersion,
    MapIdentity Identity,
    TerrainLayout Layout,
    BlendModel Blend,
    PlacedObjectSet? Objects,
    WaypointSet? Waypoints,
    Provenance Provenance,
    ImportReport Report,
    IReadOnlyList<RetainedSection> RetainedSections)
{
    public const int CurrentSchemaVersion = 1;
}
