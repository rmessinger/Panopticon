using System;
using System.Collections.Generic;
using Panopticon.Geospatial.Interchange;
using Xunit;

namespace Panopticon.Geospatial.Tests.Interchange;

public class GeometryAndFeatureTests
{
    private static GeoPosition Position(double x, double y)
    {
        return new GeoPosition(new Vector2d(x, y));
    }

    [Fact]
    public void GeoPointSwapsAxesForGeographicVector2d()
    {
        var point = new GeoPoint(48.5, 2.25);

        Assert.Equal(new Vector2d(2.25, 48.5), point.ToVector2d());
        Assert.Equal(point, GeoPoint.FromVector2d(new Vector2d(2.25, 48.5)));
    }

    [Fact]
    public void GeoPositionDefaultsAltitudeToNull()
    {
        var position = new GeoPosition(new Vector2d(2.25, 48.5));

        Assert.Null(position.AltitudeMeters);
        Assert.Equal(12.5, new GeoPosition(new Vector2d(2.25, 48.5), 12.5).AltitudeMeters);
    }

    [Fact]
    public void GeoRingCopiesPositions()
    {
        var positions = new[] { Position(0, 0), Position(1, 0), Position(1, 1) };
        var ring = new GeoRing(positions);

        positions[0] = Position(9, 9);

        Assert.Equal(Position(0, 0), ring.Positions[0]);
        Assert.Equal(3, ring.Positions.Count);
    }

    [Fact]
    public void GeoRingRejectsEmptyPositions()
    {
        Assert.Throws<ArgumentException>(() => new GeoRing(Array.Empty<GeoPosition>()));
    }

    [Fact]
    public void GeoLineStringRequiresTwoPositionsAndCopies()
    {
        var positions = new List<GeoPosition> { Position(0, 0), Position(1, 1) };

        Assert.Throws<ArgumentException>(() => new GeoLineString(new[] { Position(0, 0) }));

        var line = new GeoLineString(positions);
        positions.Clear();

        Assert.Equal(2, line.Positions.Count);
        Assert.Equal(Position(0, 0), line.Positions[0]);
    }

    [Fact]
    public void GeoPolygonStoresOuterRingAndCopiesHoles()
    {
        var holes = new List<GeoRing> { new GeoRing([Position(2, 2), Position(3, 2), Position(3, 3)]) };
        var polygon = new GeoPolygon(new GeoRing([Position(0, 0), Position(9, 0), Position(9, 9)]), holes);

        holes.Clear();

        Assert.Equal(3, polygon.OuterRing.Positions.Count);
        Assert.Single(polygon.InnerRings);
        Assert.Equal(3, polygon.InnerRings[0].Positions.Count);
    }

    [Fact]
    public void GeoPolygonDefaultsToNoHolesAndRejectsInvalidInput()
    {
        var outer = new GeoRing([Position(0, 0), Position(1, 0)]);

        Assert.Empty(new GeoPolygon(outer).InnerRings);
        Assert.Throws<ArgumentNullException>(() => new GeoPolygon(null!));
        Assert.Throws<ArgumentException>(() => new GeoPolygon(outer, [null!]));
    }

    [Fact]
    public void GeoMultiGeometryCopiesListAndSupportsNesting()
    {
        var geometries = new List<GeoGeometry>
        {
            new GeoPointGeometry(Position(1, 2)),
            new GeoMultiGeometry([new GeoLineString([Position(0, 0), Position(1, 1)])]),
        };

        var multi = new GeoMultiGeometry(geometries);
        geometries.Clear();

        Assert.Equal(2, multi.Geometries.Count);
        var nested = Assert.IsType<GeoMultiGeometry>(multi.Geometries[1]);
        Assert.Single(nested.Geometries);
        Assert.Throws<ArgumentException>(() => new GeoMultiGeometry([null!]));
    }

    [Fact]
    public void GeospatialFeatureCopiesAttributes()
    {
        var attributes = new Dictionary<string, string> { ["name"] = "Verdun", ["styleUrl"] = "#river" };
        var feature = new GeospatialFeature(
            "extent-1",
            "extent-1/placemarks/0",
            new GeoPointGeometry(Position(5.1, 49.2)),
            CoordinateSystemId.Wgs84,
            attributes);

        attributes["name"] = "mutated";
        attributes.Remove("styleUrl");

        Assert.Equal("Verdun", feature.Attributes["name"]);
        Assert.Equal("#river", feature.Attributes["styleUrl"]);
        Assert.Equal(2, feature.Attributes.Count);
    }

    [Fact]
    public void GeospatialFeatureDefaultsAttributesAndAltitudeMode()
    {
        var feature = new GeospatialFeature(
            "extent-1",
            "extent-1/placemarks/0",
            new GeoPointGeometry(Position(5.1, 49.2)),
            CoordinateSystemId.Wgs84,
            altitudeMode: GeoAltitudeMode.ClampToGround);

        Assert.Empty(feature.Attributes);
        Assert.Equal(GeoAltitudeMode.ClampToGround, feature.AltitudeMode);
        Assert.Null(new GeospatialFeature(
            "extent-1",
            "extent-1/placemarks/0",
            new GeoPointGeometry(Position(5.1, 49.2)),
            CoordinateSystemId.Wgs84).AltitudeMode);
    }

    [Fact]
    public void GeospatialFeatureRejectsInvalidInput()
    {
        var geometry = new GeoPointGeometry(Position(5.1, 49.2));

        Assert.Throws<ArgumentException>(
            () => new GeospatialFeature("", "f", geometry, CoordinateSystemId.Wgs84));
        Assert.Throws<ArgumentException>(
            () => new GeospatialFeature("s", "", geometry, CoordinateSystemId.Wgs84));
        Assert.Throws<ArgumentNullException>(
            () => new GeospatialFeature("s", "f", null!, CoordinateSystemId.Wgs84));
    }
}
