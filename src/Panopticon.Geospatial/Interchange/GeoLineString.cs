using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Panopticon.Geospatial.Interchange;

/// <summary>Line string geometry. The constructor copies the positions.</summary>
public sealed record GeoLineString : GeoGeometry
{
    public GeoLineString(IEnumerable<GeoPosition> positions)
    {
        var copy = InterchangeCopy.CopyItems(positions, nameof(positions));

        if (copy.Length < 2)
        {
            throw new ArgumentException("A line string requires at least two positions.", nameof(positions));
        }

        Positions = new ReadOnlyCollection<GeoPosition>(copy);
    }

    public IReadOnlyList<GeoPosition> Positions { get; }
}
