#nullable enable

using System;
using Panopticon.Data.Map;

namespace Panopticon.Terrain;

/// <summary>
/// Typed handoff from the package converter to the runtime bridge path plan: the
/// converter writes <see cref="Key"/> = <see cref="Version"/> onto both placed objects
/// of a generated bridge whose landings it left at their original heights because the
/// supported template's approach corridor is planned at runtime. Packages without the
/// property predate the handoff and keep the converter's landing band grading.
/// </summary>
public static class GeneratedBridgePathHandoff
{
    public const string Key = "generatedBridgePathPlan";

    public const int Version = 1;

    public static bool IsMarked(MapObject mapObject)
    {
        ArgumentNullException.ThrowIfNull(mapObject);

        return mapObject.Properties.GetPropOrNull(Key)?.GetInteger() == Version;
    }

    /// <summary>
    /// True when a property exists under <see cref="Key"/> whatever its value or type: a
    /// value this runtime does not implement still means the landings were not pregraded,
    /// so such a bridge takes the legacy fallback with the band rule instead of the
    /// authored path alone.
    /// </summary>
    public static bool IsPresent(MapObject mapObject)
    {
        ArgumentNullException.ThrowIfNull(mapObject);

        return mapObject.Properties.GetPropOrNull(Key) is not null;
    }
}
