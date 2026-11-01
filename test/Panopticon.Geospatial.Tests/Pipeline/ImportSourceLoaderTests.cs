using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Pipeline.SourceLoading;
using Panopticon.Geospatial.Tests.Fixtures;
using Xunit;

namespace Panopticon.Geospatial.Tests.Pipeline;

public class ImportSourceLoaderTests
{
    private const string KmlNs = "http://www.opengis.net/kml/2.2";

    private const string ExtentRing = "5.0,49.0 5.2,49.0 5.2,49.2 5.0,49.2 5.0,49.0";

    private const string OtherRing = "6.0,50.0 6.2,50.0 6.2,50.2 6.0,50.2 6.0,50.0";

    [Fact]
    public void EndToEnd_PreparesSourcesInManifestOrder()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        project.WriteDefaultVectorKmz();
        var dem = project.WriteDem();
        var cover = project.WriteClassification();
        project.WriteManifest(DefaultSources(dem.RelativePath, cover.RelativePath));

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(prepared);
        Assert.Equal(["extent", "dem", "vectors", "cover"], prepared!.Sources.Select(static source => source.SourceId));
        Assert.Equal(
            [ManifestSourceKind.Kml, ManifestSourceKind.Geotiff, ManifestSourceKind.Kmz, ManifestSourceKind.Geotiff],
            prepared.Sources.Select(static source => source.Kind));
        Assert.Equal(
            [ManifestSourceRole.Extent, ManifestSourceRole.Elevation, ManifestSourceRole.Vector, ManifestSourceRole.Classification],
            prepared.Sources.Select(static source => source.Role));

        var demProvenance = prepared.Sources[1];
        Assert.Equal("unknown", demProvenance.DeclaredVerticalDatum);
        Assert.Equal("EGM96", demProvenance.EmbeddedVerticalDatum);
        Assert.Equal("EGM96", demProvenance.EffectiveVerticalDatum);
        Assert.Equal(dem.RelativePath, demProvenance.RelativePath);
        Assert.Equal(dem.FullPath, demProvenance.FullPath);
        Assert.Equal(Sha256Of(dem.FullPath), demProvenance.Sha256Hex);
        Assert.Equal(new FileInfo(dem.FullPath).Length, demProvenance.LengthBytes);
        Assert.Equal("DEM", demProvenance.Label);
        Assert.Equal("operator survey", demProvenance.Attribution);
        Assert.Equal(0.6, demProvenance.HorizontalAccuracyMeters);
        Assert.Equal(1.5, demProvenance.VerticalAccuracyMeters);
        Assert.Equal(CoordinateSystemId.Wgs84, demProvenance.ActualCoordinateSystem);

        Assert.Equal("EGM96", prepared.EffectiveDemDatum);
        Assert.Equal(10d, prepared.PrimaryElevation.Raster.Get(0, 0, 0));
        Assert.Equal(1, prepared.PrimaryElevation.Raster.BandCount);
        Assert.Null(prepared.Imagery);
        Assert.NotNull(prepared.Classification);
        Assert.Equal(RasterSampleFormat.UInt8, prepared.Classification!.Raster.SampleFormat);

        Assert.Equal("playable", prepared.SelectedExtent.Feature.FeatureId);
        Assert.Equal("extent", prepared.SelectedExtent.Feature.SourceId);
        Assert.Equal(5, prepared.SelectedExtent.Polygon.OuterRing.Positions.Count);

        Assert.Equal(Sha256Of(project.ManifestPath), prepared.ManifestSha256);
        Assert.Equal(
            [project.ManifestPath, project.FullPath("area.kml"), dem.FullPath, project.FullPath("features.kmz"), cover.FullPath],
            prepared.ProtectedInputPaths);
    }

    [Fact]
    public void VectorFeatures_AreReboundAndKeepProvenance()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        project.WriteDefaultVectorKmz();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath));

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(prepared);
        Assert.Equal(
            ["extent", "extent", "vectors", "vectors"],
            prepared!.VectorFeatures.Select(static feature => feature.SourceId));
        Assert.Equal(
            ["playable", "peak", "river", "forest"],
            prepared.VectorFeatures.Select(static feature => feature.FeatureId));

        var river = prepared.VectorFeatures[2];
        Assert.Equal("water", river.Attributes["cover"]);
        Assert.Equal("doc.kml", river.Attributes["archiveEntry"]);

        var peak = Assert.IsType<GeoPointGeometry>(prepared.VectorFeatures[1].Geometry);
        Assert.Equal(120d, peak.Position.AltitudeMeters);
        Assert.Equal(CoordinateSystemId.Wgs84, prepared.VectorFeatures[0].CoordinateSystem);
    }

    [Fact]
    public void ExtentFeatureId_SelectsSinglePolygonInsideMultiGeometry()
    {
        using var project = new ImportProject();
        project.WriteExtentKml($"""
            <Placemark id="zone"><name>zone</name><MultiGeometry>
              <Polygon><outerBoundaryIs><LinearRing><coordinates>{ExtentRing}</coordinates></LinearRing></outerBoundaryIs></Polygon>
              <Point><coordinates>5.1,49.1</coordinates></Point>
            </MultiGeometry></Placemark>
            <Placemark id="other"><name>other</name><Polygon><outerBoundaryIs><LinearRing><coordinates>{OtherRing}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>
            """);
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null), ",\n  \"extentFeatureId\": \"zone\"");

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(prepared);
        Assert.Equal("zone", prepared!.SelectedExtent.Feature.FeatureId);
        Assert.Equal(5d, prepared.SelectedExtent.Polygon.OuterRing.Positions[0].Coordinate.X);
        Assert.Equal(49d, prepared.SelectedExtent.Polygon.OuterRing.Positions[0].Coordinate.Y);
    }

    [Fact]
    public void Extent_WithoutFilter_RequiresExactlyOnePolygon()
    {
        using var project = new ImportProject();
        project.WriteExtentKml($"""
            <Placemark id="a"><name>a</name><Polygon><outerBoundaryIs><LinearRing><coordinates>{ExtentRing}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>
            <Placemark id="b"><name>b</name><Polygon><outerBoundaryIs><LinearRing><coordinates>{OtherRing}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>
            """);
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.CoordinatesInvalidExtent);
        Assert.Equal(GeoStage.Coordinates, diagnostic.Stage);
        Assert.Contains("at least 2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtentFeatureId_WithTwoPolygonsInFeature_Fails()
    {
        using var project = new ImportProject();
        project.WriteExtentKml($"""
            <Placemark id="zone"><MultiGeometry>
              <Polygon><outerBoundaryIs><LinearRing><coordinates>{ExtentRing}</coordinates></LinearRing></outerBoundaryIs></Polygon>
              <Polygon><outerBoundaryIs><LinearRing><coordinates>{OtherRing}</coordinates></LinearRing></outerBoundaryIs></Polygon>
            </MultiGeometry></Placemark>
            """);
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null), ",\n  \"extentFeatureId\": \"zone\"");

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.CoordinatesInvalidExtent);
        Assert.Contains("exactly one polygon", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtentFeatureId_MatchingNoFeature_Fails()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null), ",\n  \"extentFeatureId\": \"ghost\"");

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.CoordinatesInvalidExtent);
    }

    [Fact]
    public void ExtentSource_WithoutAnyPolygon_Fails()
    {
        using var project = new ImportProject();
        project.WriteExtentKml("<Placemark id=\"peak\"><name>peak</name><Point><coordinates>5.1,49.1</coordinates></Point></Placemark>");
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.CoordinatesInvalidExtent);
        Assert.Contains("found 0", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceChangedWhileRead_FailsWithTypedError()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));

        var diagnostics = new GeoDiagnosticBag();

        var prepared = ImportSourceLoader.Load(
            project.ManifestPath,
            diagnostics,
            afterSourceRead: (source, fullPath) =>
            {
                if (source.Id == "dem")
                {
                    File.AppendAllText(fullPath, "mutated");
                }
            });

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
        Assert.Equal(GeoStage.Parse, diagnostic.Stage);
        Assert.Contains("changed while it was being read", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestChangedWhileLoaded_FailsWithTypedError()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));

        var diagnostics = new GeoDiagnosticBag();

        var prepared = ImportSourceLoader.Load(
            project.ManifestPath,
            diagnostics,
            afterManifestHash: manifestPath => File.WriteAllText(
                manifestPath,
                File.ReadAllText(manifestPath).Replace("\"grass\"", "\"stone\"")));

        Assert.Null(prepared);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestFailure);
    }

    [Fact]
    public void MissingSourceFile_FailsWithTypedError()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        project.WriteDefaultVectorKmz();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath));
        File.Delete(project.FullPath("features.kmz"));

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveFailure);
        Assert.Equal(GeoStage.Archive, diagnostic.Stage);
        Assert.Equal("features.kmz", diagnostic.SourcePath);
    }

    [Fact]
    public void DanglingSymlinkSource_FailsWithTypedError()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        File.CreateSymbolicLink(project.FullPath("linked.tif"), project.FullPath("outside.tif"));
        project.WriteRawManifest($$"""
            {
              "sources": [
                { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
                { "id": "dem", "kind": "geotiff", "path": "linked.tif", "role": "elevation" }
              ],
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "metersPerGameUnit": 1.25,
              "defaultMaterial": "grass",
              "tolerances": { "relativeVerticalMeters": 2 }
            }
            """);

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure);
        Assert.Equal(GeoStage.Parse, diagnostic.Stage);
        Assert.Equal("linked.tif", diagnostic.SourcePath);
    }

    [Fact]
    public void SymlinkSwappedToEscape_BeforeUse_FailsWithPathRejection()
    {
        using var project = new ImportProject();
        using var outside = new TempFile();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));

        var diagnostics = new GeoDiagnosticBag();

        var prepared = ImportSourceLoader.Load(
            project.ManifestPath,
            diagnostics,
            afterSourceRead: (source, _) =>
            {
                if (source.Id == "extent")
                {
                    File.Delete(dem.FullPath);
                    File.CreateSymbolicLink(dem.FullPath, outside.Path);
                }
            });

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSourcePathRejected);
        Assert.Equal(GeoStage.Manifest, diagnostic.Stage);
    }

    [Fact]
    public void DeclaredDatumContradictingEmbeddedDatum_Fails()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null, demDatum: "EGM2008"));

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.CoordinatesFailure);
        Assert.Equal(GeoStage.Coordinates, diagnostic.Stage);
        Assert.Contains("EGM2008", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("EGM96", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SourcesDeclaringDifferentKnownDatums_Fail()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        project.WriteDefaultVectorKmz();
        var dem = project.WriteDem();
        var cover = project.WriteClassification();
        project.WriteManifest(DefaultSources(dem.RelativePath, cover.RelativePath, demDatum: "EGM96", coverDatum: "EGM2008"));

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.CoordinatesFailure);
        Assert.Contains("mixes known vertical datums", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SourcesDeclaringTheSameKnownDatum_Load()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        project.WriteDefaultVectorKmz();
        var dem = project.WriteDem();
        var cover = project.WriteClassification();
        project.WriteManifest(DefaultSources(dem.RelativePath, cover.RelativePath, demDatum: "EGM96", coverDatum: "EGM96"));

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(prepared);
        Assert.Equal("EGM96", prepared!.EffectiveDemDatum);
    }

    [Fact]
    public void ReaderLimits_AreSuppliedToReaders()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        project.WriteDefaultVectorKmz();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath));
        var limits = new GeospatialImportLimits(MaxKmzEntryCount: 0);

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics, limits);

        Assert.Null(prepared);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ArchiveLimitExceeded);
    }

    [Fact]
    public void OversizedManifest_FailsTypedBeforeAnyCallback()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));
        var limits = new GeospatialImportLimits(MaxManifestBytes: new FileInfo(project.ManifestPath).Length - 1);
        var hashCallbackRan = false;

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(
            project.ManifestPath,
            diagnostics,
            limits,
            afterManifestHash: _ => hashCallbackRan = true);

        Assert.Null(prepared);
        Assert.False(hashCallbackRan);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestOversized);
        Assert.Contains("over the limit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedKml_FailsTypedBeforeReaderAndCallback()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));
        var limits = new GeospatialImportLimits { MaxSourceFileBytes = 200 };
        var readCallbackRan = false;

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(
            project.ManifestPath,
            diagnostics,
            limits,
            afterSourceRead: (_, _) => readCallbackRan = true);

        Assert.Null(prepared);
        Assert.False(readCallbackRan);
        var diagnostic = AssertSingleError(diagnostics, "GEO-SRC-001");
        Assert.Equal(GeoStage.Parse, diagnostic.Stage);
        Assert.Contains("source file limit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TinySourceFileCap_FailsRasterBeforeRead()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));
        var limits = new GeospatialImportLimits { MaxSourceFileBytes = new FileInfo(dem.FullPath).Length - 1 };
        var readSources = new List<string>();

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(
            project.ManifestPath,
            diagnostics,
            limits,
            afterSourceRead: (source, _) => readSources.Add(source.Id));

        Assert.Null(prepared);
        Assert.Equal(["extent"], readSources);
        var diagnostic = AssertSingleError(diagnostics, "GEO-SRC-001");
        Assert.Contains("source file limit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AggregateFeatureCap_TwoIndividuallyValidSources_Fail()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        project.WriteKmlPoints("a.kml", 6);
        project.WriteKmlPoints("b.kml", 6);
        var dem = project.WriteDem();
        project.WriteRawManifest($$"""
            {
              "sources": [
                { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
                { "id": "dem", "kind": "geotiff", "path": "{{dem.RelativePath}}", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1 },
                { "id": "va", "kind": "kml", "path": "a.kml", "role": "vector" },
                { "id": "vb", "kind": "kml", "path": "b.kml", "role": "vector" }
              ],
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "metersPerGameUnit": 1.25,
              "defaultMaterial": "grass"
            }
            """);
        var limits = new GeospatialImportLimits(MaxFeatureCount: 10);

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics, limits);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, "GEO-SRC-002");
        Assert.Contains("vector features", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("14", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AggregateCoordinateCap_TwoIndividuallyValidSources_Fail()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        project.WriteKmlLine("a.kml", 12);
        project.WriteKmlLine("b.kml", 12);
        var dem = project.WriteDem();
        project.WriteRawManifest($$"""
            {
              "sources": [
                { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
                { "id": "dem", "kind": "geotiff", "path": "{{dem.RelativePath}}", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1 },
                { "id": "va", "kind": "kml", "path": "a.kml", "role": "vector" },
                { "id": "vb", "kind": "kml", "path": "b.kml", "role": "vector" }
              ],
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "metersPerGameUnit": 1.25,
              "defaultMaterial": "grass"
            }
            """);
        var limits = new GeospatialImportLimits(MaxCoordinateCount: 20);

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics, limits);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, "GEO-SRC-002");
        Assert.Contains("vector coordinates", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidManifestPath_WithNulCharacter_FailsTyped()
    {
        var path = Path.Combine(Path.GetTempPath(), "bad\0manifest.json");
        var diagnostics = new GeoDiagnosticBag();

        var prepared = ImportSourceLoader.Load(path, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestFailure);
        Assert.Contains("invalid", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SymlinkSwappedDuringOwnRead_RejectedBeforeFinalHash()
    {
        using var project = new ImportProject();
        using var outside = new TempFile();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));

        var diagnostics = new GeoDiagnosticBag();

        var prepared = ImportSourceLoader.Load(
            project.ManifestPath,
            diagnostics,
            afterSourceRead: (source, fullPath) =>
            {
                if (source.Id == "dem")
                {
                    // Same bytes outside the project root: only the path recheck can catch it.
                    File.Copy(fullPath, outside.Path, overwrite: true);
                    File.Delete(fullPath);
                    File.CreateSymbolicLink(fullPath, outside.Path);
                }
            });

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSourcePathRejected);
        Assert.Equal(GeoStage.Manifest, diagnostic.Stage);
    }

    [Fact]
    public void AttributeMaterialRule_NamingRasterSource_FailsTyped()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(
            DefaultSources(dem.RelativePath, vectorPath: null),
            ",\n  \"materialRules\": [ { \"sourceId\": \"dem\", \"attribute\": \"cover\", \"value\": \"water\", \"material\": \"water\" } ]");

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("vector", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureMapping_NamingRasterSource_FailsTyped()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(
            DefaultSources(dem.RelativePath, vectorPath: null),
            ",\n  \"featureMappings\": [ { \"sourceId\": \"dem\", \"target\": \"annotation\" } ]");

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.Null(prepared);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("vector", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeMaterialRule_WithoutSourceId_LoadsAsWildcard()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(
            DefaultSources(dem.RelativePath, vectorPath: null),
            ",\n  \"materialRules\": [ { \"attribute\": \"cover\", \"value\": \"water\", \"material\": \"water\" } ]");

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(prepared);
    }

    [Fact]
    public void CancelledBeforeStart_ThrowsOperationCanceled()
    {
        using var project = new ImportProject();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => ImportSourceLoader.Load(project.ManifestPath, new GeoDiagnosticBag(), cancellationToken: cancellation.Token));
    }

    [Fact]
    public void CancelledMidRun_PropagatesOperationCanceled()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteManifest(DefaultSources(dem.RelativePath, vectorPath: null));
        using var cancellation = new CancellationTokenSource();

        Assert.ThrowsAny<OperationCanceledException>(
            () => ImportSourceLoader.Load(
                project.ManifestPath,
                new GeoDiagnosticBag(),
                cancellationToken: cancellation.Token,
                afterSourceRead: (_, _) => cancellation.Cancel()));
    }

    [Fact]
    public void SharedSourcePath_IsProtectedOnceAndFeaturesCarryBothIds()
    {
        using var project = new ImportProject();
        project.WriteDefaultExtentKml();
        var dem = project.WriteDem();
        project.WriteRawManifest($$"""
            {
              "sources": [
                { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
                { "id": "dem", "kind": "geotiff", "path": "{{dem.RelativePath}}", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1 },
                { "id": "extra", "kind": "kml", "path": "area.kml", "role": "vector" }
              ],
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "metersPerGameUnit": 1.25,
              "defaultMaterial": "grass"
            }
            """);

        var diagnostics = new GeoDiagnosticBag();
        var prepared = ImportSourceLoader.Load(project.ManifestPath, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(prepared);
        Assert.Equal(
            [project.ManifestPath, project.FullPath("area.kml"), dem.FullPath],
            prepared!.ProtectedInputPaths);
        Assert.Equal(
            ["extent", "extent", "extra", "extra"],
            prepared.VectorFeatures.Select(static feature => feature.SourceId));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ImportSourceLoader.Load(null!, new GeoDiagnosticBag()));
        Assert.Throws<ArgumentException>(() => ImportSourceLoader.Load(string.Empty, new GeoDiagnosticBag()));
        Assert.Throws<ArgumentNullException>(() => ImportSourceLoader.Load("manifest.json", null!));
    }

    [Fact]
    public void WideMultiGeometry_SmallNodeBudget_FailsTypedBeforeBulkPush()
    {
        var empty = new GeoMultiGeometry(Array.Empty<GeoGeometry>());
        var children = new GeoGeometry[100_000];
        Array.Fill(children, empty);
        var wide = new GeoMultiGeometry(children);
        long coordinates = 0;

        var status = ImportSourceLoader.CountGeometryForTest(
            wide,
            maxCoordinates: long.MaxValue,
            nodeBudget: 2,
            ref coordinates,
            CancellationToken.None);

        Assert.Equal(ImportSourceLoader.GeometryWalkStatus.NodeBudgetExceeded, status);
        Assert.Equal(0, coordinates);
    }

    [Fact]
    public void WideMultiGeometry_ExtentWalk_FailsTypedBeforeBulkPush()
    {
        var empty = new GeoMultiGeometry(Array.Empty<GeoGeometry>());
        var children = new GeoGeometry[100_000];
        Array.Fill(children, empty);
        var wide = new GeoMultiGeometry(children);
        var polygons = new List<GeoPolygon>();

        var status = ImportSourceLoader.CollectPolygonsForTest(
            wide,
            nodeBudget: 2,
            polygons,
            CancellationToken.None);

        Assert.Equal(ImportSourceLoader.GeometryWalkStatus.NodeBudgetExceeded, status);
        Assert.Empty(polygons);
    }

    [Fact]
    public void WideMultiGeometry_PushLoop_IsCancellable()
    {
        var empty = new GeoMultiGeometry(Array.Empty<GeoGeometry>());
        var children = new GeoGeometry[100_000];
        Array.Fill(children, empty);
        var wide = new GeoMultiGeometry(children);
        long coordinates = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ImportSourceLoader.CountGeometryForTest(
            wide,
            maxCoordinates: long.MaxValue,
            nodeBudget: 200_000,
            ref coordinates,
            cancellation.Token));
    }

    [Fact]
    public void NestedMultiGeometry_ReserveAccountsForPendingSiblings_FailsBeforeVisiting()
    {
        var point = new GeoPointGeometry(new GeoPosition(new Vector2d(5d, 49d)));
        var pointChildren = new GeoGeometry[10];
        Array.Fill(pointChildren, point);
        var childMulti = new GeoMultiGeometry(pointChildren);
        var root = new GeoMultiGeometry(new GeoGeometry[] { childMulti, point });
        long coordinates = 0;

        var status = ImportSourceLoader.CountGeometryForTest(
            root,
            maxCoordinates: long.MaxValue,
            nodeBudget: 12,
            ref coordinates,
            CancellationToken.None);

        Assert.Equal(ImportSourceLoader.GeometryWalkStatus.NodeBudgetExceeded, status);
        Assert.Equal(0, coordinates);
    }

    [Fact]
    public void NestedMultiGeometry_ExtentReserveAccountsForPendingSiblings_RejectsBeforeCollecting()
    {
        var polygon = new GeoPolygon(new GeoRing([new GeoPosition(new Vector2d(5d, 49d))]));
        var childMulti = new GeoMultiGeometry(new GeoGeometry[] { polygon, polygon });
        var point = new GeoPointGeometry(new GeoPosition(new Vector2d(5d, 49d)));
        var root = new GeoMultiGeometry(new GeoGeometry[] { childMulti, point });
        var polygons = new List<GeoPolygon>();

        var status = ImportSourceLoader.CollectPolygonsForTest(
            root,
            nodeBudget: 4,
            polygons,
            CancellationToken.None);

        Assert.Equal(ImportSourceLoader.GeometryWalkStatus.NodeBudgetExceeded, status);
        Assert.Empty(polygons);
    }

    [Fact]
    public void PreparedImportSources_CopiesCollectionsAndRejectsBadState()
    {
        var manifest = new GeospatialImportManifest
        {
            Sources = [new ManifestSource("extent", ManifestSourceKind.Kml, "area.kml", ManifestSourceRole.Extent)],
            TargetCoordinateSystem = new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, 31, ManifestHemisphere.North, null),
            MetersPerGameUnit = 1.25,
            DefaultMaterial = "grass",
        };
        var raster = new GeospatialRaster<double>(
            1,
            1,
            1,
            new AffineTransform(2d, 0.25d, 0d, 49d, 0d, -0.125d),
            CoordinateSystemId.Wgs84,
            new ReadOnlyMemory<double>(new[] { 1d }));
        var elevation = new LoadedRaster(
            new LoadedSource(
                "dem",
                ManifestSourceKind.Geotiff,
                ManifestSourceRole.Elevation,
                "dem.tif",
                "/tmp/dem.tif",
                new string('a', 64),
                4,
                CoordinateSystemId.Wgs84,
                "EGM96",
                null,
                "EGM96"),
            raster);
        var polygon = new GeoPolygon(new GeoRing([new GeoPosition(new Vector2d(5d, 49d))]));
        var extentFeature = new GeospatialFeature("extent", "playable", polygon, CoordinateSystemId.Wgs84);
        var extent = new SelectedExtentFeature(extentFeature, polygon);
        var sources = new List<LoadedSource> { elevation.Source };
        var features = new List<GeospatialFeature> { extentFeature };
        var paths = new List<string> { "/tmp/manifest.json", "/tmp/manifest.json", "/tmp/dem.tif" };

        var prepared = new PreparedImportSources(
            manifest,
            new string('b', 64),
            paths,
            sources,
            features,
            extent,
            elevation);

        sources.Clear();
        features.Clear();
        paths.Clear();

        Assert.Single(prepared.Sources);
        Assert.Single(prepared.VectorFeatures);
        Assert.Equal(["/tmp/manifest.json", "/tmp/dem.tif"], prepared.ProtectedInputPaths);

        Assert.Throws<ArgumentException>(() => new PreparedImportSources(
            manifest,
            "short",
            null,
            null,
            null,
            extent,
            elevation));

        Assert.Throws<ArgumentException>(() => new PreparedImportSources(
            manifest,
            new string('b', 64),
            ["/tmp/manifest.json", null!],
            [],
            [],
            extent,
            elevation));
    }

    private static string DefaultSources(
        string demRelativePath,
        string coverRelativePath = null,
        string demDatum = null,
        string coverDatum = null,
        string vectorPath = "features.kmz")
    {
        var demDatumClause = demDatum is null ? string.Empty : $", \"verticalDatum\": \"{demDatum}\"";
        var coverDatumClause = coverDatum is null ? string.Empty : $", \"verticalDatum\": \"{coverDatum}\"";
        var vectorClause = vectorPath is null
            ? string.Empty
            : $",\n            {{ \"id\": \"vectors\", \"kind\": \"kmz\", \"path\": \"{vectorPath}\", \"role\": \"vector\" }}";
        var coverClause = coverRelativePath is null
            ? string.Empty
            : $",\n            {{ \"id\": \"cover\", \"kind\": \"geotiff\", \"path\": \"{coverRelativePath}\", \"role\": \"classification\"{coverDatumClause} }}";

        return $$"""
            { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent", "label": "Front extent", "attribution": "operator survey" },
            { "id": "dem", "kind": "geotiff", "path": "{{demRelativePath}}", "role": "elevation"{{demDatumClause}}, "label": "DEM", "attribution": "operator survey", "horizontalAccuracyMeters": 0.6, "verticalAccuracyMeters": 1.5 }{{vectorClause}}{{coverClause}}
            """;
    }

    private static string Kml(string documentContent) =>
        $"<kml xmlns=\"{KmlNs}\"><Document>{documentContent}</Document></kml>";

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);

        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static GeoDiagnostic AssertSingleError(GeoDiagnosticBag diagnostics, string code)
    {
        var diagnostic = Assert.Single(diagnostics.Items);

        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);

        return diagnostic;
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "panopticon-sourceloading-" + Guid.NewGuid().ToString("N") + ".tif");
            File.WriteAllText(Path, "x");
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }

    private sealed class ImportProject : IDisposable
    {
        public ImportProject()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "panopticon-sourceloading-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string ManifestPath => System.IO.Path.Combine(Root, "manifest.json");

        public string FullPath(string relativePath) => System.IO.Path.Combine(Root, relativePath);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        public void WriteManifest(string sourcesJson, string tail = "")
        {
            File.WriteAllText(
                ManifestPath,
                $$"""
                {
                  "sources": [
                    {{sourcesJson}}
                  ],
                  "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
                  "metersPerGameUnit": 1.25,
                  "defaultMaterial": "grass",
                  "tolerances": { "relativeVerticalMeters": 2 }{{tail}}
                }
                """);
        }

        public void WriteRawManifest(string json)
        {
            File.WriteAllText(ManifestPath, json);
        }

        public void WriteDefaultExtentKml()
        {
            WriteExtentKml($"""
                <Placemark id="playable"><name>area</name><Polygon><outerBoundaryIs><LinearRing><coordinates>{ExtentRing}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>
                <Placemark id="peak"><name>peak</name><Point><coordinates>5.1,49.1,120</coordinates></Point></Placemark>
                """);
        }

        public void WriteExtentKml(string placemarks)
        {
            File.WriteAllText(FullPath("area.kml"), Kml(placemarks));
        }

        public void WriteKmlPoints(string relativePath, int count)
        {
            var placemarks = string.Concat(
                Enumerable.Range(0, count).Select(static i => $"<Placemark id=\"p{i}\"><name>p{i}</name><Point><coordinates>5.0{i % 10},49.0</coordinates></Point></Placemark>"));

            File.WriteAllText(FullPath(relativePath), Kml(placemarks));
        }

        public void WriteKmlLine(string relativePath, int positions)
        {
            var coordinates = string.Join(
                " ",
                Enumerable.Range(0, positions).Select(static i => $"5.{i:00},49.00"));
            var placemark = $"<Placemark id=\"line\"><name>line</name><LineString><coordinates>{coordinates}</coordinates></LineString></Placemark>";

            File.WriteAllText(FullPath(relativePath), Kml(placemark));
        }

        public void WriteDefaultVectorKmz()
        {
            var kml = Kml("""
                <Placemark id="river"><name>river</name><ExtendedData><Data name="cover"><value>water</value></Data></ExtendedData><LineString><coordinates>5.0,49.05 5.1,49.1</coordinates></LineString></Placemark>
                <Placemark id="forest"><name>forest</name><Polygon><outerBoundaryIs><LinearRing><coordinates>5.05,49.1 5.1,49.1 5.1,49.15 5.05,49.15 5.05,49.1</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>
                """);

            using var stream = new MemoryStream();

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(archive.CreateEntry("doc.kml", CompressionLevel.Optimal).Open());
                writer.Write(kml);
            }

            File.WriteAllBytes(FullPath("features.kmz"), stream.ToArray());
        }

        public RasterFile WriteDem()
        {
            var options = new SyntheticGeoTiffOptions { VerticalCrsEpsg = 5773 };
            var samples = new double[options.Width * options.Height];

            for (var row = 0; row < options.Height; row++)
            {
                for (var column = 0; column < options.Width; column++)
                {
                    samples[row * options.Width + column] = 10d + column + 2d * row;
                }
            }

            return WriteRaster(samples, options);
        }

        public RasterFile WriteClassification()
        {
            var options = new SyntheticGeoTiffOptions { SampleFormat = RasterSampleFormat.UInt8 };
            var samples = new double[options.Width * options.Height];

            for (var row = 0; row < options.Height; row++)
            {
                for (var column = 0; column < options.Width; column++)
                {
                    samples[row * options.Width + column] = 10d * (1d + ((column + row) % 4));
                }
            }

            return WriteRaster(samples, options);
        }

        private RasterFile WriteRaster(double[] samples, SyntheticGeoTiffOptions options)
        {
            var fixture = SyntheticGeoTiffWriter.Write(samples, options, Root);
            var directoryName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(fixture.FilePath));

            return new RasterFile(
                directoryName + "/raster.tif",
                fixture.FilePath);
        }
    }

    private sealed record RasterFile(string RelativePath, string FullPath);
}
