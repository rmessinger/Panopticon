#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Source-level provenance record with hashes and source coordinates.</summary>
public sealed record MapPackageSourceRecord
{
    [JsonConstructor]
    public MapPackageSourceRecord(
        string? kind = null,
        IReadOnlyList<string>? sha256Digests = null,
        MapPackageQuantity? originX = null,
        MapPackageQuantity? originY = null)
    {
        Kind = kind ?? string.Empty;
        Sha256Digests = sha256Digests?.ToArray() ?? [];
        OriginX = originX;
        OriginY = originY;
    }

    public string Kind { get; init; }

    public IReadOnlyList<string> Sha256Digests { get; init; }

    public MapPackageQuantity? OriginX { get; init; }

    public MapPackageQuantity? OriginY { get; init; }

    public static MapPackageSourceRecord FromModel(SourceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new MapPackageSourceRecord(
            record.Kind,
            record.Sha256Digests,
            record.OriginX.HasValue ? MapPackageQuantity.FromMapQuantity(record.OriginX.Value) : null,
            record.OriginY.HasValue ? MapPackageQuantity.FromMapQuantity(record.OriginY.Value) : null);
    }

    public SourceRecord ToModel() => new(
        Kind,
        Sha256Digests,
        OriginX?.TryToMapQuantity(out var x) == true ? x : null,
        OriginY?.TryToMapQuantity(out var y) == true ? y : null);
}
