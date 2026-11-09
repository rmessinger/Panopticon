#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed interchange home for RiverAreas.</summary>
public sealed record MapRiverAreasRecord(
    ushort Version,
    IReadOnlyList<MapRiverAreaRecord> Areas);
