using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Panopticon.Geospatial.Interchange;

/// <summary>Collection geometry that may nest further <see cref="GeoMultiGeometry"/> values. The constructor copies the list.</summary>
public sealed record GeoMultiGeometry : GeoGeometry
{
    public GeoMultiGeometry(IEnumerable<GeoGeometry> geometries)
    {
        Geometries = new ReadOnlyCollection<GeoGeometry>(
            InterchangeCopy.CopyItems(geometries, nameof(geometries)));
    }

    public IReadOnlyList<GeoGeometry> Geometries { get; }
}
