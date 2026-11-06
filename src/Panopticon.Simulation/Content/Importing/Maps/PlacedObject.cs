#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>A placed object. <c>LegacyProperties</c> preserves legacy per-object dictionary values as typed values; <c>RoadType</c> carries the raw legacy road bit flags.</summary>
public sealed record PlacedObject
{
    public PlacedObject(
        string stableId,
        MapQuantity x,
        MapQuantity y,
        MapQuantity z,
        double orientationRadians,
        AssetReference type,
        uint roadType,
        IReadOnlyDictionary<string, MapPropertyValue> legacyProperties)
    {
        ArgumentNullException.ThrowIfNull(stableId);
        ArgumentNullException.ThrowIfNull(legacyProperties);
        StableId = stableId;
        X = x;
        Y = y;
        Z = z;
        OrientationRadians = orientationRadians;
        Type = type;
        RoadType = roadType;
        LegacyProperties = new Dictionary<string, MapPropertyValue>(legacyProperties, StringComparer.Ordinal);
    }

    public string StableId { get; }

    public MapQuantity X { get; }

    public MapQuantity Y { get; }

    public MapQuantity Z { get; }

    public double OrientationRadians { get; }

    public AssetReference Type { get; }

    /// <summary>Raw legacy road bit flags; unknown bits are preserved unmasked.</summary>
    public uint RoadType { get; }

    /// <summary>Defensive copy with ordinal key comparison; the source dictionary is not retained.</summary>
    public IReadOnlyDictionary<string, MapPropertyValue> LegacyProperties { get; }
}
