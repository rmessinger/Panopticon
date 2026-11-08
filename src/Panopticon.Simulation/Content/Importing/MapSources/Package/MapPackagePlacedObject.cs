#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>A placed object in the package manifest.</summary>
public sealed record MapPackagePlacedObject
{
    [JsonConstructor]
    public MapPackagePlacedObject(
        string? stableId = null,
        MapPackageQuantity? x = null,
        MapPackageQuantity? y = null,
        MapPackageQuantity? z = null,
        double orientationRadians = 0,
        MapPackageAssetReference type = default,
        uint roadType = 0,
        IReadOnlyDictionary<string, MapPackagePropertyValue>? legacyProperties = null)
    {
        StableId = stableId ?? string.Empty;
        X = x!;
        Y = y!;
        Z = z!;
        OrientationRadians = orientationRadians;
        Type = type;
        RoadType = roadType;
        var dict = new SortedDictionary<string, MapPackagePropertyValue>(StringComparer.Ordinal);

        if (legacyProperties is not null)
        {
            foreach (var (k, v) in legacyProperties)
            {
                dict[k] = v;
            }
        }

        LegacyProperties = dict;
    }

    public string StableId { get; init; }

    public MapPackageQuantity X { get; init; }

    public MapPackageQuantity Y { get; init; }

    public MapPackageQuantity Z { get; init; }

    public double OrientationRadians { get; init; }

    public MapPackageAssetReference Type { get; init; }

    public uint RoadType { get; init; }

    public IReadOnlyDictionary<string, MapPackagePropertyValue> LegacyProperties { get; init; }

    public static MapPackagePlacedObject FromModel(PlacedObject placedObject)
    {
        ArgumentNullException.ThrowIfNull(placedObject);
        var props = new Dictionary<string, MapPackagePropertyValue>(placedObject.LegacyProperties.Count, StringComparer.Ordinal);

        foreach (var (key, value) in placedObject.LegacyProperties)
        {
            props[key] = MapPackagePropertyValue.FromModel(value);
        }

        return new MapPackagePlacedObject(
            placedObject.StableId,
            MapPackageQuantity.FromMapQuantity(placedObject.X),
            MapPackageQuantity.FromMapQuantity(placedObject.Y),
            MapPackageQuantity.FromMapQuantity(placedObject.Z),
            placedObject.OrientationRadians,
            MapPackageAssetReference.FromModel(placedObject.Type),
            placedObject.RoadType,
            props);
    }

    public PlacedObject ToModel()
    {
        var props = new Dictionary<string, MapPropertyValue>(LegacyProperties.Count, StringComparer.Ordinal);

        foreach (var (key, value) in LegacyProperties)
        {
            props[key] = value.ToModel();
        }

        return new PlacedObject(
            StableId,
            X.TryToMapQuantity(out var x) ? x : default,
            Y.TryToMapQuantity(out var y) ? y : default,
            Z.TryToMapQuantity(out var z) ? z : default,
            OrientationRadians,
            Type.ToModel(),
            RoadType,
            props);
    }
}
