#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Source and generation provenance in the package manifest.</summary>
public sealed record MapPackageProvenance
{
    [JsonConstructor]
    public MapPackageProvenance(
        string? sourceSchemaVersion = null,
        IReadOnlyList<MapPackageSourceRecord>? sources = null,
        IReadOnlyList<MapPackageGeneratedFeature>? generatedFeatures = null)
    {
        SourceSchemaVersion = sourceSchemaVersion;
        Sources = sources?.ToArray() ?? [];
        GeneratedFeatures = generatedFeatures?.ToArray() ?? [];
    }

    public string? SourceSchemaVersion { get; init; }

    public IReadOnlyList<MapPackageSourceRecord> Sources { get; init; }

    public IReadOnlyList<MapPackageGeneratedFeature> GeneratedFeatures { get; init; }

    public static MapPackageProvenance FromModel(Provenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        return new MapPackageProvenance(
            provenance.SourceSchemaVersion,
            provenance.Sources.Select(MapPackageSourceRecord.FromModel).ToArray(),
            provenance.GeneratedFeatures.Select(MapPackageGeneratedFeature.FromModel).ToArray());
    }

    public Provenance ToModel() => new(
        SourceSchemaVersion,
        Sources.Select(s => s.ToModel()).ToArray(),
        GeneratedFeatures.Select(f => f.ToModel()).ToArray());
}
