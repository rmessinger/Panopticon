#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline.GenerationPlanning;
using Panopticon.Geospatial.Pipeline.SourceLoading;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Sampling;
using Xunit;
using static Panopticon.Geospatial.Tests.Pipeline.PlanTestSupport;

namespace Panopticon.Geospatial.Tests.Pipeline;

public class ImportGenerationPlanTests
{
    [Fact]
    public void Create_KeepsSnapshotAndBuildsGridWithExpectedLattice()
    {
        var manifest = ImportManifest();
        var sources = Prepared(manifest, DefaultLoadedSources(), PolygonExtent(TmRectangle(1000d, 2000d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);
        Assert.Same(sources, plan!.Sources);
        Assert.Same(sources.LoadedManifest, plan.Manifest);
        Assert.Equal(sources.ManifestSha256, plan.ManifestSha256);
        Assert.IsType<WgsTransverseMercatorTransformer>(plan.Transformer);

        // Source equals target here, so the projected extent is the input rectangle and
        // every expected number is exact.
        var grid = plan.Grid;
        Assert.Equal(1, grid.ChunksX);
        Assert.Equal(1, grid.ChunksY);
        Assert.Equal(ExtentCrs, grid.ProjectedCoordinateSystem);
        Assert.Equal(new Vector2d(1000d, 2000d), grid.Transform.OriginEastingNorthing);
        Assert.Equal(10d * manifest.MetersPerGameUnit, grid.SampleToProjected(1, 0).X - grid.SampleToProjected(0, 0).X);

        Assert.Equal(new Vector2d(1000d, 2000d), grid.SampleToProjected(0, 0));
        Assert.Equal(new Vector2d(1010d, 2000d), grid.SampleToProjected(1, 0));
        Assert.Equal(new Vector2d(1000d, 2010d), grid.SampleToProjected(0, 1));
        Assert.Equal(new Vector2d(990d, 2000d), grid.SampleToProjected(-1, 0));

        // One chunk plus the one-sample apron reaches sample 129 on each axis.
        Assert.Equal(new Vector2d(2290d, 3290d), grid.SampleToProjected(129, 129));
        Assert.Equal(new Vector2d(0d, 0d), grid.ProjectedToGame(new Vector2d(1000d, 2000d)));

        var playable = grid.PlayableArea.OuterRing.Positions;
        Assert.Equal(1000d, playable[0].Coordinate.X);
        Assert.Equal(2000d, playable[0].Coordinate.Y);
        Assert.Equal(2000d, playable[2].Coordinate.X);
        Assert.Equal(3000d, playable[2].Coordinate.Y);
    }

    [Fact]
    public void Create_UsesSelectedPolygonNotTheOwningMultiGeometry()
    {
        var selected = TmRectangle(1000d, 2000d, 1000d, 1000d);
        var other = new GeoPointGeometry(new GeoPosition(new Vector2d(5.1d, 49.1d)));
        var extent = MultiExtent(selected, other);
        var manifest = ImportManifest();
        var sources = Prepared(manifest, DefaultLoadedSources(), extent);
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);
        Assert.IsType<GeoMultiGeometry>(sources.SelectedExtent.Feature.Geometry);
        Assert.Equal(new Vector2d(1000d, 2000d), plan!.Grid.Transform.OriginEastingNorthing);
        Assert.Equal(5, plan.Grid.PlayableArea.OuterRing.Positions.Count);
        Assert.Equal(
            selected.OuterRing.Positions.Select(static position => position.Coordinate),
            plan.Grid.PlayableArea.OuterRing.Positions.Select(static position => position.Coordinate));
    }

    [Fact]
    public void Create_BuildsDefaultFirstPaletteWithRuleOrderAndDuplicateOverride()
    {
        var materialRules = new List<MaterialRule>
        {
            new("rock", ClassificationValue: 2),
            new("forest", ClassificationValue: 1),
            new("rock", SourceId: "vectors", Attribute: "cover", Value: "rock"),
            new("sand", SourceId: "vectors", Attribute: "cover", Value: "sand"),
        };
        var featureMappings = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Geometry: ManifestFeatureGeometry.Polygon, Material: "path"),
            new("vectors", ManifestFeatureTarget.Landmark, Geometry: ManifestFeatureGeometry.Point),
            new("cover", ManifestFeatureTarget.Material, Geometry: ManifestFeatureGeometry.Polygon, Material: "forest"),
        };
        var manifest = ImportManifest(materialRules: materialRules, featureMappings: featureMappings);
        var sources = Prepared(manifest, DefaultLoadedSources(), PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);

        // Default first, material rules in list order, then material-target feature
        // mappings; duplicates keep their first position.
        Assert.Equal(["dirt", "rock", "forest", "sand", "path"], plan!.Palette.Names);
        Assert.Equal("dirt", plan.Palette.DefaultMaterial);
        Assert.Equal(0, plan.Palette.DefaultIndex);
        Assert.True(plan.Palette.TryGetIndex("forest", out var forest));
        Assert.Equal(2, forest);
        Assert.True(plan.Palette.TryGetIndex("path", out var path));
        Assert.Equal(4, path);
    }

    [Fact]
    public void Create_PaletteOver255Entries_FailsWithTypedCode()
    {
        var materialRules = Enumerable.Range(0, 256)
            .Select(static index => new MaterialRule($"material-{index}"))
            .ToList();
        var manifest = ImportManifest(materialRules: materialRules);
        var sources = Prepared(manifest, DefaultLoadedSources(), PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.Null(plan);
        var diagnostic = AssertSingleError(diagnostics, ImportGenerationPlanDiagnostics.PaletteInvalidCode);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Contains("255", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithoutImageryAndClassification_LeavesImageryResolutionNull()
    {
        var manifest = ImportManifest();
        var sources = Prepared(manifest, DefaultLoadedSources(), PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);
        Assert.Null(plan!.GridDefinition.ImageryPixelsPerSample);
        Assert.Equal(["extent", "dem"], plan.Provenance.Select(static source => source.SourceId));
    }

    [Fact]
    public void Create_WithImageryAndClassification_UsesManifestPixelsPerSample()
    {
        var manifest = ImportManifest(
            manifestSources:
            [
                .. DefaultManifestSources(),
                new ManifestSource("ortho", ManifestSourceKind.Geotiff, "inputs/ortho.dat", ManifestSourceRole.Imagery),
                new ManifestSource("cover", ManifestSourceKind.Geotiff, "inputs/cover.dat", ManifestSourceRole.Classification),
            ],
            rasterPolicy: new RasterPolicy(ImageryPixelsPerSample: 3));
        List<LoadedSource> loaded =
        [
            .. DefaultLoadedSources(),
            Source("ortho", ManifestSourceRole.Imagery, actualCoordinateSystem: CoordinateSystemId.Wgs84),
            Source("cover", ManifestSourceRole.Classification, actualCoordinateSystem: CoordinateSystemId.Wgs84),
        ];
        var sources = Prepared(manifest, loaded, PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);
        Assert.Equal(3, plan!.GridDefinition.ImageryPixelsPerSample);
        Assert.Equal(["extent", "dem", "ortho", "cover"], plan.Provenance.Select(static source => source.SourceId));
    }

    [Fact]
    public void Create_PayloadTransformKeepsAllActualCrsAndEffectiveDemDatum()
    {
        var manifest = ImportManifest(
            manifestSources:
            [
                .. DefaultManifestSources(),
                new ManifestSource("ortho", ManifestSourceKind.Geotiff, "inputs/ortho.dat", ManifestSourceRole.Imagery),
                new ManifestSource("vectors", ManifestSourceKind.Kmz, "inputs/features.dat", ManifestSourceRole.Vector),
            ],
            verticalPolicy: new VerticalPolicy(OriginMeters: 5d, Exaggeration: 2d));
        List<LoadedSource> loaded =
        [
            Source("extent", ManifestSourceRole.Extent, ManifestSourceKind.Kml),
            Source(
                "dem",
                ManifestSourceRole.Elevation,
                actualCoordinateSystem: CoordinateSystemId.Utm(31, northernHemisphere: true),
                embeddedVerticalDatum: "EGM96"),
            Source(
                "ortho",
                ManifestSourceRole.Imagery,
                actualCoordinateSystem: CoordinateSystemId.Wgs84,
                embeddedVerticalDatum: "EGM2008"),
            Source("vectors", ManifestSourceRole.Vector, ManifestSourceKind.Kmz, actualCoordinateSystem: CoordinateSystemId.Wgs84),
        ];
        var sources = Prepared(manifest, loaded, PolygonExtent(TmRectangle(1000d, 2000d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);

        // All distinct actual source CRSs in manifest order, unlike the grid's
        // extent-only list.
        var transform = plan!.GridDefinition.Transform;
        Assert.Equal(
            [ExtentCrs, CoordinateSystemId.Utm(31, northernHemisphere: true), CoordinateSystemId.Wgs84],
            transform.SourceCoordinateSystems);
        Assert.Equal([ExtentCrs], plan.Grid.Transform.SourceCoordinateSystems);

        // The manifest declared the DEM datum unknown; the embedded EGM96 reconciles the
        // payload datum while the grid keeps its manifest-only value.
        Assert.Equal("EGM96", transform.VerticalDatum);
        Assert.Equal("unknown", plan.Grid.Transform.VerticalDatum);

        // Grid origin, scales, and vertical policy stay unchanged.
        Assert.Equal(plan.Grid.Transform.OriginEastingNorthing, transform.OriginEastingNorthing);
        Assert.Equal(manifest.MetersPerGameUnit, transform.MetersPerGameUnit);
        Assert.Equal(5d, transform.VerticalOriginMeters);
        Assert.Equal(2d, transform.VerticalExaggeration);
        Assert.Equal(plan.Grid.ProjectedCoordinateSystem, transform.ProjectedCoordinateSystem);
    }

    [Fact]
    public void Create_OtherLayerDatum_DoesNotUpgradeDemDatum()
    {
        var manifest = ImportManifest(
            manifestSources:
            [
                .. DefaultManifestSources(),
                new ManifestSource("ortho", ManifestSourceKind.Geotiff, "inputs/ortho.dat", ManifestSourceRole.Imagery),
            ]);
        List<LoadedSource> loaded =
        [
            Source("extent", ManifestSourceRole.Extent, ManifestSourceKind.Kml),
            Source("dem", ManifestSourceRole.Elevation),
            Source(
                "ortho",
                ManifestSourceRole.Imagery,
                actualCoordinateSystem: CoordinateSystemId.Wgs84,
                embeddedVerticalDatum: "EGM2008"),
        ];
        var sources = Prepared(manifest, loaded, PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);
        Assert.Equal("unknown", plan!.GridDefinition.Transform.VerticalDatum);
        Assert.Equal("EGM2008", plan.Provenance.Single(source => source.SourceId == "ortho").EmbeddedVerticalDatum);
    }

    [Fact]
    public void Create_ProvenanceCopiesSnapshotInManifestOrder()
    {
        var manifest = ImportManifest();
        List<LoadedSource> loaded =
        [
            Source("extent", ManifestSourceRole.Extent, ManifestSourceKind.Kml, label: "Play area"),
            Source(
                "dem",
                ManifestSourceRole.Elevation,
                actualCoordinateSystem: CoordinateSystemId.Utm(31, northernHemisphere: true),
                declaredVerticalDatum: "EGM96",
                embeddedVerticalDatum: "EGM96",
                label: "DEM",
                attribution: "operator survey",
                horizontalAccuracyMeters: 0.6,
                verticalAccuracyMeters: 1.5),
        ];
        var sources = Prepared(manifest, loaded, PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);

        var dem = Assert.Single(plan!.Provenance, static source => source.SourceId == "dem");
        Assert.Equal(ManifestSourceKind.Geotiff, dem.Kind);
        Assert.Equal(ManifestSourceRole.Elevation, dem.Role);
        Assert.Equal("inputs/dem.dat", dem.RelativePath);
        Assert.Equal(Sha256("dem"), dem.Sha256Hex);
        Assert.Equal(1000L + "dem".Length, dem.LengthBytes);
        Assert.Equal(CoordinateSystemId.Utm(31, northernHemisphere: true), dem.ActualCoordinateSystem);
        Assert.Equal("EGM96", dem.DeclaredVerticalDatum);
        Assert.Equal("EGM96", dem.EmbeddedVerticalDatum);
        Assert.Equal("DEM", dem.Label);
        Assert.Equal("operator survey", dem.Attribution);
        Assert.Equal(0.6, dem.HorizontalAccuracyMeters);
        Assert.Equal(1.5, dem.VerticalAccuracyMeters);

        var collection = Assert.IsAssignableFrom<ICollection<PayloadSourceProvenance>>(plan.Provenance);
        Assert.True(collection.IsReadOnly);
    }

    [Fact]
    public void Create_DerivesSamplingOptionsAndSlopeThresholdsFromManifest()
    {
        var manifest = ImportManifest(
            rasterPolicy: new RasterPolicy(
                NoData: ManifestNoDataPolicy.NearestValid,
                NoDataSearchRadiusMeters: 12.5,
                Edge: ManifestEdgePolicy.Constant,
                EdgeConstantMeters: -3d,
                ImageryPixelsPerSample: 2),
            slopePolicy: new SlopePolicy(ImpassableSlopeDegrees: 60, UnbuildableSlopeDegrees: 30));
        var sources = Prepared(manifest, DefaultLoadedSources(), PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);
        Assert.Equal(NoDataPolicy.NearestValid, plan!.SamplingOptions.NoData);
        Assert.Equal(12.5, plan.SamplingOptions.NoDataSearchRadiusMeters);
        Assert.Null(plan.SamplingOptions.NoDataConstantMeters);
        Assert.Equal(EdgePolicy.Constant, plan.SamplingOptions.Edge);
        Assert.Equal(-3d, plan.SamplingOptions.EdgeConstantMeters);
        Assert.Null(plan.SamplingOptions.EdgeSearchRadiusMeters);
        Assert.Equal(60d, plan.SlopeThresholds.ImpassableDegrees);
        Assert.Equal(30d, plan.SlopeThresholds.UnbuildableDegrees);
        Assert.Equal(60d, plan.GridDefinition.SlopeThresholds.ImpassableDegrees);
        Assert.Equal(30d, plan.GridDefinition.SlopeThresholds.UnbuildableDegrees);
    }

    [Fact]
    public void Create_InvalidSlopeThresholds_FailsWithTypedCode()
    {
        var manifest = ImportManifest(slopePolicy: new SlopePolicy(ImpassableSlopeDegrees: 91));
        var sources = Prepared(manifest, DefaultLoadedSources(), PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.Null(plan);
        AssertSingleError(diagnostics, ImportGenerationPlanDiagnostics.SlopeThresholdsInvalidCode);
    }

    [Fact]
    public void Create_InconsistentRasterPolicy_FailsWithTypedCode()
    {
        var manifest = ImportManifest(rasterPolicy: new RasterPolicy(NoData: ManifestNoDataPolicy.NearestValid));
        var sources = Prepared(manifest, DefaultLoadedSources(), PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.Null(plan);
        AssertSingleError(diagnostics, ImportGenerationPlanDiagnostics.SamplingOptionsInvalidCode);
    }

    [Fact]
    public void Create_InvalidImageryPixelsPerSample_FailsWithTypedCode()
    {
        var manifest = ImportManifest(
            manifestSources:
            [
                .. DefaultManifestSources(),
                new ManifestSource("ortho", ManifestSourceKind.Geotiff, "inputs/ortho.dat", ManifestSourceRole.Imagery),
            ],
            rasterPolicy: new RasterPolicy(ImageryPixelsPerSample: 0));
        List<LoadedSource> loaded =
        [
            .. DefaultLoadedSources(),
            Source("ortho", ManifestSourceRole.Imagery, actualCoordinateSystem: CoordinateSystemId.Wgs84),
        ];
        var sources = Prepared(manifest, loaded, PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.Null(plan);
        AssertSingleError(diagnostics, ImportGenerationPlanDiagnostics.PayloadGridInvalidCode);
    }

    [Fact]
    public void Create_CancelledToken_Throws()
    {
        var manifest = ImportManifest();
        var sources = Prepared(manifest, DefaultLoadedSources(), PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => ImportGenerationPlan.Create(sources, new GeoDiagnosticBag(), cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Create_NormalizesProvenancePathDroppingDotAndEmptySegments()
    {
        var manifest = ImportManifest();
        List<LoadedSource> loaded =
        [
            Source("extent", ManifestSourceRole.Extent, ManifestSourceKind.Kml),
            Source(
                "dem",
                ManifestSourceRole.Elevation,
                embeddedVerticalDatum: "EGM96",
                label: "DEM",
                relativePath: "./rasters//dem.tif"),
        ];
        var sources = Prepared(manifest, loaded, PolygonExtent(TmRectangle(0d, 0d, 1000d, 1000d)));
        var diagnostics = new GeoDiagnosticBag();

        var plan = ImportGenerationPlan.Create(sources, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(plan);
        var dem = Assert.Single(plan!.Provenance, static source => source.SourceId == "dem");
        Assert.Equal("rasters/dem.tif", dem.RelativePath);
        Assert.Equal(Sha256("dem"), dem.Sha256Hex);
        Assert.Equal("DEM", dem.Label);
        Assert.Equal("EGM96", dem.EmbeddedVerticalDatum);

        // Only the metadata representation is normalized; the snapshot keeps the
        // manifest's spelling.
        Assert.Equal("./rasters//dem.tif", sources.Sources.Single(static source => source.SourceId == "dem").RelativePath);
        Assert.Same(manifest, plan.Manifest);
    }

    private static GeoDiagnostic AssertSingleError(GeoDiagnosticBag diagnostics, string code)
    {
        var diagnostic = Assert.Single(diagnostics.Items, static item => item.Severity == GeoSeverity.Error);
        Assert.Equal(code, diagnostic.Code);

        return diagnostic;
    }
}
