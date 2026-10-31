using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Panopticon.Geospatial.Interchange;

/// <summary>Ring of positions forming a polygon boundary. The constructor copies the positions.</summary>
public sealed record GeoRing
{
    public GeoRing(IEnumerable<GeoPosition> positions)
    {
        var copy = InterchangeCopy.CopyItems(positions, nameof(positions));

        if (copy.Length == 0)
        {
            throw new ArgumentException("A ring requires at least one position.", nameof(positions));
        }

        Positions = new ReadOnlyCollection<GeoPosition>(copy);
    }

    public IReadOnlyList<GeoPosition> Positions { get; }
}
