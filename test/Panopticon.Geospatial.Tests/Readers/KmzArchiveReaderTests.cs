using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Readers;
using Xunit;

namespace Panopticon.Geospatial.Tests.Readers;

public class KmzArchiveReaderTests
{
    private const string Ns = "http://www.opengis.net/kml/2.2";

    public static TheoryData<string> UnsafeReferenceFragments => new()
    {
        "<GroundOverlay><Icon><href>http://example.com/tile.png</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>https://example.com/tile.png</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>ftp://example.com/tile.png</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>file:///etc/passwd</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>//example.com/tile.png</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>/rooted/tile.png</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>../outside/tile.png</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>..\\outside\\tile.png</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>%2e%2e%2foutside/tile.png</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>%2Frooted/tile.png</href></Icon></GroundOverlay>",
        "<GroundOverlay><Icon><href>tiles/%2e%2e/tile.png</href></Icon></GroundOverlay>",
        "<Placemark><Model><Link><href>https://example.com/model.dae</href></Link></Model></Placemark>",
        "<Placemark><styleUrl>http://example.com/styles.kml#bridge</styleUrl><Point><coordinates>1,2</coordinates></Point></Placemark>",
        "<Placemark><styleUrl>%68ttp%3A%2F%2Fexample.com%2Fs.kml%23bridge</styleUrl><Point><coordinates>1,2</coordinates></Point></Placemark>",
        "<NetworkLinkControl><Update><targetHref>http://example.com/update.kml</targetHref></Update></NetworkLinkControl>",
    };

    private static string Kml(string documentContent)
    {
        return $"""
            <kml xmlns="{Ns}"><Document>{documentContent}</Document></kml>
            """;
    }

    private static string Point(string name)
    {
        return $"<Placemark><name>{name}</name><Point><coordinates>1,2</coordinates></Point></Placemark>";
    }

    private static byte[] CreateKmz(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);

                if (name.EndsWith('/'))
                {
                    continue;
                }

                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static (IReadOnlyList<GeospatialFeature> Features, GeoDiagnosticBag Diagnostics) ReadKmz(
        byte[] kmz,
        string sourceId = "kmz",
        GeospatialImportLimits limits = null,
        string sourcePath = null)
    {
        using var stream = new MemoryStream(kmz);
        var diagnostics = new GeoDiagnosticBag();
        var features = new KmzArchiveReader().Read(stream, sourceId, limits ?? new GeospatialImportLimits(), diagnostics, sourcePath);

        return (features, diagnostics);
    }

    private static GeoDiagnostic AssertSingleError(GeoDiagnosticBag diagnostics, string code, GeoStage stage)
    {
        var error = Assert.Single(diagnostics.Items, item => item.Severity == GeoSeverity.Error);

        Assert.Equal(code, error.Code);
        Assert.Equal(stage, error.Stage);

        return error;
    }

    private static void AssertEqualGeometry(GeoGeometry expected, GeoGeometry actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());

        switch (expected)
        {
            case GeoPointGeometry point:
                Assert.Equal(point.Position, Assert.IsType<GeoPointGeometry>(actual).Position);
                break;
            case GeoLineString line:
                Assert.Equal(line.Positions, Assert.IsType<GeoLineString>(actual).Positions);
                break;
            case GeoPolygon polygon:
                var otherPolygon = Assert.IsType<GeoPolygon>(actual);
                Assert.Equal(polygon.OuterRing.Positions, otherPolygon.OuterRing.Positions);
                Assert.Equal(polygon.InnerRings.Count, otherPolygon.InnerRings.Count);

                for (var i = 0; i < polygon.InnerRings.Count; i++)
                {
                    Assert.Equal(polygon.InnerRings[i].Positions, otherPolygon.InnerRings[i].Positions);
                }

                break;
            case GeoMultiGeometry multi:
                var otherMulti = Assert.IsType<GeoMultiGeometry>(actual);
                Assert.Equal(multi.Geometries.Count, otherMulti.Geometries.Count);

                for (var i = 0; i < multi.Geometries.Count; i++)
                {
                    AssertEqualGeometry(multi.Geometries[i], otherMulti.Geometries[i]);
                }

                break;
            default:
                Assert.Fail($"Unexpected geometry type '{expected.GetType().Name}'.");
                break;
        }
    }

    [Fact]
    public void KmzWrappingKmlYieldsSemanticallyIdenticalFeatures()
    {
        var kml = Kml(
            """
            <Style id="flag"><IconStyle><Icon><href>icons/flag.png</href></Icon></IconStyle></Style>
            <Folder><name>Front</name>
              <Placemark><name>Bridge</name><styleUrl>#flag</styleUrl>
                <ExtendedData><Data name="kind"><value>bridge</value></Data></ExtendedData>
                <Point><altitudeMode>relativeToGround</altitudeMode><coordinates>2.25,48.5,120.5</coordinates></Point>
              </Placemark>
              <Placemark><name>Road</name><LineString><coordinates>2,48 3,49 4,50</coordinates></LineString></Placemark>
              <Placemark><name>Area</name><Polygon>
                <outerBoundaryIs><LinearRing><coordinates>0,0 0,4 4,4 4,0 0,0</coordinates></LinearRing></outerBoundaryIs>
                <innerBoundaryIs><LinearRing><coordinates>1,1 1,2 2,2 2,1 1,1</coordinates></LinearRing></innerBoundaryIs>
              </Polygon></Placemark>
              <Placemark><name>Set</name><MultiGeometry>
                <Point><coordinates>5,5</coordinates></Point>
                <LineString><coordinates>5,5 6,6</coordinates></LineString>
              </MultiGeometry></Placemark>
            </Folder>
            """);
        var plainDiagnostics = new GeoDiagnosticBag();
        using var plainStream = new MemoryStream(Encoding.UTF8.GetBytes(kml));
        var plainFeatures = new KmlReader().Read(plainStream, "kmz", new GeospatialImportLimits(), plainDiagnostics);
        var (wrappedFeatures, wrappedDiagnostics) = ReadKmz(
            CreateKmz(("doc.kml", kml), ("icons/flag.png", "png")),
            sourceId: "kmz");

        Assert.False(plainDiagnostics.HasErrors);
        Assert.False(wrappedDiagnostics.HasErrors);
        Assert.Equal(plainFeatures.Count, wrappedFeatures.Count);

        for (var i = 0; i < plainFeatures.Count; i++)
        {
            var plain = plainFeatures[i];
            var wrapped = wrappedFeatures[i];

            Assert.Equal(plain.SourceId, wrapped.SourceId);
            Assert.Equal(plain.FeatureId, wrapped.FeatureId);
            Assert.Equal(plain.CoordinateSystem, wrapped.CoordinateSystem);
            Assert.Equal(plain.AltitudeMode, wrapped.AltitudeMode);
            AssertEqualGeometry(plain.Geometry, wrapped.Geometry);
            Assert.Equal(plain.Attributes.Count + 1, wrapped.Attributes.Count);
            Assert.Equal("doc.kml", wrapped.Attributes["archiveEntry"]);

            foreach (var pair in plain.Attributes)
            {
                Assert.Equal(pair.Value, wrapped.Attributes[pair.Key]);
            }
        }
    }

    [Fact]
    public void RecordsArchiveEntryProvenanceOnFeatures()
    {
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", Kml(Point("kept")))));

        Assert.False(diagnostics.HasErrors);
        Assert.All(features, feature => Assert.Equal("doc.kml", feature.Attributes["archiveEntry"]));
    }

    [Fact]
    public void PrefersRootDocKmlOverEarlierKmlInArchiveOrder()
    {
        var (features, diagnostics) = ReadKmz(CreateKmz(
            ("first.kml", Kml(Point("first"))),
            ("doc.kml", Kml(Point("root")))));

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("root", Assert.Single(features).Attributes["name"]);
    }

    [Fact]
    public void RootDocKmlIsMatchedCaseInsensitively()
    {
        var (features, diagnostics) = ReadKmz(CreateKmz(
            ("first.kml", Kml(Point("first"))),
            ("DOC.KML", Kml(Point("root")))));

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("root", Assert.Single(features).Attributes["name"]);
    }

    [Fact]
    public void FallsBackToFirstKmlInArchiveOrderWithoutDocKml()
    {
        var (features, diagnostics) = ReadKmz(CreateKmz(
            ("b/alpha.kml", Kml(Point("alpha"))),
            ("a/beta.kml", Kml(Point("beta")))));

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("alpha", Assert.Single(features).Attributes["name"]);
    }

    [Fact]
    public void SubdirectoryDocKmlIsNotSelectedAsRootDocument()
    {
        var (features, diagnostics) = ReadKmz(CreateKmz(
            ("nested/doc.kml", Kml(Point("nested"))),
            ("other.kml", Kml(Point("other")))));

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("nested", Assert.Single(features).Attributes["name"]);
    }

    [Fact]
    public void DirectoryEntriesDoNotParticipateInKmlSelection()
    {
        var (features, diagnostics) = ReadKmz(CreateKmz(
            ("assets.kml/", ""),
            ("doc.kml", Kml(Point("kept"))),
            ("icons/", ""),
            ("icons/flag.png", "png")));

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("kept", Assert.Single(features).Attributes["name"]);
    }

    [Fact]
    public void EmptyArchiveIsArchiveFailure()
    {
        var (features, diagnostics) = ReadKmz(CreateKmz());

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive);
        Assert.Contains("no KML entry", error.Message);
    }

    [Fact]
    public void ArchiveWithoutKmlEntryIsArchiveFailure()
    {
        var (features, diagnostics) = ReadKmz(CreateKmz(("readme.txt", "no geometry here")));

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive);
    }

    [Theory]
    [InlineData("../evil.kml")]
    [InlineData("/etc/passwd")]
    [InlineData("dir\\evil.kml")]
    [InlineData("C:\\evil.kml")]
    [InlineData("\\\\server\\share\\evil.kml")]
    [InlineData("a/../../evil.kml")]
    public void RejectsUnsafeEntryNames(string entryName)
    {
        var (features, diagnostics) = ReadKmz(CreateKmz((entryName, Kml(Point("kept")))));

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeEntryName, GeoStage.Archive);
    }

    [Fact]
    public void RejectsEntryNameThatNormalizesToEmpty()
    {
        var dotFile = ReadKmz(CreateKmz((".", Kml(Point("kept")))));
        var dotDirectory = ReadKmz(CreateKmz(("./", ""), ("doc.kml", Kml(Point("kept")))));

        Assert.Empty(dotFile.Features);
        AssertSingleError(dotFile.Diagnostics, GeoDiagnosticCodes.ArchiveUnsafeEntryName, GeoStage.Archive);
        Assert.Empty(dotDirectory.Features);
        AssertSingleError(dotDirectory.Diagnostics, GeoDiagnosticCodes.ArchiveUnsafeEntryName, GeoStage.Archive);
    }

    [Fact]
    public void RejectsDuplicateEntryNamesAfterNormalization()
    {
        var (features, diagnostics) = ReadKmz(CreateKmz(
            ("a/b.kml", Kml(Point("first"))),
            ("a//./b.kml", Kml(Point("second")))));

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeEntryName, GeoStage.Archive);
        Assert.Contains("after path normalization", error.Message);
    }

    [Fact]
    public void RejectsEntryCountOverLimit()
    {
        var kmz = CreateKmz(("doc.kml", Kml(Point("kept"))), ("icon.png", "png"));
        var (features, diagnostics) = ReadKmz(kmz, limits: new GeospatialImportLimits(MaxKmzEntryCount: 1));

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveLimitExceeded, GeoStage.Archive);
        Assert.Contains("entry count", error.Message);
    }

    [Fact]
    public void RejectsPerEntryExpandedSizeOverLimit()
    {
        var kmz = CreateKmz(("doc.kml", new string('a', 200)));
        var (features, diagnostics) = ReadKmz(kmz, limits: new GeospatialImportLimits(MaxKmzExpandedBytes: 100));

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveLimitExceeded, GeoStage.Archive);
    }

    [Fact]
    public void RejectsTotalExpandedSizeOverLimit()
    {
        var content = new string('a', 200);
        var kmz = CreateKmz(("doc.kml", content), ("second.kml", content));
        var (features, diagnostics) = ReadKmz(kmz, limits: new GeospatialImportLimits(MaxKmzExpandedBytes: 300));

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveLimitExceeded, GeoStage.Archive);
        Assert.Contains("in total", error.Message);
    }

    [Fact]
    public void RejectsZipBombRatioBeforeExpansion()
    {
        var kmz = CreateKmz(("doc.kml", new string('a', 1_000_000)));
        var (features, diagnostics) = ReadKmz(kmz);

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveLimitExceeded, GeoStage.Archive);
        Assert.Contains("compression ratio", error.Message);
    }

    [Fact]
    public void RejectsKmlEntryOverKmlEntryLimit()
    {
        var kmz = CreateKmz(("doc.kml", new string('a', 50)));
        var (features, diagnostics) = ReadKmz(kmz, limits: new GeospatialImportLimits(MaxKmlEntryBytes: 10));

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveLimitExceeded, GeoStage.Archive);
        Assert.Contains("KML entry", error.Message);
    }

    [Fact]
    public void RejectsZeroCompressedLengthMetadata()
    {
        var kmz = CreateKmz(("doc.kml", new string('a', 200)));
        var patched = PatchCentralDirectoryInt32(kmz, offsetFromHeader: 20, value: 0);
        var (features, diagnostics) = ReadKmz(patched);

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveLimitExceeded, GeoStage.Archive);
        Assert.Contains("compressed", error.Message);
    }

    [Fact]
    public void FakedEntryMetadataFailsClosed()
    {
        // The zip decoder fences reads at the declared size, and the reader counts actual bytes,
        // so an entry understating its size cannot smuggle its real payload: the truncated
        // content reaches KmlReader only as far as it parses, and the import fails.
        var kmz = CreateKmz(("doc.kml", Kml(Point("kept")) + new string(' ', 200)));
        var patched = PatchCentralDirectoryInt32(kmz, offsetFromHeader: 24, value: 10);
        var (features, diagnostics) = ReadKmz(patched);

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
    }

    [Fact]
    public void AdvertisedEntryCountBelowActualFailsBeforeArchiveMaterialization()
    {
        var kmz = CreateKmz(("doc.kml", Kml(Point("kept"))), ("icon.png", "png"));
        var patched = PatchEndOfCentralDirectoryInt16(kmz, offsetFromRecord: 8, value: 1);
        patched = PatchEndOfCentralDirectoryInt16(patched, offsetFromRecord: 10, value: 1);
        var (features, diagnostics) = ReadKmz(patched, limits: new GeospatialImportLimits(MaxKmzEntryCount: 1));

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive);
        Assert.Contains("central directory", error.Message);
    }

    [Fact]
    public void RejectsMultiDiskArchive()
    {
        var kmz = CreateKmz(("doc.kml", Kml(Point("kept"))));
        var patched = PatchEndOfCentralDirectoryInt16(kmz, offsetFromRecord: 4, value: 1);
        var (features, diagnostics) = ReadKmz(patched);

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive);
        Assert.Contains("multiple disks", error.Message);
    }

    [Fact]
    public void RejectsZip64Archive()
    {
        var kmz = CreateKmz(("doc.kml", Kml(Point("kept"))));
        var patched = PatchEndOfCentralDirectoryInt16(kmz, offsetFromRecord: 10, value: unchecked((short)0xFFFF));
        var (features, diagnostics) = ReadKmz(patched);

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive);
        Assert.Contains("ZIP64", error.Message);
    }

    [Fact]
    public void TruncatedArchiveIsTypedArchiveFailure()
    {
        var kmz = CreateKmz(("doc.kml", Kml(Point("kept"))));
        var (features, diagnostics) = ReadKmz(kmz[..^10]);

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive);
        Assert.Contains("end-of-central-directory", error.Message);
    }

    [Fact]
    public void RejectsNonSeekableStreamBeforeOpeningArchive()
    {
        using var stream = new NonSeekableStream(CreateKmz(("doc.kml", Kml(Point("kept")))));
        var diagnostics = new GeoDiagnosticBag();
        var features = new KmzArchiveReader().Read(stream, "kmz", new GeospatialImportLimits(), diagnostics);

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive);
        Assert.Contains("seekable", error.Message);
    }

    [Fact]
    public void StreamIsLeftOpenAfterReading()
    {
        var stream = new MemoryStream(CreateKmz(("doc.kml", Kml(Point("kept")))));
        var diagnostics = new GeoDiagnosticBag();
        var features = new KmzArchiveReader().Read(stream, "kmz", new GeospatialImportLimits(), diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(features);

        stream.Seek(0, SeekOrigin.Begin);
        var firstByte = new byte[1];
        Assert.Equal(1, stream.Read(firstByte, 0, 1));
        Assert.Equal((byte)'P', firstByte[0]);
    }

    [Fact]
    public void RejectsNetworkLinkInSelectedKml()
    {
        var kml = Kml(Point("kept") + "<NetworkLink><Link><href>linked.kml</href></Link></NetworkLink>");
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", kml), ("linked.kml", Kml(Point("linked")))));

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeReference, GeoStage.Archive);
        Assert.Contains("NetworkLink", error.Message);
    }

    [Theory]
    [MemberData(nameof(UnsafeReferenceFragments))]
    public void RejectsRemoteOrEscapingReferencesInSelectedKml(string fragment)
    {
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", Kml(Point("kept") + fragment))));

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeReference, GeoStage.Archive);
    }

    [Fact]
    public void RejectsNetworkLinkImmediatelyAfterLocalHref()
    {
        var kml = Kml(Point("kept") + "<GroundOverlay><Icon><href>local.png</href><NetworkLink/></Icon></GroundOverlay>");
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", kml)));

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeReference, GeoStage.Archive);
        Assert.Contains("NetworkLink", error.Message);
    }

    [Fact]
    public void RejectsRemoteHrefImmediatelyAfterLocalHref()
    {
        var kml = Kml("<GroundOverlay><Icon><href>local.png</href><href>http://example.com/x.png</href></Icon></GroundOverlay>");
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", kml)));

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeReference, GeoStage.Archive);
    }

    [Fact]
    public void RejectsRemoteStyleUrlImmediatelyAfterLocalStyleUrl()
    {
        var kml = Kml(
            "<Placemark><styleUrl>#local</styleUrl><styleUrl>http://example.com/s.kml#bridge</styleUrl>"
            + "<Point><coordinates>1,2</coordinates></Point></Placemark>");
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", kml)));

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeReference, GeoStage.Archive);
    }

    [Fact]
    public void RejectsNullCharacterInReference()
    {
        var kml = Kml("<GroundOverlay><Icon><href>bad\0.png</href></Icon></GroundOverlay>");
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", kml)));

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeReference, GeoStage.Archive);
    }

    [Fact]
    public void RejectsNetworkLinkInUnselectedKml()
    {
        var kmz = CreateKmz(
            ("doc.kml", Kml(Point("kept"))),
            ("old/other.kml", Kml("<NetworkLink><Link><href>http://example.com/x.kml</href></Link></NetworkLink>")));
        var (features, diagnostics) = ReadKmz(kmz);

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeReference, GeoStage.Archive);
        Assert.Contains("other.kml", error.Message);
    }

    [Fact]
    public void RejectsRemoteReferenceInUnselectedKml()
    {
        var kmz = CreateKmz(
            ("doc.kml", Kml(Point("kept"))),
            ("linked.kml", Kml("<GroundOverlay><Icon><href>ftp://example.com/x.png</href></Icon></GroundOverlay>")));
        var (features, diagnostics) = ReadKmz(kmz);

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeReference, GeoStage.Archive);
        Assert.Contains("linked.kml", error.Message);
    }

    [Fact]
    public void UnselectedMalformedKmlFailsClosed()
    {
        var malformed = $"<kml xmlns=\"{Ns}\"><Document><Placemark><name>broken";
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", Kml(Point("kept"))), ("old/other.kml", malformed)));

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive);
        Assert.Contains("other.kml", error.Message);
        Assert.IsType<System.Xml.XmlException>(error.Detail);
    }

    [Fact]
    public void UnreadableReferenceMarkupIsRejected()
    {
        var kml = Kml("<GroundOverlay><Icon><href><nested>http://example.com/x.png</nested></href></Icon></GroundOverlay>");
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", kml)));

        Assert.Empty(features);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveUnsafeReference, GeoStage.Archive);
    }

    [Fact]
    public void AcceptsArchiveLocalReferencesWithoutLoadingResources()
    {
        var kml = Kml(
            """
            <Style id="flag"><IconStyle><Icon><href>icons/flag.png</href></Icon></IconStyle></Style>
            <Placemark><name>kept</name><styleUrl>#flag</styleUrl><Point><coordinates>1,2</coordinates></Point></Placemark>
            """);
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", kml), ("icons/", ""), ("icons/flag.png", "png")));

        Assert.False(diagnostics.HasErrors);
        var feature = Assert.Single(features);
        Assert.Equal("#flag", feature.Attributes["styleUrl"]);
        Assert.Equal("doc.kml", feature.Attributes["archiveEntry"]);
    }

    [Fact]
    public void SelectedKmlParseErrorsKeepParseStageAndQualifiedSourcePath()
    {
        var malformed = $"<kml xmlns=\"{Ns}\"><Document><Placemark><name>broken";
        var (features, diagnostics) = ReadKmz(CreateKmz(("doc.kml", malformed)), sourcePath: "map.kmz");

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
        Assert.Equal("map.kmz!doc.kml", error.SourcePath);
    }

    [Fact]
    public void GarbageInputIsArchiveFailureWithRetainedDetail()
    {
        var (features, diagnostics) = ReadKmz(Encoding.UTF8.GetBytes("this is not a zip file"));

        Assert.Empty(features);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive);
        Assert.NotNull(error.Detail);
    }

    [Fact]
    public void PathOverloadUsesFileNameAsSourceIdAndMatchesStreamOverload()
    {
        var kml = Kml(
            Point("first")
            + "<Placemark><name>Road</name><LineString><coordinates>2,48 3,49</coordinates></LineString></Placemark>");
        var bytes = CreateKmz(("doc.kml", kml));
        var path = Path.Combine(Path.GetTempPath(), "panopticon-kmz-" + Guid.NewGuid().ToString("N") + ".kmz");
        File.WriteAllBytes(path, bytes);

        try
        {
            var pathDiagnostics = new GeoDiagnosticBag();
            var pathFeatures = new KmzArchiveReader().Read(path, new GeospatialImportLimits(), pathDiagnostics);
            var (streamFeatures, streamDiagnostics) = ReadKmz(bytes);

            Assert.False(pathDiagnostics.HasErrors);
            Assert.False(streamDiagnostics.HasErrors);
            Assert.Equal(2, pathFeatures.Count);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), pathFeatures[0].SourceId);
            Assert.Equal(streamFeatures.Select(feature => feature.FeatureId), pathFeatures.Select(feature => feature.FeatureId));

            for (var i = 0; i < pathFeatures.Count; i++)
            {
                AssertEqualGeometry(streamFeatures[i].Geometry, pathFeatures[i].Geometry);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] PatchCentralDirectoryInt32(byte[] kmz, int offsetFromHeader, int value)
    {
        var centralHeaderIndex = LastIndexOf(kmz, [0x50, 0x4B, 0x01, 0x02]);
        Assert.True(centralHeaderIndex >= 0, "the synthetic KMZ must contain a central directory header");
        var patched = (byte[])kmz.Clone();
        BitConverter.GetBytes(value).CopyTo(patched, centralHeaderIndex + offsetFromHeader);

        return patched;
    }

    private static byte[] PatchEndOfCentralDirectoryInt16(byte[] kmz, int offsetFromRecord, short value)
    {
        var endRecordIndex = LastIndexOf(kmz, [0x50, 0x4B, 0x05, 0x06]);
        Assert.True(endRecordIndex >= 0, "the synthetic KMZ must contain an end-of-central-directory record");
        var patched = (byte[])kmz.Clone();
        BitConverter.GetBytes(value).CopyTo(patched, endRecordIndex + offsetFromRecord);

        return patched;
    }

    private static int LastIndexOf(byte[] haystack, byte[] pattern)
    {
        for (var i = haystack.Length - pattern.Length; i >= 0; i--)
        {
            var matches = true;

            for (var k = 0; k < pattern.Length; k++)
            {
                if (haystack[i + k] != pattern[k])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] bytes)
            : base(bytes)
        {
        }

        public override bool CanSeek => false;
    }
}
