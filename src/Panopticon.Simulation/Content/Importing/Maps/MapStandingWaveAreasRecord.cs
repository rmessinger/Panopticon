#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed interchange home for StandingWaveAreas.</summary>
public sealed record MapStandingWaveAreasRecord(
    ushort Version,
    IReadOnlyList<MapStandingWaveAreaRecord> Areas);
