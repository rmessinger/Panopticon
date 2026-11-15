#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Strict-JSON geometry: Point, LineString, Polygon, or MultiGeometry, discriminated by Type.</summary>
public sealed record MapPackageVectorGeometry(
    string Type,
    MapPackageVectorPosition? Position = null,
    IReadOnlyList<MapPackageVectorPosition>? Positions = null,
    IReadOnlyList<IReadOnlyList<MapPackageVectorPosition>>? Rings = null,
    IReadOnlyList<MapPackageVectorGeometry>? Geometries = null)
{
    public const string PointType = "Point";

    public const string LineStringType = "LineString";

    public const string PolygonType = "Polygon";

    public const string MultiGeometryType = "MultiGeometry";

    public static MapPackageVectorGeometry FromModel(VectorGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return geometry switch
        {
            VectorPointGeometry point => new(PointType, MapPackageVectorPosition.FromModel(point.Position)),
            VectorLineStringGeometry line => new(LineStringType, Positions: line.Positions.Select(MapPackageVectorPosition.FromModel).ToArray()),
            VectorPolygonGeometry polygon => new(PolygonType, Rings: PolygonRings(polygon)),
            VectorMultiGeometry multi => new(MultiGeometryType, Geometries: multi.Geometries.Select(FromModel).ToArray()),
            _ => throw new FormatException($"Unsupported vector geometry '{geometry.GetType().Name}'."),
        };
    }

    public VectorGeometry ToModel() => Type switch
    {
        PointType => new VectorPointGeometry(RequireValue(Position, PointType, nameof(Position)).ToModel()),
        LineStringType => new VectorLineStringGeometry(RequireValue(Positions, LineStringType, nameof(Positions)).Select(p => p.ToModel()).ToArray()),
        PolygonType => PolygonFromRings(RequireValue(Rings, PolygonType, nameof(Rings))),
        MultiGeometryType => new VectorMultiGeometry(RequireValue(Geometries, MultiGeometryType, nameof(Geometries)).Select(g => g.ToModel()).ToArray()),
        _ => throw new FormatException($"Unknown vector geometry type '{Type}'."),
    };

    private static IReadOnlyList<IReadOnlyList<MapPackageVectorPosition>> PolygonRings(VectorPolygonGeometry polygon)
    {
        var rings = new List<IReadOnlyList<MapPackageVectorPosition>>(1 + polygon.InnerRings.Count)
        {
            polygon.OuterRing.Positions.Select(MapPackageVectorPosition.FromModel).ToArray(),
        };

        foreach (var inner in polygon.InnerRings)
        {
            rings.Add(inner.Positions.Select(MapPackageVectorPosition.FromModel).ToArray());
        }

        return rings;
    }

    private static VectorPolygonGeometry PolygonFromRings(IReadOnlyList<IReadOnlyList<MapPackageVectorPosition>> rings)
    {
        if (rings.Count == 0)
        {
            throw new FormatException("Polygon geometry has no rings.");
        }

        var outer = new VectorRing(rings[0].Select(p => p.ToModel()).ToArray());
        var inner = rings.Skip(1).Select(r => new VectorRing(r.Select(p => p.ToModel()).ToArray())).ToArray();
        return new VectorPolygonGeometry(outer, inner);
    }

    private static T RequireValue<T>(T? value, string type, string part) where T : class =>
        value ?? throw new FormatException($"{type} geometry is missing '{part}'.");
}
