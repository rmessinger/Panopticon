using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Readers;
using Xunit;

namespace Panopticon.Geospatial.Tests.Readers;

public class KmlReaderTests
{
    private const string Ns = "http://www.opengis.net/kml/2.2";

    private static (IReadOnlyList<GeospatialFeature> Features, GeoDiagnosticBag Diagnostics) ReadKml(
        string kml,
        GeospatialImportLimits limits = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(kml));
        var diagnostics = new GeoDiagnosticBag();
        var features = new KmlReader().Read(stream, "kml", limits ?? new GeospatialImportLimits(), diagnostics);

        return (features, diagnostics);
    }

    private static string WriteTempKml(string kml)
    {
        var path = Path.Combine(Path.GetTempPath(), "panopticon-kml-" + Guid.NewGuid().ToString("N") + ".kml");
        File.WriteAllText(path, kml);

        return path;
    }

    private static string Kml(string documentContent)
    {
        return $"""
            <kml xmlns="{Ns}"><Document>{documentContent}</Document></kml>
            """;
    }

    private static string Placemark(string placemarkContent)
    {
        return $"<Placemark>{placemarkContent}</Placemark>";
    }

    private static void AssertSingleError(GeoDiagnosticBag diagnostics, string code)
    {
        var error = Assert.Single(diagnostics.Items, item => item.Severity == GeoSeverity.Error);

        Assert.Equal(code, error.Code);
        Assert.Equal(GeoStage.Parse, error.Stage);
    }

    [Fact]
    public void ReadsPointWithAttributesAltitudeAndAltitudeMode()
    {
        var kml = Kml(Placemark(
            """
            <name>Bridge</name><styleUrl>#bridgeStyle</styleUrl>
            <Point><altitudeMode>relativeToGround</altitudeMode><coordinates>2.25,48.5,120.5</coordinates></Point>
            """));

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        var feature = Assert.Single(features);
        Assert.Equal("kml", feature.SourceId);
        Assert.Equal("Bridge#1", feature.FeatureId);
        Assert.Equal(CoordinateSystemId.Wgs84, feature.CoordinateSystem);
        Assert.Equal(GeoAltitudeMode.RelativeToGround, feature.AltitudeMode);
        var point = Assert.IsType<GeoPointGeometry>(feature.Geometry);

        Assert.Equal(new Vector2d(2.25, 48.5), point.Position.Coordinate);
        Assert.Equal(120.5, point.Position.AltitudeMeters);
        Assert.Equal("Bridge", feature.Attributes["name"]);
        Assert.Equal(string.Empty, feature.Attributes["folder"]);
        Assert.Equal("#bridgeStyle", feature.Attributes["styleUrl"]);
        Assert.Equal("relativeToGround", feature.Attributes["altitudeMode"]);
    }

    [Fact]
    public void ReadsLineStringWithMultipleCoordinates()
    {
        var kml = Kml(Placemark("<LineString><coordinates>2,48 3,49 4,50</coordinates></LineString>"));

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        var line = Assert.IsType<GeoLineString>(Assert.Single(features).Geometry);
        Assert.Equal(3, line.Positions.Count);
        Assert.Equal(4, line.Positions[2].Coordinate.X);
        Assert.Null(line.Positions[0].AltitudeMeters);
    }

    [Fact]
    public void ReadsPolygonWithOuterRingAndHoles()
    {
        var kml = Kml(Placemark(
            """
            <Polygon>
              <outerBoundaryIs><LinearRing><coordinates>0,0 0,4 4,4 4,0 0,0</coordinates></LinearRing></outerBoundaryIs>
              <innerBoundaryIs><LinearRing><coordinates>1,1 1,2 2,2 2,1 1,1</coordinates></LinearRing></innerBoundaryIs>
              <innerBoundaryIs><LinearRing><coordinates>3,3 3,3.5 3.5,3.5 3.5,3 3,3</coordinates></LinearRing></innerBoundaryIs>
            </Polygon>
            """));

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        var polygon = Assert.IsType<GeoPolygon>(Assert.Single(features).Geometry);

        Assert.Equal(5, polygon.OuterRing.Positions.Count);
        Assert.Equal(2, polygon.InnerRings.Count);
        Assert.Equal(5, polygon.InnerRings[0].Positions.Count);
    }

    [Fact]
    public void AutoClosesUnclosedRings()
    {
        var kml = Kml(Placemark(
            """
            <Polygon>
              <outerBoundaryIs><LinearRing><coordinates>0,0 0,4 4,4 4,0</coordinates></LinearRing></outerBoundaryIs>
            </Polygon>
            """));

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        var ring = Assert.IsType<GeoPolygon>(Assert.Single(features).Geometry).OuterRing;

        Assert.Equal(5, ring.Positions.Count);
        Assert.Equal(ring.Positions[0], ring.Positions[^1]);
    }

    [Fact]
    public void NestedFoldersBuildFolderPathAndStableIds()
    {
        var kml = Kml(
            """
            <Folder><name>Front</name>
              <Folder><name>North</name>
                <Placemark><name>Hill</name><Point><coordinates>1,1</coordinates></Point></Placemark>
                <Placemark><name>Hill</name><Point><coordinates>2,2</coordinates></Point></Placemark>
                <Placemark><Point><coordinates>3,3</coordinates></Point></Placemark>
              </Folder>
            </Folder>
            """);

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(3, features.Count);
        Assert.Equal(["Front/North/Hill#1", "Front/North/Hill#2", "Front/North/#3"], features.Select(feature => feature.FeatureId));
        Assert.All(features, feature => Assert.Equal("Front/North", feature.Attributes["folder"]));
    }

    [Fact]
    public void ExplicitPlacemarkIdWinsOverSynthesizedId()
    {
        var kml = Kml(Placemark("<name>x</name><Point><coordinates>1,1</coordinates></Point>").Replace("<Placemark>", "<Placemark id=\"landmark-7\">"));

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("landmark-7", Assert.Single(features).FeatureId);
    }

    [Fact]
    public void NestedMultiGeometryPreservesTreeAndStableIds()
    {
        var kml = Kml(
            """
            <Placemark id="mg">
              <MultiGeometry>
                <Point><coordinates>1,1</coordinates></Point>
                <LineString><coordinates>0,0 2,2</coordinates></LineString>
                <MultiGeometry>
                  <Polygon>
                    <outerBoundaryIs><LinearRing><coordinates>0,0 0,1 1,1 1,0 0,0</coordinates></LinearRing></outerBoundaryIs>
                    <innerBoundaryIs><LinearRing><coordinates>0.2,0.2 0.2,0.4 0.4,0.4 0.4,0.2 0.2,0.2</coordinates></LinearRing></innerBoundaryIs>
                  </Polygon>
                </MultiGeometry>
              </MultiGeometry>
            </Placemark>
            """);

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        var feature = Assert.Single(features);
        Assert.Equal("mg", feature.FeatureId);
        var multi = Assert.IsType<GeoMultiGeometry>(feature.Geometry);

        Assert.Equal(3, multi.Geometries.Count);
        Assert.IsType<GeoPointGeometry>(multi.Geometries[0]);
        Assert.IsType<GeoLineString>(multi.Geometries[1]);
        var nested = Assert.IsType<GeoMultiGeometry>(multi.Geometries[2]);
        var polygon = Assert.IsType<GeoPolygon>(Assert.Single(nested.Geometries));
        Assert.Single(polygon.InnerRings);
    }

    [Fact]
    public void ReadsExtendedDataAndSchemaDataAttributes()
    {
        var kml = Kml(Placemark(
            """
            <name>Bridge</name>
            <ExtendedData>
              <Data name="kind"><displayName>Kind</displayName><value>bridge</value></Data>
              <SchemaData schemaUrl="#bridgeSchema">
                <SimpleData name="height">12.5</SimpleData>
                <SimpleData name="span">300</SimpleData>
              </SchemaData>
            </ExtendedData>
            <Point><coordinates>1,2</coordinates></Point>
            """));

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        var attributes = Assert.Single(features).Attributes;

        Assert.Equal("bridge", attributes["kind"]);
        Assert.Equal("12.5", attributes["height"]);
        Assert.Equal("300", attributes["span"]);
        Assert.Equal("Bridge", attributes["name"]);
    }

    [Fact]
    public void WarnsAndSkipsUnsupportedElements()
    {
        var kml = $"""
            <kml xmlns="{Ns}">
              <Document>
                <NetworkLink><name>remote</name><Url><href>http://example.com/x.kml</href></Url></NetworkLink>
                <GroundOverlay><Icon><href>http://example.com/img.png</href></Icon></GroundOverlay>
                <Placemark><Model><Link><href>model.dae</href></Link></Model></Placemark>
                <Placemark id="kept"><Point><coordinates>1,2</coordinates></Point></Placemark>
              </Document>
              <NetworkLinkControl><Update><targetHref>http://example.com/x.kml</targetHref></Update></NetworkLinkControl>
              <gx:Tour xmlns:gx="http://www.google.com/kml/ext/2.2"><gx:Playlist/></gx:Tour>
            </kml>
            """;

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        var warnings = diagnostics.Items.Where(item => item.Severity == GeoSeverity.Warning).ToList();
        Assert.Equal(5, warnings.Count);
        Assert.All(warnings, warning =>
        {
            Assert.Equal(GeoDiagnosticCodes.ParseUnsupportedKmlElement, warning.Code);
            Assert.Equal(GeoStage.Parse, warning.Stage);
        });
        Assert.Equal("kept", Assert.Single(features).FeatureId);
    }

    [Fact]
    public void WarnsForPlacemarkWithoutSupportedGeometry()
    {
        var kml = Kml(Placemark("<name>empty</name>"));

        var (features, diagnostics) = ReadKml(kml);

        Assert.Empty(features);
        Assert.False(diagnostics.HasErrors);
        var warning = Assert.Single(diagnostics.Items);

        Assert.Equal(GeoDiagnosticCodes.ParseUnsupportedKmlElement, warning.Code);
    }

    [Fact]
    public void RejectsDtdWithoutResolvingEntities()
    {
        var kml =
            """
            <!DOCTYPE kml [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <kml xmlns="http://www.opengis.net/kml/2.2">
              <Document><Placemark><name>&xxe;</name><Point><coordinates>1,2</coordinates></Point></Placemark></Document>
            </kml>
            """;

        var (features, diagnostics) = ReadKml(kml);

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
        Assert.IsType<System.Xml.XmlException>(diagnostics.Items.Single(item => item.Severity == GeoSeverity.Error).Detail);
    }

    [Fact]
    public void RejectsNonKmlRoot()
    {
        var (features, diagnostics) = ReadKml("<html><body>x</body></html>");

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
    }

    [Fact]
    public void RejectsMalformedCoordinatesAndKeepsValidFeatures()
    {
        string[] malformed =
        [
            "abc,2",
            "1,2,abc",
            "NaN,1",
            "1,Infinity",
            "200,0",
            "0,-95",
            "1",
            "1,2,3,4",
        ];
        var placemarks = string.Join(string.Empty, malformed.Select(tuple =>
            $"<Placemark><LineString><coordinates>{tuple}</coordinates></LineString></Placemark>"));
        var kml = Kml(placemarks + Placemark("<name>valid</name><Point><coordinates>1,2</coordinates></Point>"));

        var (features, diagnostics) = ReadKml(kml);

        Assert.Single(features);
        Assert.Equal(8, diagnostics.Items.Count(item => item.Severity == GeoSeverity.Error));
        Assert.All(
            diagnostics.Items.Where(item => item.Severity == GeoSeverity.Error),
            error => Assert.Equal(GeoDiagnosticCodes.ParseFailure, error.Code));
    }

    [Fact]
    public void RejectsPointWithWrongTupleCount()
    {
        var kml = Kml(Placemark("<Point><coordinates>1,2 3,4</coordinates></Point>"));

        var (features, diagnostics) = ReadKml(kml);

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
    }

    [Fact]
    public void RejectsDegenerateGeometries()
    {
        var kml = Kml(
            Placemark("<LineString><coordinates>1,2</coordinates></LineString>")
            + Placemark("<LineString><coordinates/></LineString>")
            + Placemark("<Polygon><innerBoundaryIs><LinearRing><coordinates>0,0 0,1 1,1 0,0</coordinates></LinearRing></innerBoundaryIs></Polygon>")
            + Placemark("<MultiGeometry><name>nothing</name></MultiGeometry>"));

        var (features, diagnostics) = ReadKml(kml);

        Assert.Empty(features);
        Assert.Equal(4, diagnostics.Items.Count(item => item.Severity == GeoSeverity.Error));
    }

    [Fact]
    public void CoordinateLimitFailsWithParseErrorBeforeCompleting()
    {
        var kml = Kml(
            Placemark("<LineString><coordinates>0,0 1,1 2,2 3,3</coordinates></LineString>")
            + Placemark("<name>sentinel</name><Point><coordinates>9,9</coordinates></Point>"));
        var limits = new GeospatialImportLimits(MaxCoordinateCount: 3);

        var (features, diagnostics) = ReadKml(kml, limits);

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseKmlLimitExceeded);
    }

    [Fact]
    public void FeatureLimitStopsReadingAndReturnsPartialList()
    {
        var kml = Kml(
            Placemark("<name>a</name><Point><coordinates>1,1</coordinates></Point>")
            + Placemark("<name>b</name><Point><coordinates>2,2</coordinates></Point>")
            + Placemark("<name>sentinel</name><Point><coordinates>3,3</coordinates></Point>"));
        var limits = new GeospatialImportLimits(MaxFeatureCount: 1);

        var (features, diagnostics) = ReadKml(kml, limits);

        var feature = Assert.Single(features);
        Assert.Equal("a", feature.Attributes["name"]);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseKmlLimitExceeded);
    }

    [Fact]
    public void EntryByteLimitRejectsFileBeforeParsing()
    {
        var kml = Kml(Placemark("<Point><coordinates>1,2</coordinates></Point>"));
        var path = WriteTempKml(kml);

        try
        {
            var diagnostics = new GeoDiagnosticBag();
            var features = new KmlReader().Read(path, new GeospatialImportLimits(MaxKmlEntryBytes: 10), diagnostics);

            Assert.Empty(features);
            AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseKmlLimitExceeded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PathOverloadUsesFileNameAsSourceId()
    {
        var kml = Kml(Placemark("<name>p</name><Point><coordinates>1,2</coordinates></Point>"));
        var path = WriteTempKml(kml);

        try
        {
            var diagnostics = new GeoDiagnosticBag();
            var features = new KmlReader().Read(path, new GeospatialImportLimits(), diagnostics);

            Assert.False(diagnostics.HasErrors);
            var feature = Assert.Single(features);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), feature.SourceId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StreamOverloadMatchesPathOverload()
    {
        var kml = Kml(
            Placemark("<name>a</name><Point><coordinates>1,2</coordinates></Point>")
            + Placemark("<name>b</name><LineString><coordinates>0,0 1,1</coordinates></LineString>"));
        var path = WriteTempKml(kml);

        try
        {
            var pathDiagnostics = new GeoDiagnosticBag();
            var pathFeatures = new KmlReader().Read(path, new GeospatialImportLimits(), pathDiagnostics);
            var streamDiagnostics = new GeoDiagnosticBag();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(kml));
            var streamFeatures = new KmlReader().Read(stream, "doc", new GeospatialImportLimits(), streamDiagnostics);

            Assert.False(pathDiagnostics.HasErrors);
            Assert.False(streamDiagnostics.HasErrors);
            Assert.Equal(pathFeatures.Count, streamFeatures.Count);
            Assert.Equal(pathFeatures.Select(feature => feature.FeatureId), streamFeatures.Select(feature => feature.FeatureId));
            Assert.Equal(
                pathFeatures.Select(feature => feature.Geometry.GetType()),
                streamFeatures.Select(feature => feature.Geometry.GetType()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AcceptsKml20NamespaceWithPrefixes()
    {
        var kml =
            """
            <k:kml xmlns:k="http://earth.google.com/kml/2.0">
              <k:Document>
                <k:Placemark><k:name>legacy</k:name><k:Point><k:coordinates>2,48</k:coordinates></k:Point></k:Placemark>
              </k:Document>
            </k:kml>
            """;

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        var feature = Assert.Single(features);
        Assert.Equal("legacy", feature.Attributes["name"]);
        var point = Assert.IsType<GeoPointGeometry>(feature.Geometry);

        Assert.Equal(2, point.Position.Coordinate.X);
        Assert.Equal(48, point.Position.Coordinate.Y);
    }

    [Fact]
    public void TreatsGxSeaFloorAltitudeModeAsClampToGroundWithWarning()
    {
        var kml =
            $"""
            <kml xmlns="{Ns}" xmlns:gx="http://www.google.com/kml/ext/2.2">
              <Document>
                <Placemark>
                  <Point><gx:altitudeMode>clampToSeaFloor</gx:altitudeMode><coordinates>1,2</coordinates></Point>
                </Placemark>
              </Document>
            </kml>
            """;

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        var feature = Assert.Single(features);
        Assert.Equal(GeoAltitudeMode.ClampToGround, feature.AltitudeMode);
        Assert.Equal("clampToSeaFloor", feature.Attributes["altitudeMode"]);
        Assert.Single(diagnostics.Items, item => item.Severity == GeoSeverity.Warning);
    }

    [Fact]
    public void DuplicateChildrenAreConsumedAndFirstValueWins()
    {
        var kml = Kml(
            """
            <Placemark>
              <name>first</name><name>second</name>
              <styleUrl>#a</styleUrl><styleUrl>#b</styleUrl>
              <Point><coordinates>1,2</coordinates><coordinates>9,9</coordinates></Point>
            </Placemark>
            <Placemark><name>after</name><Point><coordinates>3,3</coordinates></Point></Placemark>
            """);

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, features.Count);
        Assert.Equal("first", features[0].Attributes["name"]);
        Assert.Equal("#a", features[0].Attributes["styleUrl"]);
        Assert.Equal(1, Assert.IsType<GeoPointGeometry>(features[0].Geometry).Position.Coordinate.X);
        Assert.Equal("after", features[1].Attributes["name"]);
    }

    [Fact]
    public void DuplicateOuterBoundaryIsConsumedAndFirstRingWins()
    {
        var kml = Kml(
            """
            <Placemark>
              <Polygon>
                <outerBoundaryIs><LinearRing><coordinates>0,0 0,4 4,4 4,0 0,0</coordinates></LinearRing></outerBoundaryIs>
                <outerBoundaryIs><LinearRing><coordinates>9,9 9,10 10,10 10,9 9,9</coordinates></LinearRing></outerBoundaryIs>
              </Polygon>
            </Placemark>
            <Placemark><name>after</name><Point><coordinates>3,3</coordinates></Point></Placemark>
            """);

        var (features, diagnostics) = ReadKml(kml);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, features.Count);
        var ring = Assert.IsType<GeoPolygon>(features[0].Geometry).OuterRing;
        Assert.Equal(0, ring.Positions[0].Coordinate.X);
        Assert.Equal("after", features[1].Attributes["name"]);
    }

    [Fact]
    public void MissingCoordinatesElementsProduceParseErrors()
    {
        var kml = Kml(
            Placemark("<Point><altitudeMode>absolute</altitudeMode></Point>")
            + Placemark("<LineString><tessellate>1</tessellate></LineString>")
            + Placemark("<Polygon><outerBoundaryIs><LinearRing><extrude>1</extrude></LinearRing></outerBoundaryIs></Polygon>"));

        var (features, diagnostics) = ReadKml(kml);

        Assert.Empty(features);
        var errors = diagnostics.Items.Where(item => item.Severity == GeoSeverity.Error).ToList();
        Assert.Equal(3, errors.Count);
        Assert.All(errors, error => Assert.Equal(GeoDiagnosticCodes.ParseFailure, error.Code));
    }

    [Fact]
    public void RejectsDegenerateRingsButKeepsCollinearTriangles()
    {
        string PolygonWithRing(string ring) =>
            $"<Polygon><outerBoundaryIs><LinearRing><coordinates>{ring}</coordinates></LinearRing></outerBoundaryIs></Polygon>";
        var kml = Kml(
            Placemark(PolygonWithRing("0,0 0,0 0,0 0,0"))
            + Placemark(PolygonWithRing("0,0 0,1 0,0"))
            + Placemark(PolygonWithRing("0,0 1,0 2,0")));

        var (features, diagnostics) = ReadKml(kml);

        Assert.Equal(2, diagnostics.Items.Count(item => item.Severity == GeoSeverity.Error));
        var ring = Assert.IsType<GeoPolygon>(Assert.Single(features).Geometry).OuterRing;

        Assert.Equal(4, ring.Positions.Count);
        Assert.Equal(ring.Positions[0], ring.Positions[^1]);
    }

    [Fact]
    public void RejectsNestedMarkupInScalarElements()
    {
        var kml = Kml(
            """
            <Placemark><name>plain<b>bold</b>tail</name><Point><coordinates>1,2</coordinates></Point></Placemark>
            <Placemark><name>after</name><Point><coordinates>3,3</coordinates></Point></Placemark>
            """);

        var (features, diagnostics) = ReadKml(kml);

        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
        Assert.Equal(2, features.Count);
        Assert.Equal("plaintail", features[0].Attributes["name"]);
        Assert.Equal("after", features[1].Attributes["name"]);
    }

    [Fact]
    public void RejectsTrailingSecondRootElement()
    {
        var kml = Kml(Placemark("<Point><coordinates>1,2</coordinates></Point>")) + "<trailing/>";

        var (features, diagnostics) = ReadKml(kml);

        Assert.Single(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
    }

    [Fact]
    public void DeepMultiGeometryNestingIsRejectedAsLimit()
    {
        var kml = Kml(Placemark(
            string.Concat(Enumerable.Repeat("<MultiGeometry>", 200))
            + "<Point><coordinates>1,1</coordinates></Point>"
            + string.Concat(Enumerable.Repeat("</MultiGeometry>", 200))));

        var (features, diagnostics) = ReadKml(kml);

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseKmlLimitExceeded);
    }

    [Fact]
    public void DeepFolderNestingIsRejectedAsLimit()
    {
        var kml = Kml(
            string.Concat(Enumerable.Repeat("<Folder>", 200))
            + Placemark("<Point><coordinates>1,1</coordinates></Point>")
            + string.Concat(Enumerable.Repeat("</Folder>", 200)));

        var (features, diagnostics) = ReadKml(kml);

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseKmlLimitExceeded);
    }

    [Fact]
    public void ByteLimitIsEnforcedOnNonSeekableUtf16Streams()
    {
        var placemarks = string.Join(string.Empty, Enumerable.Repeat(
            "<Placemark><name>pad</name><Point><coordinates>1.25,48.5,320.5</coordinates></Point></Placemark>",
            15));
        var text = Kml(placemarks);
        var limitBytes = text.Length + 200;
        var bytes = new byte[Encoding.Unicode.GetPreamble().Length + (text.Length * 2)];
        Encoding.Unicode.GetPreamble().CopyTo(bytes, 0);
        Encoding.Unicode.GetBytes(text).CopyTo(bytes, 2);

        using var stream = new NonSeekableStream(bytes);
        var diagnostics = new GeoDiagnosticBag();
        var features = new KmlReader().Read(stream, "kml", new GeospatialImportLimits(MaxKmlEntryBytes: limitBytes), diagnostics);

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseKmlLimitExceeded);
    }

    [Fact]
    public void StreamReadFailureProducesTypedDiagnostic()
    {
        using var stream = new ThrowingStream();
        var diagnostics = new GeoDiagnosticBag();
        var features = new KmlReader().Read(stream, "kml", new GeospatialImportLimits(), diagnostics);

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
        Assert.IsType<IOException>(diagnostics.Items.Single(item => item.Severity == GeoSeverity.Error).Detail);
    }

    [Fact]
    public void ReturnsReadOnlyFeatureList()
    {
        var (features, _) = ReadKml(Kml(Placemark("<Point><coordinates>1,2</coordinates></Point>")));

        Assert.IsNotType<List<GeospatialFeature>>(features);
    }

    [Fact]
    public void UnknownAltitudeModeWarningDoesNotSuppressMissingCoordinatesError()
    {
        var kml = Kml(Placemark("<Point><altitudeMode>warpDrive</altitudeMode></Point>"));

        var (features, diagnostics) = ReadKml(kml);

        Assert.Empty(features);
        Assert.Equal(2, diagnostics.Items.Count);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
        Assert.Single(diagnostics.Items, item => item.Severity == GeoSeverity.Warning);
    }

    [Fact]
    public void EmptyInnerBoundaryProducesParseError()
    {
        var kml = Kml(Placemark(
            """
            <Polygon>
              <outerBoundaryIs><LinearRing><coordinates>0,0 0,4 4,4 4,0 0,0</coordinates></LinearRing></outerBoundaryIs>
              <innerBoundaryIs/>
            </Polygon>
            """));

        var (features, diagnostics) = ReadKml(kml);

        var polygon = Assert.IsType<GeoPolygon>(Assert.Single(features).Geometry);
        Assert.Empty(polygon.InnerRings);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
    }

    [Fact]
    public void DuplicateFeatureIdsProduceParseError()
    {
        var kml = Kml(
            "<Placemark id=\"dup\"><Point><coordinates>1,1</coordinates></Point></Placemark>"
            + "<Placemark id=\"dup\"><Point><coordinates>2,2</coordinates></Point></Placemark>"
            + "<Placemark id=\"same#4\"><Point><coordinates>3,3</coordinates></Point></Placemark>"
            + "<Placemark><name>same</name><Point><coordinates>4,4</coordinates></Point></Placemark>");

        var (features, diagnostics) = ReadKml(kml);

        Assert.Equal(["dup", "same#4"], features.Select(feature => feature.FeatureId));
        var errors = diagnostics.Items.Where(item => item.Severity == GeoSeverity.Error).ToList();
        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(GeoDiagnosticCodes.ParseFailure, error.Code));
    }

    [Fact]
    public void StreamIsLeftOpenAfterReading()
    {
        var bytes = Encoding.UTF8.GetBytes(Kml(Placemark("<Point><coordinates>1,2</coordinates></Point>")));
        var stream = new MemoryStream(bytes);
        var diagnostics = new GeoDiagnosticBag();
        var features = new KmlReader().Read(stream, "kml", new GeospatialImportLimits(), diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(features);

        stream.Seek(0, SeekOrigin.Begin);
        var firstByte = new byte[1];
        Assert.Equal(1, stream.Read(firstByte, 0, 1));
        Assert.Equal((byte)'<', firstByte[0]);
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] bytes)
            : base(bytes)
        {
        }

        public override bool CanSeek => false;
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("read failure");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
