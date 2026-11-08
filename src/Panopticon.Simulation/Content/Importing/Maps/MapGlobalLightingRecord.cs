#nullable enable

using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed interchange home for GlobalLighting.</summary>
public sealed record MapGlobalLightingRecord(
    ushort Version,
    MapTimeOfDay Time,
    IReadOnlyDictionary<MapTimeOfDay, MapLightingConfigurationRecord> Configurations,
    uint ShadowColorArgb,
    byte[]? Unknown = null,
    Vector3? Unknown2 = null,
    uint? Unknown3Argb = null,
    Vector3? NoCloudFactor = null);
