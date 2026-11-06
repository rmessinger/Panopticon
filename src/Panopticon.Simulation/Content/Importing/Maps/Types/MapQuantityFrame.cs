#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Frame in which a <see cref="MapQuantity"/> is expressed.</summary>
public enum MapQuantityFrame
{
    /// <summary>Map local XY, southwest origin, +X east, +Y north.</summary>
    MapLocalXy,

    /// <summary>Map local Z, rebased to the declared vertical origin.</summary>
    MapLocalZ,

    /// <summary>Source CRS horizontal frame.</summary>
    SourceCrs,

    /// <summary>Source vertical frame with its declared datum.</summary>
    SourceVertical,

    /// <summary>Legacy engine XY axes.</summary>
    LegacyEngineXy,

    /// <summary>Legacy engine Z axis.</summary>
    LegacyEngineZ,
}
