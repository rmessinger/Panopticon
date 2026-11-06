#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Source-level digests and origin, with coordinates in the source CRS or vertical frame. Digests are copied at construction.</summary>
public sealed record SourceRecord
{
    public SourceRecord(string kind, IReadOnlyList<string> sha256Digests, MapQuantity? originX, MapQuantity? originY)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(sha256Digests);
        Kind = kind;
        Sha256Digests = sha256Digests.ToArray();
        OriginX = originX;
        OriginY = originY;
    }

    public string Kind { get; }

    public IReadOnlyList<string> Sha256Digests { get; }

    public MapQuantity? OriginX { get; }

    public MapQuantity? OriginY { get; }
}
