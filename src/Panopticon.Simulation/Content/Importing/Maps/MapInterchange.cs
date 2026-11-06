#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Engine-owned map interchange root. Terrain, objects, and waypoints are components; provenance and the import report sit beside them. Collections are copied at construction; exposed read-only views are snapshots.</summary>
public sealed record MapInterchange
{
    public const int CurrentSchemaVersion = 1;

    public MapInterchange(
        int schemaVersion,
        MapIdentity identity,
        TerrainLayout layout,
        BlendModel blend,
        PlacedObjectSet? objects,
        WaypointSet? waypoints,
        Provenance provenance,
        ImportReport report,
        IReadOnlyList<RetainedSection> retainedSections)
    {
        ArgumentNullException.ThrowIfNull(retainedSections);
        SchemaVersion = schemaVersion;
        Identity = identity;
        Layout = layout;
        Blend = blend;
        Objects = objects;
        Waypoints = waypoints;
        Provenance = provenance;
        Report = report;
        RetainedSections = retainedSections.ToArray();
    }

    public int SchemaVersion { get; init; }

    public MapIdentity Identity { get; init; }

    public TerrainLayout Layout { get; init; }

    public BlendModel Blend { get; init; }

    public PlacedObjectSet? Objects { get; init; }

    public WaypointSet? Waypoints { get; init; }

    public Provenance Provenance { get; init; }

    public ImportReport Report { get; init; }

    public IReadOnlyList<RetainedSection> RetainedSections { get; init; }
}
