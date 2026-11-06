#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>A placed object. <c>LegacyProperties</c> preserves legacy per-object dictionary values as strings.</summary>
public sealed record PlacedObject(
    string StableId,
    MapQuantity X,
    MapQuantity Y,
    MapQuantity Z,
    double OrientationRadians,
    AssetReference Type,
    IReadOnlyDictionary<string, string> LegacyProperties);
