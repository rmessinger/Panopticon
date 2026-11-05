using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Manifest;
using Xunit;

namespace Panopticon.Geospatial.Tests.Manifest;

/// <summary>
/// Anti-rot guard for the committed Sedan example manifest (WI-R2-01 step 2): the file
/// keeps loading through the real ManifestLoader/ManifestValidator with no source data,
/// game install, GDAL, network, or environment present.
/// Placeholder control values exist only so the committed example parses; every control
/// value must be regenerated from the frozen real references by the prepare workflow and
/// placeholder values must never be copied into a proof manifest; nothing here asserts
/// or implies a physical proof PASS.
/// </summary>
public class SedanIgnProofManifestTests
{
    private const double PlannedLatitudeMin = 49.6990;

    private const double PlannedLatitudeMax = 49.7050;

    private const double PlannedLongitudeMin = 4.9425;

    private const double PlannedLongitudeMax = 4.9515;

    private const string ExampleOnlyMarker = "EXAMPLE ONLY - NOT MEASURED";

    private const string IgnAttribution = "IGN, Licence Ouverte 2.0";

    [Fact]
    public void ExampleManifest_LoadsWithZeroErrorDiagnosticsWithoutSourceData()
    {
        LoadExampleManifest();
    }

    [Fact]
    public void TargetCoordinateSystemAndScale_MatchPlannedSedanConfiguration()
    {
        var manifest = LoadExampleManifest();

        Assert.Equal(
            new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, 31, ManifestHemisphere.North, null),
            manifest.TargetCoordinateSystem);
        Assert.Equal(0.3048, manifest.MetersPerGameUnit);

        // The vertical origin is a placeholder default, per the example's labeling.
        Assert.Equal(new VerticalPolicy(0.0, 1.0), manifest.VerticalPolicy);
    }

    [Fact]
    public void Sources_AreExactlyOneExtentOneElevationAndOneImagery()
    {
        var manifest = LoadExampleManifest();

        Assert.Equal(3, manifest.Sources.Count);
        var extent = Assert.Single(manifest.Sources, static source => source.Role == ManifestSourceRole.Extent);
        var elevation = Assert.Single(manifest.Sources, static source => source.Role == ManifestSourceRole.Elevation);
        var imagery = Assert.Single(manifest.Sources, static source => source.Role == ManifestSourceRole.Imagery);
        Assert.Equal(ManifestSourceKind.Kml, extent.Kind);
        Assert.Equal(ManifestSourceKind.Geotiff, elevation.Kind);
        Assert.Equal(ManifestSourceKind.Geotiff, imagery.Kind);
        Assert.Equal("NGF-IGN69", elevation.VerticalDatum);
    }

    [Fact]
    public void SourcePaths_AreRelativeWithLabelsAttributionsAndNoStatedAccuracy()
    {
        var manifest = LoadExampleManifest();

        Assert.All(manifest.Sources, static source =>
        {
            Assert.False(Path.IsPathRooted(source.Path));
            Assert.DoesNotContain("..", source.Path);
            Assert.False(string.IsNullOrWhiteSpace(source.Label));
            Assert.False(string.IsNullOrWhiteSpace(source.Attribution));
            Assert.Null(source.HorizontalAccuracyMeters);
            Assert.Null(source.VerticalAccuracyMeters);
        });

        foreach (var id in new[] { "rge-alti-example", "bd-ortho-example" })
        {
            var source = Assert.Single(manifest.Sources, candidate => candidate.Id == id);
            Assert.Contains(IgnAttribution, source.Attribution);
        }

        var extent = Assert.Single(manifest.Sources, static source => source.Role == ManifestSourceRole.Extent);
        Assert.Contains("generated extent KML", extent.Attribution);
    }

    [Fact]
    public void RasterPolicy_IsNearestValidWithRadiusAndFailingEdge()
    {
        var manifest = LoadExampleManifest();
        var policy = manifest.RasterPolicy;

        Assert.Equal(ManifestNoDataPolicy.NearestValid, policy.NoData);
        Assert.NotNull(policy.NoDataSearchRadiusMeters);
        Assert.True(double.IsFinite(policy.NoDataSearchRadiusMeters.Value));
        Assert.True(policy.NoDataSearchRadiusMeters.Value > 0);
        Assert.Equal(ManifestEdgePolicy.Fail, policy.Edge);
        Assert.Equal(10, policy.ImageryPixelsPerSample);
    }

    [Fact]
    public void Tolerances_CarryOnlyTheExampleVerticalPlaceholder()
    {
        var manifest = LoadExampleManifest();

        // The vertical tolerance is an EXAMPLE ONLY placeholder with no operator validity;
        // ToleranceSpec has no label field, so its EXAMPLE-ONLY status is carried by the
        // elevation source Label plus these tests.
        var tolerances = manifest.Tolerances;
        Assert.NotNull(tolerances.VerticalMeters);
        Assert.True(tolerances.VerticalMeters!.Value > 0);
        Assert.Null(tolerances.HorizontalMeters);
        Assert.Null(tolerances.RelativeVerticalMeters);
    }

    [Fact]
    public void ControlPoints_AreSevenExamplePlaceholdersInsideThePlannedExtent()
    {
        var manifest = LoadExampleManifest();
        var points = manifest.ControlPoints;

        Assert.Equal(7, points.Count);
        Assert.Equal(7, points.Select(static point => point.Name).Distinct(StringComparer.Ordinal).Count());

        var verticalPoints = points.Where(static point => point.ImageryRgb is null).ToList();
        var imageryPoints = points.Where(static point => point.ImageryRgb is not null).ToList();
        Assert.Equal(5, verticalPoints.Count);
        Assert.Equal(2, imageryPoints.Count);

        Assert.All(verticalPoints, static point =>
        {
            Assert.Equal(0.0, point.ElevationMeters);
            Assert.Null(point.ExpectedGameX);
            Assert.Null(point.ExpectedGameY);
            Assert.Null(point.FeatureId);
            Assert.Null(point.ImageryToleranceBytes);
        });

        Assert.All(imageryPoints, static point =>
        {
            Assert.Null(point.ElevationMeters);
            Assert.Equal(3, point.ImageryRgb!.Count);
            Assert.NotNull(point.ImageryToleranceBytes);
            Assert.True(point.ImageryToleranceBytes!.Value > 0);
        });

        Assert.All(points, static point =>
        {
            Assert.Contains(ExampleOnlyMarker, point.Name);
            Assert.InRange(point.Latitude, PlannedLatitudeMin, PlannedLatitudeMax);
            Assert.InRange(point.Longitude, PlannedLongitudeMin, PlannedLongitudeMax);
        });
    }

    private static GeospatialImportManifest LoadExampleManifest()
    {
        var diagnostics = new GeoDiagnosticBag();
        var manifest = ManifestLoader.Load(ExampleManifestPath(), diagnostics);

        Assert.NotNull(manifest);
        Assert.Equal(0, diagnostics.Items.Count(static item => item.Severity == GeoSeverity.Error));
        return manifest!;
    }

    private static string ExampleManifestPath([CallerFilePath] string sourcePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "Examples", "sedan-ign-proof.json");
}
