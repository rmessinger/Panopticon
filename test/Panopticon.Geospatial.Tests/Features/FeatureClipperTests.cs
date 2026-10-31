#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Projection;
using Xunit;

namespace Panopticon.Geospatial.Tests.Features;

public class FeatureClipperTests
{
    private const double OriginEasting = 100000d;

    private const double OriginNorthing = 6000000d;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private static readonly CoordinateSystemId Tm4 = CoordinateSystemId.TransverseMercator(4d, 1d, 0d, 0d);

    [Fact]
    public void LineCrossingTheRectangle_IsClippedToExactBoundaryIntersections()
    {
        // Both intersection parameters are dyadic, so the clipped endpoints are exactly
        // on the rectangle boundary.
        var diagnostics = new GeoDiagnosticBag();
        var results = ClipFeatures(Line((-1280d, 640d, null), (1280d, 640d, null)), diagnostics);

        var part = Assert.Single(results);
        var line = Assert.IsType<GeoLineString>(part.Geometry);
        Assert.Equal(2, line.Positions.Count);
        Assert.Equal(0d, line.Positions[0].Coordinate.X);
        Assert.Equal(640d, line.Positions[0].Coordinate.Y);
        Assert.Equal(1280d, line.Positions[1].Coordinate.X);
        Assert.Equal(640d, line.Positions[1].Coordinate.Y);
        Assert.Null(line.Positions[0].AltitudeMeters);
        Assert.Empty(diagnostics.Items);
        Assert.Equal("area.kml/road#0", part.PartId);
    }

    [Fact]
    public void LineWithEndpointsOnCorners_IsKeptUnchanged()
    {
        var results = ClipFeatures(Line((0d, 0d, null), (1280d, 1280d, null)));

        var part = Assert.Single(results);
        var line = Assert.IsType<GeoLineString>(part.Geometry);
        Assert.Equal(0d, line.Positions[0].Coordinate.X);
        Assert.Equal(0d, line.Positions[0].Coordinate.Y);
        Assert.Equal(1280d, line.Positions[1].Coordinate.X);
        Assert.Equal(1280d, line.Positions[1].Coordinate.Y);
    }

    [Fact]
    public void LineLeavingAndReentering_SplitsIntoStableParts()
    {
        // The first segment exits the east edge at an exact dyadic fraction; the fourth
        // enters the north edge exactly; the fifth continues the same run.
        var results = ClipFeatures(
            Line(
                (100d, 100d, null),
                (1380d, 100d, null),
                (1380d, 1500d, null),
                (100d, 1536d, null),
                (100d, 512d, null),
                (300d, 512d, null)));

        Assert.Equal(2, results.Count);
        Assert.Equal("area.kml/road#0", results[0].PartId);
        Assert.Equal("area.kml/road#1", results[1].PartId);

        var first = Assert.IsType<GeoLineString>(results[0].Geometry);
        Assert.Equal(2, first.Positions.Count);
        Assert.Equal(100d, first.Positions[0].Coordinate.X);
        Assert.Equal(100d, first.Positions[0].Coordinate.Y);
        Assert.Equal(1280d, first.Positions[1].Coordinate.X);
        Assert.Equal(100d, first.Positions[1].Coordinate.Y);

        var second = Assert.IsType<GeoLineString>(results[1].Geometry);
        Assert.Equal(3, second.Positions.Count);
        Assert.Equal(100d, second.Positions[0].Coordinate.X);
        Assert.Equal(1280d, second.Positions[0].Coordinate.Y);
        Assert.Equal(100d, second.Positions[1].Coordinate.X);
        Assert.Equal(512d, second.Positions[1].Coordinate.Y);
        Assert.Equal(300d, second.Positions[2].Coordinate.X);
        Assert.Equal(512d, second.Positions[2].Coordinate.Y);
    }

    [Fact]
    public void LineOnlyTouchingTheCorner_IsDroppedWithDegenerateInfo()
    {
        // The segment passes exactly through the (0, 1280) corner without entering.
        var diagnostics = new GeoDiagnosticBag();
        var results = ClipFeatures(Line((-100d, 1180d, null), (100d, 1380d, null)), diagnostics);

        Assert.Empty(results);
        AssertDiagnostic(diagnostics, FeatureDiagnosticCodes.FeatureDegenerateAfterClip, GeoSeverity.Info);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void LineClippingInterpolatesAltitudeOnNewBoundaryVertices()
    {
        var results = ClipFeatures(Line((-1280d, 640d, 100d), (1280d, 640d, 300d)));

        var line = Assert.IsType<GeoLineString>(Assert.Single(results).Geometry);
        Assert.Equal(200d, line.Positions[0].AltitudeMeters);
        Assert.Equal(300d, line.Positions[1].AltitudeMeters);
    }

    [Fact]
    public void PointsOnTheBoundaryAreKeptAndPointsOutsideAreDroppedWithInfo()
    {
        var kept = new[]
        {
            Point(0d, 0d),
            Point(1280d, 640d),
            Point(640d, 1280d),
            Point(640d, 640d),
        };
        var dropped = new[]
        {
            Point(-1d, 640d),
            Point(1281d, 640d),
            Point(640d, -1d),
            Point(640d, 1281d),
        };

        var diagnostics = new GeoDiagnosticBag();
        var keptSource = new GeospatialFeature(
            "area.kml",
            "markers",
            new GeoMultiGeometry([.. kept]),
            Tm4,
            new Dictionary<string, string> { ["layer"] = "landmarks" },
            GeoAltitudeMode.ClampToGround);
        var droppedSource = new GeospatialFeature(
            "area.kml",
            "offmap",
            new GeoMultiGeometry([.. dropped]),
            Tm4);
        var results = FeatureClipper.Clip([keptSource, droppedSource], CreateGrid(), Transformer, diagnostics);

        Assert.Equal(4, results.Count);
        for (var i = 0; i < results.Count; i++)
        {
            Assert.Same(keptSource, results[i].SourceFeature);
            Assert.Equal("landmarks", results[i].SourceFeature.Attributes["layer"]);
            Assert.Equal(GeoAltitudeMode.ClampToGround, results[i].SourceFeature.AltitudeMode);
            Assert.Equal("area.kml/markers#" + i.ToString(CultureInfo.InvariantCulture), results[i].PartId);
        }

        AssertDiagnostic(diagnostics, FeatureDiagnosticCodes.FeatureDroppedOutside, GeoSeverity.Info);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void PolygonStraddlingTheCorner_ProducesTheExpectedClippedRing()
    {
        var results = ClipFeatures(
            Polygon(Ring((-640d, -640d), (640d, -640d), (640d, 640d), (-640d, 640d))));

        var polygon = Assert.IsType<GeoPolygon>(Assert.Single(results).Geometry);
        AssertRing(
            polygon.OuterRing,
            (0d, 0d),
            (640d, 0d),
            (640d, 640d),
            (0d, 640d));
        Assert.Empty(polygon.InnerRings);
    }

    [Fact]
    public void ConcaveUClippedIntoDisconnectedPieces_KeepsSeparatePartsWithStableIds()
    {
        // The connecting column sits west of the map edge, so clipping cuts the U into
        // two disconnected rectangles inside the map.
        var results = ClipFeatures(
            Polygon(
                Ring(
                    (-300d, 200d),
                    (600d, 200d),
                    (600d, 400d),
                    (-100d, 400d),
                    (-100d, 600d),
                    (600d, 600d),
                    (600d, 800d),
                    (-300d, 800d))),
            featureId: "area");

        Assert.Equal(2, results.Count);
        Assert.Equal("area.kml/area#0", results[0].PartId);
        Assert.Equal("area.kml/area#1", results[1].PartId);

        var first = Assert.IsType<GeoPolygon>(results[0].Geometry);
        AssertRing(first.OuterRing, (0d, 200d), (600d, 200d), (600d, 400d), (0d, 400d));

        var second = Assert.IsType<GeoPolygon>(results[1].Geometry);
        AssertRing(second.OuterRing, (0d, 600d), (600d, 600d), (600d, 800d), (0d, 800d));
    }

    [Fact]
    public void FullyInsidePolygon_NormalizesWindingStartVertexHoleOrderAndAltitudes()
    {
        // Clockwise shell and counter-clockwise holes given in reverse order: the output
        // must be canonical regardless of the input orientation.
        var shell = Ring((50d, 50d, 10d), (50d, 300d, 20d), (520d, 300d, 30d), (520d, 50d, 40d));
        var hole2 = Ring((400d, 150d), (480d, 150d), (480d, 220d), (400d, 220d));
        var hole1 = Ring((150d, 150d), (250d, 150d), (250d, 250d), (150d, 250d));

        var results = ClipFeatures(Polygon(shell, hole2, hole1));

        var polygon = Assert.IsType<GeoPolygon>(Assert.Single(results).Geometry);
        AssertRing(polygon.OuterRing, (50d, 50d), (520d, 50d), (520d, 300d), (50d, 300d));
        AssertAltitudes(polygon.OuterRing, [10d, 40d, 30d, 20d, 10d]);

        Assert.Equal(2, polygon.InnerRings.Count);
        AssertRing(polygon.InnerRings[0], (150d, 150d), (150d, 250d), (250d, 250d), (250d, 150d));
        AssertRing(polygon.InnerRings[1], (400d, 150d), (400d, 220d), (480d, 220d), (480d, 150d));
    }

    [Fact]
    public void HoleStraddlingTheMapEdge_CutsANotchAndDropsTheHoleRing()
    {
        var hole = Ring((1140d, 100d, 5d), (1380d, 100d, null), (1380d, 300d, 7d), (1140d, 300d, 6d));
        var shell = Ring((-200d, 50d), (1480d, 50d), (1480d, 350d), (-200d, 350d));

        var results = ClipFeatures(Polygon(shell, hole));

        var polygon = Assert.IsType<GeoPolygon>(Assert.Single(results).Geometry);
        Assert.Empty(polygon.InnerRings);

        // The surviving hole vertices keep their source altitudes; vertices created on
        // the map edge get zero.
        AssertRing(
            polygon.OuterRing,
            (0d, 50d),
            (1280d, 50d),
            (1280d, 100d),
            (1140d, 100d),
            (1140d, 300d),
            (1280d, 300d),
            (1280d, 350d),
            (0d, 350d));
        AssertAltitudes(polygon.OuterRing, [0d, 0d, 0d, 5d, 6d, 0d, 0d, 0d, 0d]);
    }

    [Fact]
    public void RetainedHoleInsideTheMap_StaysExcluded()
    {
        var hole = Ring((400d, 400d), (700d, 400d), (700d, 700d), (400d, 700d));
        var shell = Ring((100d, 100d), (1100d, 100d), (1100d, 1100d), (100d, 1100d));

        var results = ClipFeatures(Polygon(shell, hole));

        var polygon = Assert.IsType<GeoPolygon>(Assert.Single(results).Geometry);
        AssertRing(polygon.OuterRing, (100d, 100d), (1100d, 100d), (1100d, 1100d), (100d, 1100d));
        var retained = Assert.Single(polygon.InnerRings);
        AssertRing(retained, (400d, 400d), (400d, 700d), (700d, 700d), (700d, 400d));
    }

    [Fact]
    public void SelfIntersectingPolygon_IsAGenerateErrorAndIsNotAutoRepaired()
    {
        var diagnostics = new GeoDiagnosticBag();
        var results = ClipFeatures(
            Polygon(Ring((340d, 540d), (940d, 540d), (340d, 740d), (940d, 740d))),
            diagnostics);

        Assert.Empty(results);
        var diagnostic = AssertDiagnostic(diagnostics, FeatureDiagnosticCodes.FeatureInvalidTopology, GeoSeverity.Error);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void PolygonTouchingOnlyTheMapEdge_IsDroppedWithDegenerateInfo()
    {
        var diagnostics = new GeoDiagnosticBag();
        var results = ClipFeatures(
            Polygon(Ring((1280d, 100d), (1380d, 100d), (1380d, 200d), (1280d, 200d))),
            diagnostics);

        Assert.Empty(results);
        AssertDiagnostic(diagnostics, FeatureDiagnosticCodes.FeatureDegenerateAfterClip, GeoSeverity.Info);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void PolygonAndLineFullyOutside_AreDroppedWithInfo()
    {
        var diagnostics = new GeoDiagnosticBag();
        var results = ClipFeatures(
            new GeoMultiGeometry(
            [
                Polygon(Ring((2000d, 2000d), (2100d, 2000d), (2100d, 2100d), (2000d, 2100d))),
                Line((2000d, 2200d, null), (2300d, 2200d, null)),
            ]),
            diagnostics);

        Assert.Empty(results);
        AssertDiagnostic(diagnostics, FeatureDiagnosticCodes.FeatureDroppedOutside, GeoSeverity.Info);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void NonFiniteCoordinate_IsACoordinatesError()
    {
        var diagnostics = new GeoDiagnosticBag();
        var results = ClipFeatures(Line((double.NaN, 640d, null), (100d, 100d, null)), diagnostics);

        Assert.Empty(results);
        var diagnostic = AssertDiagnostic(diagnostics, FeatureDiagnosticCodes.FeatureNonFiniteCoordinate, GeoSeverity.Error);
        Assert.Equal(GeoStage.Coordinates, diagnostic.Stage);
    }

    [Fact]
    public void FailedProjection_ReportsATypedErrorAndContinuesWithLaterFeatures()
    {
        var projectedPoint = Point(100d, 100d);
        var geographic = new TransverseMercatorProjection(4d, 1d, 0d, 0d).Inverse(OriginEasting, OriginNorthing);
        var failing = new GeospatialFeature(
            "area.kml",
            "far",
            new GeoPointGeometry(new GeoPosition(geographic.ToVector2d())),
            CoordinateSystemId.Wgs84);

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureClipper.Clip([failing, Feature("near", projectedPoint)], CreateGrid(), new ThrowingTransformer(), diagnostics);

        Assert.Single(results);
        Assert.Equal("area.kml/near#0", results[0].PartId);

        var diagnostic = AssertDiagnostic(diagnostics, FeatureDiagnosticCodes.FeatureProjectionFailed, GeoSeverity.Error);
        Assert.Equal(GeoStage.Coordinates, diagnostic.Stage);
        Assert.NotNull(diagnostic.Detail);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void TransformerIsInjectable_AndIsUsedForForeignSourceCrs()
    {
        var shifted = new GeospatialFeature(
            "area.kml",
            "shifted",
            new GeoPointGeometry(new GeoPosition(new Vector2d(OriginEasting - 700d, OriginNorthing - 1600d))),
            CoordinateSystemId.TransverseMercator(5d, 1d, 0d, 0d));

        var results = FeatureClipper.Clip([shifted], CreateGrid(), new ShiftTransformer(new Vector2d(1000d, 2000d)), new GeoDiagnosticBag());

        var point = Assert.IsType<GeoPointGeometry>(Assert.Single(results).Geometry);
        Assert.Equal(300d, point.Position.Coordinate.X);
        Assert.Equal(400d, point.Position.Coordinate.Y);
    }

    [Fact]
    public void Wgs84FeatureIsProjectedThroughTheTransformerIntoGameXY()
    {
        var geographic = new TransverseMercatorProjection(4d, 1d, 0d, 0d)
            .Inverse(OriginEasting + 640d, OriginNorthing + 640d);
        var feature = new GeospatialFeature(
            "area.kml",
            "landmark",
            new GeoPointGeometry(new GeoPosition(geographic.ToVector2d(), 25d)),
            CoordinateSystemId.Wgs84);

        var results = FeatureClipper.Clip([feature], CreateGrid(), Transformer, new GeoDiagnosticBag());

        var point = Assert.IsType<GeoPointGeometry>(Assert.Single(results).Geometry);
        Assert.Equal(640d, point.Position.Coordinate.X, 6);
        Assert.Equal(640d, point.Position.Coordinate.Y, 6);
        Assert.Equal(25d, point.Position.AltitudeMeters);
    }

    [Fact]
    public void MultiGeometryParts_GetSequentialStableIdsInDocumentOrder()
    {
        var results = ClipFeatures(
            new GeoMultiGeometry(
            [
                Point(200d, 200d),
                Line((50d, 50d, null), (1230d, 50d, null)),
                Point(100d, 100d),
            ]),
            featureId: "cluster");

        Assert.Equal(3, results.Count);
        Assert.Equal("area.kml/cluster#0", results[0].PartId);
        Assert.Equal("area.kml/cluster#1", results[1].PartId);
        Assert.Equal("area.kml/cluster#2", results[2].PartId);
        Assert.Equal(200d, Assert.IsType<GeoPointGeometry>(results[0].Geometry).Position.Coordinate.X);
        Assert.Equal(100d, Assert.IsType<GeoPointGeometry>(results[2].Geometry).Position.Coordinate.X);
    }

    [Fact]
    public void NestingBeyondTheReaderBound_IsAParseError()
    {
        GeoGeometry nested = Point(100d, 100d);
        for (var i = 0; i < FeatureClipper.MaxGeometryNestingDepth + 2; i++)
        {
            nested = new GeoMultiGeometry([nested]);
        }

        var diagnostics = new GeoDiagnosticBag();
        var results = ClipFeatures(nested, diagnostics);

        Assert.Empty(results);
        var diagnostic = AssertDiagnostic(diagnostics, FeatureDiagnosticCodes.FeatureNestingTooDeep, GeoSeverity.Error);
        Assert.Equal(GeoStage.Parse, diagnostic.Stage);
    }

    [Fact]
    public void CancelledTokenThrowsBeforeAnyOutput()
    {
        var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => ClipFeatures(Point(100d, 100d), diagnostics: new GeoDiagnosticBag(), cancellation: source.Token));
    }

    [Fact]
    public void ClippedOutputAboveTheFeatureLimit_IsAGenerateErrorAndStops()
    {
        var diagnostics = new GeoDiagnosticBag();
        var limits = new GeospatialImportLimits(MaxFeatureCount: 1);
        var results = FeatureClipper.Clip(
            [Feature("a", Point(100d, 100d)), Feature("b", Point(200d, 200d))],
            CreateGrid(),
            Transformer,
            diagnostics,
            limits);

        Assert.Single(results);
        Assert.Equal("area.kml/a#0", results[0].PartId);
        var diagnostic = AssertDiagnostic(diagnostics, FeatureDiagnosticCodes.FeatureOutputLimitExceeded, GeoSeverity.Error);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Contains("maxFeatureCount", diagnostic.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<LocalFeature> ClipFeatures(
        GeoGeometry geometry,
        GeoDiagnosticBag? diagnostics = null,
        ICoordinateTransformer? transformer = null,
        GeospatialImportLimits? limits = null,
        string featureId = "road",
        CancellationToken cancellation = default)
    {
        return FeatureClipper.Clip(
            [Feature(featureId, geometry)],
            CreateGrid(),
            transformer ?? Transformer,
            diagnostics ?? new GeoDiagnosticBag(),
            limits,
            cancellation);
    }

    private static LocalMapGrid CreateGrid()
    {
        var extent = new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(OriginEasting, OriginNorthing)),
                new GeoPosition(new Vector2d(OriginEasting + 1280d, OriginNorthing)),
                new GeoPosition(new Vector2d(OriginEasting + 1280d, OriginNorthing + 1280d)),
                new GeoPosition(new Vector2d(OriginEasting, OriginNorthing + 1280d)),
                new GeoPosition(new Vector2d(OriginEasting, OriginNorthing)),
            ]));

        var grid = LocalMapGrid.Create(extent, Tm4, CreateManifest(), Transformer, new GeoDiagnosticBag());

        Assert.NotNull(grid);
        return grid!;
    }

    private static GeospatialImportManifest CreateManifest()
    {
        return new GeospatialImportManifest
        {
            Sources =
            [
                new ManifestSource("extent", ManifestSourceKind.Kml, "area.kml", ManifestSourceRole.Extent),
                new ManifestSource("dem", ManifestSourceKind.Geotiff, "dem.tif", ManifestSourceRole.Elevation, "EGM96"),
            ],
            TargetCoordinateSystem = new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: 4d),
            MetersPerGameUnit = 1d,
            DefaultMaterial = "grass",
            VerticalPolicy = new VerticalPolicy(),
            MaxScaleError = 0.001d,
        };
    }

    private static GeospatialFeature Feature(string id, GeoGeometry geometry)
    {
        return new GeospatialFeature("area.kml", id, geometry, Tm4);
    }

    private static GeoPointGeometry Point(double x, double y)
    {
        return new GeoPointGeometry(new GeoPosition(new Vector2d(OriginEasting + x, OriginNorthing + y)));
    }

    private static GeoLineString Line(params (double X, double Y, double? Altitude)[] stops)
    {
        var positions = new GeoPosition[stops.Length];
        for (var i = 0; i < stops.Length; i++)
        {
            positions[i] = new GeoPosition(
                new Vector2d(OriginEasting + stops[i].X, OriginNorthing + stops[i].Y),
                stops[i].Altitude);
        }

        return new GeoLineString(positions);
    }

    private static GeoPolygon Polygon(GeoRing outerRing, params GeoRing[] holes)
    {
        return new GeoPolygon(outerRing, holes);
    }

    private static GeoRing Ring(params (double X, double Y, double? Altitude)[] vertices)
    {
        return RingOf(vertices);
    }

    private static GeoRing Ring(params (double X, double Y)[] vertices)
    {
        return RingOf(
            Array.ConvertAll(vertices, v => (v.X, v.Y, (double?)null)));
    }

    private static GeoRing RingOf((double X, double Y, double? Altitude)[] vertices)
    {
        var positions = new List<GeoPosition>(vertices.Length + 1);
        foreach (var vertex in vertices)
        {
            positions.Add(new GeoPosition(
                new Vector2d(OriginEasting + vertex.X, OriginNorthing + vertex.Y),
                vertex.Altitude));
        }

        positions.Add(positions[0]);
        return new GeoRing(positions);
    }

    private static void AssertRing(GeoRing ring, params (double X, double Y)[] expected)
    {
        Assert.Equal(expected.Length + 1, ring.Positions.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].X, ring.Positions[i].Coordinate.X);
            Assert.Equal(expected[i].Y, ring.Positions[i].Coordinate.Y);
        }

        Assert.Equal(ring.Positions[0].Coordinate, ring.Positions[^1].Coordinate);
    }

    private static void AssertAltitudes(GeoRing ring, double?[] expected)
    {
        Assert.Equal(expected.Length, ring.Positions.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], ring.Positions[i].AltitudeMeters);
        }
    }

    private static GeoDiagnostic AssertDiagnostic(GeoDiagnosticBag diagnostics, string code, GeoSeverity severity)
    {
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(severity, diagnostic.Severity);

        return diagnostic;
    }

    private sealed class ThrowingTransformer : ICoordinateTransformer
    {
        public Vector2d Transform(Vector2d point, CoordinateSystemId source, CoordinateSystemId target)
        {
            throw new NotSupportedException("No transform is available for this CRS pair.");
        }
    }

    private sealed class ShiftTransformer(Vector2d offset) : ICoordinateTransformer
    {
        public Vector2d Transform(Vector2d point, CoordinateSystemId source, CoordinateSystemId target)
        {
            return new Vector2d(point.X + offset.X, point.Y + offset.Y);
        }
    }
}
