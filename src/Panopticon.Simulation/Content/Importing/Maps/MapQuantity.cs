#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>A source value preserved as read, with its declared unit and frame. Conversion happens only in adapters.</summary>
public readonly record struct MapQuantity(double Value, MapQuantityUnit Unit, MapQuantityFrame Frame);
