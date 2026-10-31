using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Manifest;
using Xunit;

namespace Panopticon.Geospatial.Tests.Manifest;

public class ManifestLoaderTests
{
    private const string FullManifest = """
        {
          "sources": [
            { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent", "label": "Front extent", "attribution": "operator survey" },
            { "id": "dem", "kind": "geotiff", "path": "dem.tif", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1.5, "horizontalAccuracyMeters": 0.6 },
            { "id": "cover", "kind": "geotiff", "path": "cover.tif", "role": "classification" },
            { "id": "vectors", "kind": "kmz", "path": "features.kmz", "role": "vector" }
          ],
          "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
          "metersPerGameUnit": 1.25,
          "defaultMaterial": "grass",
          "extentFeatureId": "playable",
          "verticalPolicy": { "originMeters": 120, "exaggeration": 1.5 },
          "rasterPolicy": {
            "noData": "nearest-valid",
            "noDataSearchRadiusMeters": 250.5,
            "edge": "constant",
            "edgeConstantMeters": 30,
            "imageryPixelsPerSample": 4
          },
          "tolerances": { "horizontalMeters": 2, "verticalMeters": 1.5 },
          "materialRules": [
            { "classificationValue": 20, "material": "shrub" },
            { "sourceId": "vectors", "attribute": "cover", "value": "water", "material": "water" }
          ],
          "featureMappings": [
            { "sourceId": "vectors", "geometry": "polygon", "attribute": "kind", "value": "river", "target": "water" },
            { "sourceId": "vectors", "geometry": "point", "attribute": "name", "value": "bridge", "target": "object", "objectDefinition": "BridgeStone", "orientation": "fixed", "orientationDegrees": 90 },
            { "sourceId": "extent", "geometry": "point", "attribute": "name", "target": "landmark" },
            { "sourceId": "vectors", "geometry": "polygon", "attribute": "kind", "value": "forest", "target": "material", "material": "forest" },
            { "sourceId": "vectors", "geometry": "polygon", "attribute": "zone", "value": "keep", "target": "passability", "passable": false }
          ],
          "slopePolicy": { "impassableSlopeDegrees": 40, "unbuildableSlopeDegrees": 12 },
          "maxScaleError": 0.0005
        }
        """;

    private const string MinimalManifest = """
        {
          "sources": [
            { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
            { "id": "dem", "kind": "geotiff", "path": "dem.tif", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1 }
          ],
          "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
          "defaultMaterial": "grass",
          "metersPerGameUnit": 1.25
        }
        """;

    private const string UnknownDatumManifest = """
        {
          "sources": [
            { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
            { "id": "dem", "kind": "geotiff", "path": "dem.tif", "role": "elevation" }
          ],
          "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
          "defaultMaterial": "grass",
          "metersPerGameUnit": 1.25,
          "tolerances": { "relativeVerticalMeters": 2 }
        }
        """;

    private const string PolicyManifest = """
        {
          "sources": [
            { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
            { "id": "dem", "kind": "geotiff", "path": "dem.tif", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1 }
          ],
          "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
          "defaultMaterial": "grass",
          "metersPerGameUnit": 1.25,
          "rasterPolicy": { "noData": "nearest-valid", "noDataSearchRadiusMeters": 10, "edge": "constant", "edgeConstantMeters": 5, "imageryPixelsPerSample": 2 }
        }
        """;

    private const string OptionalNullManifest = """
        {
          "sources": [
            { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
            { "id": "dem", "kind": "geotiff", "path": "dem.tif", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1 }
          ],
          "targetCoordinateSystem": { "type": "transverse-mercator" },
          "defaultMaterial": "grass",
          "metersPerGameUnit": 1.25,
          "verticalPolicy": null,
          "rasterPolicy": null,
          "tolerances": null,
          "materialRules": null,
          "featureMappings": null,
          "slopePolicy": null
        }
        """;

    private static (GeospatialImportManifest Manifest, GeoDiagnosticBag Diagnostics) LoadJson(
        string json,
        GeospatialImportLimits limits = null) =>
        LoadInScope(json, static _ => { }, limits);

    private static (GeospatialImportManifest Manifest, GeoDiagnosticBag Diagnostics) LoadInScope(
        string json,
        Action<string> setup,
        GeospatialImportLimits limits = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "panopticon-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            setup(directory);
            var path = Path.Combine(directory, "manifest.json");
            File.WriteAllText(path, json);
            var diagnostics = new GeoDiagnosticBag();
            var manifest = ManifestLoader.Load(path, diagnostics, limits);
            return (manifest, diagnostics);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string WithoutMember(string json, string member)
    {
        var pattern = "\"" + member + "\":\\s*(?:null|\\{[^{}]*\\}|\"[^\"]*\"|true|false|-?\\d+(?:\\.\\d+)?)";
        var match = Regex.Match(json, pattern);
        Assert.True(match.Success, $"Member '{member}' not found.");
        var result = json.Remove(match.Index, match.Length);
        result = Regex.Replace(result, "{\\s*,", "{");
        result = Regex.Replace(result, ",\\s*,", ",");
        return Regex.Replace(result, ",\\s*}", "}");
    }

    private static GeoDiagnostic AssertSingleError(GeoDiagnosticBag diagnostics, string code)
    {
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(GeoStage.Manifest, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
        return diagnostic;
    }

    private static List<string> ErrorCodes(GeoDiagnosticBag diagnostics) =>
        diagnostics.Items.Select(static item => item.Code).ToList();

    [Fact]
    public void FullManifest_RoundTripsToExpectedRecords()
    {
        var (manifest, diagnostics) = LoadJson(FullManifest);

        Assert.False(diagnostics.HasErrors);
        var loaded = Assert.IsType<GeospatialImportManifest>(manifest);
        Assert.Equal(
        [
            new ManifestSource("extent", ManifestSourceKind.Kml, "area.kml", ManifestSourceRole.Extent, "unknown", "Front extent", "operator survey", null, null),
            new ManifestSource("dem", ManifestSourceKind.Geotiff, "dem.tif", ManifestSourceRole.Elevation, "EGM96", null, null, 0.6, 1.5),
            new ManifestSource("cover", ManifestSourceKind.Geotiff, "cover.tif", ManifestSourceRole.Classification, "unknown", null, null, null, null),
            new ManifestSource("vectors", ManifestSourceKind.Kmz, "features.kmz", ManifestSourceRole.Vector, "unknown", null, null, null, null),
        ], loaded.Sources);
        Assert.Equal(new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, 31, ManifestHemisphere.North, null), loaded.TargetCoordinateSystem);
        Assert.Equal(1.25, loaded.MetersPerGameUnit);
        Assert.Equal("grass", loaded.DefaultMaterial);
        Assert.Equal("playable", loaded.ExtentFeatureId);
        Assert.Equal(new VerticalPolicy(120, 1.5), loaded.VerticalPolicy);
        Assert.Equal(
            new RasterPolicy(ManifestNoDataPolicy.NearestValid, 250.5, null, ManifestEdgePolicy.Constant, 30, null, 4),
            loaded.RasterPolicy);
        Assert.Equal(new ToleranceSpec(2, 1.5, null), loaded.Tolerances);
        Assert.Equal(
        [
            new MaterialRule("shrub", 20, null, null, null),
            new MaterialRule("water", null, "vectors", "cover", "water"),
        ], loaded.MaterialRules);
        Assert.Equal(
        [
            new FeatureMappingRule("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon, "kind", "river", null, null, null, null, null, null),
            new FeatureMappingRule("vectors", ManifestFeatureTarget.Object, ManifestFeatureGeometry.Point, "name", "bridge", "BridgeStone", ManifestFeatureOrientation.Fixed, 90, null, null, null),
            new FeatureMappingRule("extent", ManifestFeatureTarget.Landmark, ManifestFeatureGeometry.Point, "name", null, null, null, null, null, null, null),
            new FeatureMappingRule("vectors", ManifestFeatureTarget.Material, ManifestFeatureGeometry.Polygon, "kind", "forest", null, null, null, "forest", null, null),
            new FeatureMappingRule("vectors", ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon, "zone", "keep", null, null, null, null, false, null),
        ], loaded.FeatureMappings);
        Assert.Equal(new SlopePolicy(40, 12), loaded.SlopePolicy);
        Assert.Equal(0.0005, loaded.MaxScaleError);
    }

    [Fact]
    public void UnknownDatumManifest_LoadsWithRelativeTolerance()
    {
        var (manifest, diagnostics) = LoadJson(UnknownDatumManifest);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
        Assert.Equal("unknown", manifest!.Sources[1].VerticalDatum);
        Assert.Equal(2, manifest.Tolerances.RelativeVerticalMeters);
    }

    [Fact]
    public void AlternateKnownDatum_IsAccepted()
    {
        var json = FullManifest.Replace("\"verticalDatum\": \"EGM96\"", "\"verticalDatum\": \"EGM2008\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
        Assert.Equal("EGM2008", manifest!.Sources[1].VerticalDatum);
    }

    [Fact]
    public void OptionalNullSections_UseDefaults()
    {
        var (manifest, diagnostics) = LoadJson(OptionalNullManifest);

        Assert.False(diagnostics.HasErrors);
        var loaded = Assert.IsType<GeospatialImportManifest>(manifest);
        Assert.Equal(new VerticalPolicy(), loaded.VerticalPolicy);
        Assert.Equal(new RasterPolicy(), loaded.RasterPolicy);
        Assert.Equal(new ToleranceSpec(), loaded.Tolerances);
        Assert.Empty(loaded.MaterialRules);
        Assert.Empty(loaded.FeatureMappings);
        Assert.Equal(new SlopePolicy(), loaded.SlopePolicy);
        Assert.Equal(0.001, loaded.MaxScaleError);
        Assert.Null(loaded.ExtentFeatureId);
        Assert.Equal(ManifestCrsKind.TransverseMercator, loaded.TargetCoordinateSystem.Kind);
        Assert.Null(loaded.TargetCoordinateSystem.CentralMeridianDegrees);
    }

    [Fact]
    public void ManifestAtExactByteLimit_Loads()
    {
        var limits = new GeospatialImportLimits(MaxManifestBytes: MinimalManifest.Length);
        var (manifest, diagnostics) = LoadJson(MinimalManifest, limits);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
    }

    [Fact]
    public void OversizedManifest_FailsWithSizeCodeBeforeDeserialization()
    {
        var json = "{ this is not valid json ";
        var (manifest, diagnostics) = LoadJson(json, new GeospatialImportLimits(MaxManifestBytes: 8));

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestOversized);
        Assert.Contains("8 bytes", diagnostic.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> UnknownEnumTokenCases => new()
    {
        { MinimalManifest.Replace("\"kind\": \"geotiff\"", "\"kind\": \"shapefile\""), "ManifestSourceKind" },
        { MinimalManifest.Replace("\"role\": \"elevation\"", "\"role\": \"height\""), "ManifestSourceRole" },
        { MinimalManifest.Replace("\"type\": \"utm\"", "\"type\": \"albers\""), "ManifestCrsKind" },
        { MinimalManifest.Replace("\"hemisphere\": \"north\"", "\"hemisphere\": \"equator\""), "ManifestHemisphere" },
        { FullManifest.Replace("\"noData\": \"nearest-valid\"", "\"noData\": \"interpolate\""), "ManifestNoDataPolicy" },
        { FullManifest.Replace("\"edge\": \"constant\"", "\"edge\": \"mirror\""), "ManifestEdgePolicy" },
        { FullManifest.Replace("\"target\": \"water\"", "\"target\": \"wall\""), "ManifestFeatureTarget" },
        { FullManifest.Replace("\"geometry\": \"polygon\"", "\"geometry\": \"square\""), "ManifestFeatureGeometry" },
        { FullManifest.Replace("\"orientation\": \"fixed\"", "\"orientation\": \"sideways\""), "ManifestFeatureOrientation" },
    };

    [Theory]
    [MemberData(nameof(UnknownEnumTokenCases))]
    public void UnknownEnumToken_ProducesSchemaDiagnostic(string json, string enumName)
    {
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSchemaInvalid);
        Assert.Contains(enumName, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownTopLevelMember_ProducesSchemaDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"defaultMaterial\": \"grass\",",
            "\"defaultMaterial\": \"grass\", \"extra\": 1,");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSchemaInvalid);
    }

    [Fact]
    public void UnknownNestedMember_ProducesSchemaDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"verticalAccuracyMeters\": 1 }",
            "\"verticalAccuracyMeters\": 1, \"bogus\": true }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSchemaInvalid);
    }

    [Fact]
    public void WrongMemberType_ProducesSchemaDiagnostic()
    {
        var json = MinimalManifest.Replace("\"metersPerGameUnit\": 1.25", "\"metersPerGameUnit\": \"fast\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSchemaInvalid);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void MalformedRoot_ProducesSchemaDiagnostic(string json)
    {
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSchemaInvalid);
    }

    [Fact]
    public void MissingManifestFile_ProducesIoDiagnostic()
    {
        var path = Path.Combine(Path.GetTempPath(), "panopticon-manifest-missing-" + Guid.NewGuid().ToString("N") + ".json");
        var diagnostics = new GeoDiagnosticBag();

        var manifest = ManifestLoader.Load(path, diagnostics);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestFailure);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ManifestLoader.Load(null!, new GeoDiagnosticBag()));
        Assert.Throws<ArgumentException>(() => ManifestLoader.Load(string.Empty, new GeoDiagnosticBag()));
        Assert.Throws<ArgumentNullException>(() => ManifestLoader.Load("manifest.json", null!));
    }

    [Fact]
    public void MissingSources_ProducesMissingRequiredDiagnostic()
    {
        var json = """
            {
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "defaultMaterial": "grass",
              "metersPerGameUnit": 1.25
            }
            """;
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("sources", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullSources_ProducesMissingRequiredDiagnostic()
    {
        var json = """
            {
              "sources": null,
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "defaultMaterial": "grass",
              "metersPerGameUnit": 1.25
            }
            """;
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
    }

    [Fact]
    public void EmptySources_ProducesMissingRequiredDiagnostic()
    {
        var json = """
            {
              "sources": [],
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "defaultMaterial": "grass",
              "metersPerGameUnit": 1.25
            }
            """;
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
    }

    [Fact]
    public void NullSourceEntry_ProducesMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"verticalAccuracyMeters\": 1 }",
            "\"verticalAccuracyMeters\": 1 }, null");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("sources[2]", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSourceId_ProducesMissingRequiredDiagnostic()
    {
        var json = WithoutMember(MinimalManifest, "id");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("sources[0].id", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSourceKind_ProducesMissingRequiredDiagnostic()
    {
        var json = WithoutMember(MinimalManifest, "kind");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("sources[0].kind", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSourcePath_ProducesMissingRequiredDiagnostic()
    {
        var json = WithoutMember(MinimalManifest, "path");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("sources[0].path", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSourceRole_ProducesMissingRequiredDiagnostics()
    {
        var json = WithoutMember(MinimalManifest, "role");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        Assert.All(diagnostics.Items, static item => Assert.Equal(GeoDiagnosticCodes.ManifestMissingRequired, item.Code));
        Assert.Contains(diagnostics.Items, static item => item.Message.Contains("sources[0].role", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Items, static item => item.Message.Contains("extent", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingTargetCoordinateSystem_ProducesMissingRequiredDiagnostic()
    {
        var json = WithoutMember(MinimalManifest, "targetCoordinateSystem");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("targetCoordinateSystem", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullTargetCoordinateSystem_ProducesMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"targetCoordinateSystem\": { \"type\": \"utm\", \"zone\": 31, \"hemisphere\": \"north\" }",
            "\"targetCoordinateSystem\": null");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
    }

    [Fact]
    public void MissingTargetType_ProducesMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"targetCoordinateSystem\": { \"type\": \"utm\", \"zone\": 31, \"hemisphere\": \"north\" }",
            "\"targetCoordinateSystem\": { \"zone\": 31, \"hemisphere\": \"north\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
    }

    [Fact]
    public void UtmWithoutZone_ProducesMissingRequiredDiagnostic()
    {
        var json = WithoutMember(MinimalManifest, "zone");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("zone", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UtmWithoutHemisphere_ProducesMissingRequiredDiagnostic()
    {
        var json = WithoutMember(MinimalManifest, "hemisphere");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("hemisphere", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingMetersPerGameUnit_ProducesMissingRequiredDiagnostic()
    {
        var json = WithoutMember(MinimalManifest, "metersPerGameUnit");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("metersPerGameUnit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDefaultMaterial_ProducesMissingRequiredDiagnostic()
    {
        var json = WithoutMember(MinimalManifest, "defaultMaterial");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("defaultMaterial", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoExtentSource_ProducesMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "{ \"id\": \"extent\", \"kind\": \"kml\", \"path\": \"area.kml\", \"role\": \"extent\" },",
            "{ \"id\": \"features\", \"kind\": \"kml\", \"path\": \"features.kml\", \"role\": \"vector\" },");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("exactly one", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoExtentSources_ProduceMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "{ \"id\": \"extent\", \"kind\": \"kml\", \"path\": \"area.kml\", \"role\": \"extent\" },",
            "{ \"id\": \"extent\", \"kind\": \"kml\", \"path\": \"area.kml\", \"role\": \"extent\" },\n            { \"id\": \"extent2\", \"kind\": \"kmz\", \"path\": \"area2.kmz\", \"role\": \"extent\" },");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("found 2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoElevationSource_ProducesMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "{ \"id\": \"dem\", \"kind\": \"geotiff\", \"path\": \"dem.tif\", \"role\": \"elevation\", \"verticalDatum\": \"EGM96\", \"verticalAccuracyMeters\": 1 }",
            "{ \"id\": \"cover\", \"kind\": \"geotiff\", \"path\": \"cover.tif\", \"role\": \"classification\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("elevation", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownDatumWithoutRelativeTolerance_ProducesMissingRequiredDiagnostic()
    {
        var json = WithoutMember(UnknownDatumManifest, "tolerances");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("relativeVerticalMeters", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownDatumWithoutAccuracyOrTolerance_ProducesMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"verticalDatum\": \"EGM96\", \"verticalAccuracyMeters\": 1",
            "\"verticalDatum\": \"EGM96\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("verticalMeters", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownDatumWithAccuracy_DoesNotRequireTolerance()
    {
        var (manifest, diagnostics) = LoadJson(MinimalManifest);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
        Assert.Equal(new ToleranceSpec(), manifest!.Tolerances);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.25)]
    public void NonPositiveMetersPerGameUnit_ProducesValueInvalidDiagnostic(double value)
    {
        var json = MinimalManifest.Replace(
            "\"metersPerGameUnit\": 1.25",
            "\"metersPerGameUnit\": " + value.ToString(CultureInfo.InvariantCulture));
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("metersPerGameUnit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WhitespaceDefaultMaterial_ProducesMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace("\"defaultMaterial\": \"grass\"", "\"defaultMaterial\": \"  \"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
    }

    [Fact]
    public void WhitespaceExtentFeatureId_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace("\"extentFeatureId\": \"playable\"", "\"extentFeatureId\": \"   \"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
    }

    [Fact]
    public void NonPositiveMaxScaleError_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace("\"maxScaleError\": 0.0005", "\"maxScaleError\": 0");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("maxScaleError", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultMaxScaleError_IsApplied()
    {
        var json = WithoutMember(FullManifest, "maxScaleError");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
        Assert.Equal(0.001, manifest!.MaxScaleError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void NonPositiveExaggeration_ProducesValueInvalidDiagnostic(double value)
    {
        var json = FullManifest.Replace(
            "\"exaggeration\": 1.5",
            "\"exaggeration\": " + value.ToString(CultureInfo.InvariantCulture));
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("exaggeration", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void UtmZoneOutOfRange_ProducesValueInvalidDiagnostic(int zone)
    {
        var json = MinimalManifest.Replace("\"zone\": 31", "\"zone\": " + zone);
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("zone", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UtmWithCentralMeridian_ProducesValueInvalidDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"targetCoordinateSystem\": { \"type\": \"utm\", \"zone\": 31, \"hemisphere\": \"north\" }",
            "\"targetCoordinateSystem\": { \"type\": \"utm\", \"zone\": 31, \"hemisphere\": \"north\", \"centralMeridian\": 3 }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
    }

    [Fact]
    public void TransverseMercatorWithZone_ProducesValueInvalidDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"targetCoordinateSystem\": { \"type\": \"utm\", \"zone\": 31, \"hemisphere\": \"north\" }",
            "\"targetCoordinateSystem\": { \"type\": \"transverse-mercator\", \"zone\": 31 }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
    }

    [Theory]
    [InlineData(181)]
    [InlineData(-180.5)]
    public void TransverseMercatorMeridianOutOfRange_ProducesValueInvalidDiagnostic(double meridian)
    {
        var json = OptionalNullManifest.Replace(
            "\"targetCoordinateSystem\": { \"type\": \"transverse-mercator\" }",
            "\"targetCoordinateSystem\": { \"type\": \"transverse-mercator\", \"centralMeridian\": "
                + meridian.ToString(CultureInfo.InvariantCulture) + " }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
    }

    [Fact]
    public void TransverseMercatorWithCentralMeridian_Loads()
    {
        var json = OptionalNullManifest.Replace(
            "\"targetCoordinateSystem\": { \"type\": \"transverse-mercator\" }",
            "\"targetCoordinateSystem\": { \"type\": \"transverse-mercator\", \"centralMeridian\": 7 }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
        Assert.Equal(
            new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, null, null, 7),
            manifest!.TargetCoordinateSystem);
    }

    [Fact]
    public void NearestValidWithoutRadius_ProducesMissingRequiredDiagnostic()
    {
        var json = PolicyManifest.Replace(
            "\"rasterPolicy\": { \"noData\": \"nearest-valid\", \"noDataSearchRadiusMeters\": 10,",
            "\"rasterPolicy\": { \"noData\": \"nearest-valid\",");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("noDataSearchRadiusMeters", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveNoDataRadius_ProducesValueInvalidDiagnostic(double radius)
    {
        var json = PolicyManifest.Replace(
            "\"noDataSearchRadiusMeters\": 10",
            "\"noDataSearchRadiusMeters\": " + radius.ToString(CultureInfo.InvariantCulture));
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("noDataSearchRadiusMeters", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstantNoDataWithoutValue_ProducesMissingRequiredDiagnostic()
    {
        var json = PolicyManifest.Replace(
            "\"rasterPolicy\": { \"noData\": \"nearest-valid\", \"noDataSearchRadiusMeters\": 10,",
            "\"rasterPolicy\": { \"noData\": \"constant\",");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("noDataConstantMeters", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstantEdgeWithoutValue_ProducesMissingRequiredDiagnostic()
    {
        var json = PolicyManifest.Replace("\"edge\": \"constant\", \"edgeConstantMeters\": 5", "\"edge\": \"constant\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("edgeConstantMeters", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonPositiveImageryPixelsPerSample_ProducesValueInvalidDiagnostic()
    {
        var json = PolicyManifest.Replace("\"imageryPixelsPerSample\": 2", "\"imageryPixelsPerSample\": 0");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("imageryPixelsPerSample", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"horizontalMeters\": 2, \"verticalMeters\": 1.5", "\"horizontalMeters\": 0")]
    [InlineData("\"horizontalMeters\": 2, \"verticalMeters\": 1.5", "\"verticalMeters\": -1")]
    [InlineData("\"horizontalMeters\": 2, \"verticalMeters\": 1.5", "\"relativeVerticalMeters\": 0")]
    public void NonPositiveTolerance_ProducesValueInvalidDiagnostic(string from, string to)
    {
        var json = FullManifest.Replace(from, to);
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
    }

    [Theory]
    [InlineData("\"impassableSlopeDegrees\": 40", "\"impassableSlopeDegrees\": 0")]
    [InlineData("\"unbuildableSlopeDegrees\": 12", "\"unbuildableSlopeDegrees\": 91")]
    [InlineData("\"impassableSlopeDegrees\": 40", "\"impassableSlopeDegrees\": -5")]
    public void SlopeThresholdOutOfRange_ProducesValueInvalidDiagnostic(string from, string to)
    {
        var json = FullManifest.Replace(from, to);
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
    }

    [Fact]
    public void DuplicateSourceIds_ProduceValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace("\"id\": \"cover\"", "\"id\": \"dem\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("Duplicate", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VectorKindCannotServeRasterRole_ProducesValueInvalidDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"kind\": \"geotiff\", \"path\": \"dem.tif\", \"role\": \"elevation\"",
            "\"kind\": \"kml\", \"path\": \"dem.kml\", \"role\": \"elevation\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("cannot serve role", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RasterKindCannotServeExtentRole_ProducesValueInvalidDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "\"kind\": \"kml\", \"path\": \"area.kml\", \"role\": \"extent\"",
            "\"kind\": \"geotiff\", \"path\": \"area.tif\", \"role\": \"extent\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("cannot serve role", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterialRuleWithoutMatcher_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "{ \"classificationValue\": 20, \"material\": \"shrub\" }",
            "{ \"material\": \"shrub\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("classificationValue or attribute", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterialRuleWithBothMatchers_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "{ \"classificationValue\": 20, \"material\": \"shrub\" }",
            "{ \"classificationValue\": 20, \"attribute\": \"t\", \"material\": \"shrub\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("cannot set both", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterialRuleWithoutMaterial_ProducesMissingRequiredDiagnostic()
    {
        var json = FullManifest.Replace(
            "{ \"classificationValue\": 20, \"material\": \"shrub\" }",
            "{ \"classificationValue\": 20 }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("material", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterialRuleWithUnknownSourceId_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "{ \"sourceId\": \"vectors\", \"attribute\": \"cover\", \"value\": \"water\", \"material\": \"water\" }",
            "{ \"sourceId\": \"ghost\", \"attribute\": \"cover\", \"value\": \"water\", \"material\": \"water\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("ghost", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassificationRuleReferencingVectorSource_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "{ \"classificationValue\": 20, \"material\": \"shrub\" }",
            "{ \"classificationValue\": 20, \"sourceId\": \"vectors\", \"material\": \"shrub\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("classification", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassificationRuleWithoutClassificationSource_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "{ \"id\": \"cover\", \"kind\": \"geotiff\", \"path\": \"cover.tif\", \"role\": \"classification\" },",
            string.Empty);
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("classification", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureMappingWithUnknownSourceId_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "{ \"sourceId\": \"vectors\", \"geometry\": \"polygon\", \"attribute\": \"kind\", \"value\": \"river\", \"target\": \"water\" }",
            "{ \"sourceId\": \"ghost\", \"geometry\": \"polygon\", \"attribute\": \"kind\", \"value\": \"river\", \"target\": \"water\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("ghost", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureMappingReferencingRasterSource_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "{ \"sourceId\": \"vectors\", \"geometry\": \"polygon\", \"attribute\": \"kind\", \"value\": \"river\", \"target\": \"water\" }",
            "{ \"sourceId\": \"dem\", \"geometry\": \"polygon\", \"attribute\": \"kind\", \"value\": \"river\", \"target\": \"water\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("vector", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ObjectTargetWithoutObjectDefinition_ProducesMissingRequiredDiagnostic()
    {
        var json = FullManifest.Replace(
            "\"target\": \"object\", \"objectDefinition\": \"BridgeStone\"",
            "\"target\": \"object\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("objectDefinition", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ObjectTargetWithoutOrientation_ProducesMissingRequiredDiagnostic()
    {
        var json = FullManifest.Replace(
            ", \"orientation\": \"fixed\", \"orientationDegrees\": 90 }",
            " }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("orientation", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedOrientationWithoutDegrees_ProducesMissingRequiredDiagnostic()
    {
        var json = FullManifest.Replace(
            "\"orientation\": \"fixed\", \"orientationDegrees\": 90 }",
            "\"orientation\": \"fixed\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("orientationDegrees", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrientationOnNonObjectTarget_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "\"value\": \"river\", \"target\": \"water\" }",
            "\"value\": \"river\", \"target\": \"water\", \"orientation\": \"none\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("object", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetKindIncompatibleWithGeometry_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "\"geometry\": \"polygon\", \"attribute\": \"kind\", \"value\": \"river\", \"target\": \"water\" }",
            "\"geometry\": \"point\", \"attribute\": \"kind\", \"value\": \"river\", \"target\": \"water\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("water", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsTargetCompatible_MatchesDeepDiveZones()
    {
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Landmark, ManifestFeatureGeometry.Point));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Object, ManifestFeatureGeometry.Point));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Road, ManifestFeatureGeometry.Line));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.River, ManifestFeatureGeometry.Line));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Line));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Polygon));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Material, ManifestFeatureGeometry.Polygon));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon));
        Assert.True(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Buildability, ManifestFeatureGeometry.Polygon));
        Assert.False(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Water, ManifestFeatureGeometry.Point));
        Assert.False(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Landmark, ManifestFeatureGeometry.Polygon));
        Assert.False(FeatureMappingRule.IsTargetCompatible(ManifestFeatureTarget.Road, ManifestFeatureGeometry.Polygon));
    }

    [Theory]
    [InlineData("\"path\": \"https://example.com/dem.tif\"")]
    [InlineData("\"path\": \"http://example.com/dem.tif\"")]
    [InlineData("\"path\": \"/etc/passwd\"")]
    [InlineData("\"path\": \"C:\\\\dem.tif\"")]
    [InlineData("\"path\": \"\\\\\\\\server\\\\share\\\\dem.tif\"")]
    [InlineData("\"path\": \"../outside.tif\"")]
    [InlineData("\"path\": \"sub/../../outside.tif\"")]
    [InlineData("\"path\": \"..\\\\outside.tif\"")]
    public void UnsafeSourcePath_ProducesPathRejectedDiagnostic(string replacement)
    {
        var json = MinimalManifest.Replace("\"path\": \"dem.tif\"", replacement);
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSourcePathRejected);
        Assert.NotNull(diagnostic.SourcePath);
    }

    [Fact]
    public void WhitespaceSourcePath_ProducesMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace("\"path\": \"dem.tif\"", "\"path\": \"   \"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
    }

    [Fact]
    public void SourceSymlinkEscapingManifestDirectory_ProducesPathRejectedDiagnostic()
    {
        var outside = Path.Combine(Path.GetTempPath(), "panopticon-manifest-outside-" + Guid.NewGuid().ToString("N") + ".tif");
        File.WriteAllText(outside, "x");
        try
        {
            var json = MinimalManifest.Replace("\"path\": \"dem.tif\"", "\"path\": \"linked.tif\"");
            var (manifest, diagnostics) = LoadInScope(json, directory =>
                File.CreateSymbolicLink(Path.Combine(directory, "linked.tif"), outside));

            Assert.Null(manifest);
            AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSourcePathRejected);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void SourceSymlinkInsideManifestDirectory_Loads()
    {
        var json = MinimalManifest.Replace("\"path\": \"dem.tif\"", "\"path\": \"linked.tif\"");
        var (manifest, diagnostics) = LoadInScope(json, directory =>
        {
            var real = Path.Combine(directory, "dem.tif");
            File.WriteAllText(real, "x");
            File.CreateSymbolicLink(Path.Combine(directory, "linked.tif"), real);
        });

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
    }

    [Fact]
    public void MultipleInvalidFields_ReportAllDistinctDiagnostics()
    {
        var json = FullManifest
            .Replace("\"role\": \"extent\"", "\"role\": \"vector\"")
            .Replace("\"exaggeration\": 1.5", "\"exaggeration\": 0")
            .Replace("\"maxScaleError\": 0.0005", "\"maxScaleError\": 0");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        Assert.True(diagnostics.HasErrors);
        var codes = ErrorCodes(diagnostics);
        Assert.Equal(1, codes.Count(static code => code == GeoDiagnosticCodes.ManifestMissingRequired));
        Assert.Equal(2, codes.Count(static code => code == GeoDiagnosticCodes.ManifestValueInvalid));
    }

    [Fact]
    public void TwoElevationSources_ProduceMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "{ \"id\": \"dem\", \"kind\": \"geotiff\", \"path\": \"dem.tif\", \"role\": \"elevation\", \"verticalDatum\": \"EGM96\", \"verticalAccuracyMeters\": 1 }",
            "{ \"id\": \"dem\", \"kind\": \"geotiff\", \"path\": \"dem.tif\", \"role\": \"elevation\", \"verticalDatum\": \"EGM96\", \"verticalAccuracyMeters\": 1 },\n            { \"id\": \"dem2\", \"kind\": \"geotiff\", \"path\": \"dem2.tif\", \"role\": \"elevation\", \"verticalDatum\": \"EGM96\", \"verticalAccuracyMeters\": 1 }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("elevation", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("found 2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoImagerySources_ProduceMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "{ \"id\": \"extent\", \"kind\": \"kml\", \"path\": \"area.kml\", \"role\": \"extent\" },",
            "{ \"id\": \"extent\", \"kind\": \"kml\", \"path\": \"area.kml\", \"role\": \"extent\" },\n            { \"id\": \"img1\", \"kind\": \"geotiff\", \"path\": \"img1.tif\", \"role\": \"imagery\" },\n            { \"id\": \"img2\", \"kind\": \"geotiff\", \"path\": \"img2.tif\", \"role\": \"imagery\" },");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("imagery", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("found 2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoClassificationSources_ProduceMissingRequiredDiagnostic()
    {
        var json = MinimalManifest.Replace(
            "{ \"id\": \"extent\", \"kind\": \"kml\", \"path\": \"area.kml\", \"role\": \"extent\" },",
            "{ \"id\": \"extent\", \"kind\": \"kml\", \"path\": \"area.kml\", \"role\": \"extent\" },\n            { \"id\": \"cover1\", \"kind\": \"geotiff\", \"path\": \"cover1.tif\", \"role\": \"classification\" },\n            { \"id\": \"cover2\", \"kind\": \"geotiff\", \"path\": \"cover2.tif\", \"role\": \"classification\" },");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("classification", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("found 2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EdgeNearestWithoutRadius_ProducesMissingRequiredDiagnostic()
    {
        var json = PolicyManifest.Replace("\"edge\": \"constant\", \"edgeConstantMeters\": 5", "\"edge\": \"nearest\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("edgeSearchRadiusMeters", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EdgeNearestWithRadius_Loads()
    {
        var json = PolicyManifest.Replace(
            "\"edge\": \"constant\", \"edgeConstantMeters\": 5",
            "\"edge\": \"nearest\", \"edgeSearchRadiusMeters\": 12");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
        Assert.Equal(ManifestEdgePolicy.Nearest, manifest!.RasterPolicy.Edge);
        Assert.Equal(12, manifest.RasterPolicy.EdgeSearchRadiusMeters);
    }

    [Fact]
    public void InactiveNoDataRadiusInvalid_ProducesValueInvalidDiagnostic()
    {
        var json = PolicyManifest.Replace(
            "\"noData\": \"nearest-valid\", \"noDataSearchRadiusMeters\": 10,",
            "\"noData\": \"fail\", \"noDataSearchRadiusMeters\": 0,");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("noDataSearchRadiusMeters", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InactiveEdgeRadiusInvalid_ProducesValueInvalidDiagnostic()
    {
        var json = PolicyManifest.Replace(
            "\"edge\": \"constant\", \"edgeConstantMeters\": 5",
            "\"edge\": \"fail\", \"edgeSearchRadiusMeters\": -1");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("edgeSearchRadiusMeters", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterialTargetWithoutMaterial_ProducesMissingRequiredDiagnostic()
    {
        var json = FullManifest.Replace("\"target\": \"material\", \"material\": \"forest\" }", "\"target\": \"material\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("material", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PassabilityTargetWithoutPassable_ProducesMissingRequiredDiagnostic()
    {
        var json = FullManifest.Replace("\"target\": \"passability\", \"passable\": false }", "\"target\": \"passability\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("passable", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildabilityTargetWithoutBuildable_ProducesMissingRequiredDiagnostic()
    {
        var json = FullManifest.Replace("\"target\": \"passability\", \"passable\": false }", "\"target\": \"buildability\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestMissingRequired);
        Assert.Contains("buildable", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterialOnNonMaterialTarget_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "{ \"sourceId\": \"extent\", \"geometry\": \"point\", \"attribute\": \"name\", \"target\": \"landmark\" }",
            "{ \"sourceId\": \"extent\", \"geometry\": \"point\", \"attribute\": \"name\", \"target\": \"landmark\", \"material\": \"grass\" }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("material", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PassableOnMaterialTarget_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "\"target\": \"material\", \"material\": \"forest\" }",
            "\"target\": \"material\", \"material\": \"forest\", \"passable\": true }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("passable", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildableOnPassabilityTarget_ProducesValueInvalidDiagnostic()
    {
        var json = FullManifest.Replace(
            "\"target\": \"passability\", \"passable\": false }",
            "\"target\": \"passability\", \"passable\": false, \"buildable\": true }");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid);
        Assert.Contains("buildable", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtentSourceAllowedInFeatureMappings_Loads()
    {
        var json = MinimalManifest.Replace(
            "\"metersPerGameUnit\": 1.25",
            "\"metersPerGameUnit\": 1.25,\n          \"featureMappings\": [ { \"sourceId\": \"extent\", \"geometry\": \"point\", \"attribute\": \"name\", \"target\": \"waypoint\" } ]");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
        var mapping = Assert.Single(manifest!.FeatureMappings);
        Assert.Equal("extent", mapping.SourceId);
        Assert.Equal(ManifestFeatureTarget.Waypoint, mapping.Target);
    }

    [Fact]
    public void ExtentSourceAllowedInVectorMaterialRule_Loads()
    {
        var json = MinimalManifest.Replace(
            "\"metersPerGameUnit\": 1.25",
            "\"metersPerGameUnit\": 1.25,\n          \"materialRules\": [ { \"sourceId\": \"extent\", \"attribute\": \"cover\", \"value\": \"water\", \"material\": \"water\" } ]");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
        var rule = Assert.Single(manifest!.MaterialRules);
        Assert.Equal("extent", rule.SourceId);
    }

    [Fact]
    public void ParentSegmentInsideTree_ProducesPathRejectedDiagnostic()
    {
        var json = MinimalManifest.Replace("\"path\": \"dem.tif\"", "\"path\": \"area/../dem.tif\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.Null(manifest);
        var diagnostic = AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSourcePathRejected);
        Assert.Contains("..", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BackslashSourcePath_IsNormalizedAndLoaded()
    {
        var json = MinimalManifest.Replace("\"path\": \"dem.tif\"", "\"path\": \"sub\\\\dem.tif\"");
        var (manifest, diagnostics) = LoadJson(json);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(manifest);
        Assert.Equal("sub/dem.tif", manifest!.Sources[1].Path);
    }

    [Fact]
    public void SymlinkWithParentTraversal_ProducesPathRejectedDiagnostic()
    {
        var json = MinimalManifest.Replace("\"path\": \"dem.tif\"", "\"path\": \"linked/../escape.tif\"");
        var (manifest, diagnostics) = LoadInScope(json, directory =>
        {
            Directory.CreateDirectory(Path.Combine(directory, "sub"));
            File.CreateSymbolicLink(Path.Combine(directory, "linked"), Path.Combine(directory, "sub"));
        });

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSourcePathRejected);
    }

    [Fact]
    public void SymlinkLoop_ProducesPathRejectedDiagnostic()
    {
        var json = MinimalManifest.Replace("\"path\": \"dem.tif\"", "\"path\": \"loop.tif\"");
        var (manifest, diagnostics) = LoadInScope(json, directory =>
            File.CreateSymbolicLink(Path.Combine(directory, "loop.tif"), Path.Combine(directory, "loop.tif")));

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestSourcePathRejected);
    }

    [Fact]
    public void ManifestOneByteOverLimit_ProducesOversizedDiagnostic()
    {
        var limit = MinimalManifest.Length;
        var json = MinimalManifest.PadRight(limit + 1);
        var (manifest, diagnostics) = LoadJson(json, new GeospatialImportLimits(MaxManifestBytes: limit));

        Assert.Null(manifest);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ManifestOversized);
    }

    [Fact]
    public void ReturnedCollections_AreReadOnly()
    {
        var (manifest, diagnostics) = LoadJson(FullManifest);

        Assert.False(diagnostics.HasErrors);
        Assert.IsType<ReadOnlyCollection<ManifestSource>>(manifest!.Sources);
        Assert.IsType<ReadOnlyCollection<MaterialRule>>(manifest.MaterialRules);
        Assert.IsType<ReadOnlyCollection<FeatureMappingRule>>(manifest.FeatureMappings);
    }
}
