#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed interchange home for StandingWaterAreas.</summary>
public sealed record MapStandingWaterAreasRecord(
    ushort Version,
    IReadOnlyList<MapStandingWaterAreaRecord> Areas);
