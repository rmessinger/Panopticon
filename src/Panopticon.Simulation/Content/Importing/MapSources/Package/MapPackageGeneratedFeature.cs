#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Generated feature provenance record with stable ID and attributes.</summary>
public sealed record MapPackageGeneratedFeature
{
    [JsonConstructor]
    public MapPackageGeneratedFeature(
        string? stableId = null,
        string? kind = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        StableId = stableId ?? string.Empty;
        Kind = kind ?? string.Empty;
        var dict = new SortedDictionary<string, string>(StringComparer.Ordinal);

        if (attributes is not null)
        {
            foreach (var (k, v) in attributes)
            {
                dict[k] = v;
            }
        }

        Attributes = dict;
    }

    public string StableId { get; init; }

    public string Kind { get; init; }

    public IReadOnlyDictionary<string, string> Attributes { get; init; }

    public static MapPackageGeneratedFeature FromModel(GeneratedFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return new MapPackageGeneratedFeature(feature.StableId, feature.Kind, feature.Attributes);
    }

    public GeneratedFeature ToModel() => new(StableId, Kind, Attributes);
}
