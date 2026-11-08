#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed interchange home for GlobalWaterSettings.</summary>
public sealed record MapGlobalWaterSettingsRecord(
    ushort Version,
    bool ReflectionOn,
    float ReflectionPlaneZ);
