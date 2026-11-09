#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Root manifest document for an H3 map package directory container.</summary>
public sealed record MapPackageManifest
{
    [JsonConstructor]
    public MapPackageManifest(
        string? format = null,
        int packageVersion = 0,
        int schemaVersion = 0,
        MapPackageIdentity? identity = null,
        MapPackageTerrainLayout? layout = null,
        MapPackageBlendModel? blend = null,
        IReadOnlyList<MapPackagePlacedObject>? objects = null,
        IReadOnlyList<MapPackageWaypoint>? waypoints = null,
        MapPackageProvenance? provenance = null,
        IReadOnlyList<MapPackageLayerEntry>? layers = null,
        IReadOnlyList<MapPackageRetainedEntry>? retainedSections = null)
    {
        Format = format ?? string.Empty;
        PackageVersion = packageVersion;
        SchemaVersion = schemaVersion;
        Identity = identity!;
        Layout = layout!;
        Blend = blend!;
        Objects = objects?.ToArray();
        Waypoints = waypoints?.ToArray();
        Provenance = provenance!;
        Layers = layers?.ToArray() ?? [];
        RetainedSections = retainedSections?.ToArray() ?? [];
    }

    public string Format { get; init; } = MapPackageFormat.PackageFormatName;

    public int PackageVersion { get; init; } = MapPackageFormat.CurrentPackageVersion;

    public int SchemaVersion { get; init; } = MapInterchange.CurrentSchemaVersion;

    public MapPackageIdentity Identity { get; init; }

    public MapPackageTerrainLayout Layout { get; init; }

    public MapPackageBlendModel Blend { get; init; }

    public IReadOnlyList<MapPackagePlacedObject>? Objects { get; init; }

    public IReadOnlyList<MapPackageWaypoint>? Waypoints { get; init; }

    public MapPackageProvenance Provenance { get; init; }

    public IReadOnlyList<MapPackageLayerEntry> Layers { get; init; }

    public IReadOnlyList<MapPackageRetainedEntry> RetainedSections { get; init; }

    public string ToJson() => MapPackageManifestJson.Serialize(this);

    public static (MapPackageManifest? Manifest, IReadOnlyList<MapPackageDiagnostic> Diagnostics) FromJson(string json) =>
        MapPackageManifestJson.Parse(json);
}
