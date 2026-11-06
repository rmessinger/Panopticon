#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Source-level digests and origin, with coordinates in the source CRS or vertical frame.</summary>
public sealed record SourceRecord(
    string Kind,
    IReadOnlyList<string> Sha256Digests,
    MapQuantity? OriginX,
    MapQuantity? OriginY);
