using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Panopticon.Geospatial.Interchange;

/// <summary>Polygon geometry: an outer ring plus optional holes. The constructor copies the hole list.</summary>
public sealed record GeoPolygon : GeoGeometry
{
    public GeoPolygon(GeoRing outerRing, IEnumerable<GeoRing>? innerRings = null)
    {
        ArgumentNullException.ThrowIfNull(outerRing);

        OuterRing = outerRing;
        InnerRings = new ReadOnlyCollection<GeoRing>(
            InterchangeCopy.CopyItems(innerRings ?? [], nameof(innerRings)));
    }

    public GeoRing OuterRing { get; }

    public IReadOnlyList<GeoRing> InnerRings { get; }
}
