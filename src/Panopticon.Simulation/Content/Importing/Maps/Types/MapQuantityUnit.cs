#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Declared unit of a <see cref="MapQuantity"/>.</summary>
public enum MapQuantityUnit
{
    /// <summary>Engine game units. Assumes no physical unit; it is not feet or meters by definition.</summary>
    GameUnit,

    Meters,

    Degrees,

    Scalar,
}
