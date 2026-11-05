#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline.Validation;
using Panopticon.Geospatial.Tests.Pipeline;
using Xunit;
using static Panopticon.Geospatial.Tests.Validation.ControlPointTestSupport;

namespace Panopticon.Geospatial.Tests.Validation;

public class ControlPointValidatorTests
{
    [Fact]
    public void Validate_PointOnPlane_ProducesSixOrderedChecksWithAnalyticValues()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);
        RewriteManifest(
            project,
            PointJson("cp-on-plane", expectedGameX: GameX, expectedGameY: GameY, imageryRgb: [10, 10, 60], imageryToleranceBytes: 5));
        var manifest = LoadManifest(project);

        using (reader)
        {
            var diagnostics = new GeoDiagnosticBag();
            var checks = Validate(reader, manifest, diagnostics);

            Assert.False(diagnostics.HasErrors);
            Assert.Equal(
                ["cp-on-plane/horizontal", "cp-on-plane/absolute-vertical", "cp-on-plane/relative-vertical", "cp-on-plane/red", "cp-on-plane/green", "cp-on-plane/blue"],
                checks.Select(static check => check.Name).ToArray());

            var horizontal = Check(checks, "cp-on-plane/horizontal");
            Assert.Equal(PayloadValidationStatus.Passed, horizontal.Status);
            Assert.Equal(10d, horizontal.Tolerance);
            Assert.Equal("meters", horizontal.Units);
            Assert.True(horizontal.Actual < 0.001d, $"distance {horizontal.Actual}");

            // Known EGM96 payload datum: the DEM's stated 1.5 m accuracy governs.
            var absolute = Check(checks, "cp-on-plane/absolute-vertical");
            Assert.Equal(PayloadValidationStatus.Passed, absolute.Status);
            Assert.Equal(1.5d, absolute.Tolerance);
            Assert.Equal(13.15d, absolute.Expected!.Value, 3);
            Assert.Equal(13.15d, absolute.Actual!.Value, 3);

            // No explicit relative tolerance declared.
            Assert.Equal(PayloadValidationStatus.NotEvaluated, Check(checks, "cp-on-plane/relative-vertical").Status);

            Assert.All(
                [Check(checks, "cp-on-plane/red"), Check(checks, "cp-on-plane/green"), Check(checks, "cp-on-plane/blue")],
                static check =>
                {
                    Assert.Equal(PayloadValidationStatus.Passed, check.Status);
                    Assert.Equal(5d, check.Tolerance);
                    Assert.Equal("bytes", check.Units);
                });
            Assert.Equal(10d, Check(checks, "cp-on-plane/red").Expected);
            Assert.Equal(60d, Check(checks, "cp-on-plane/blue").Actual);
        }
    }

    [Fact]
    public void Validate_NoIndependentTruth_MarksHorizontalNotEvaluated()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);
        RewriteManifest(project, PointJson("cp-free", elevationMeters: null, imageryRgb: null));
        var manifest = LoadManifest(project);

        using (reader)
        {
            var checks = Validate(reader, manifest, new GeoDiagnosticBag());

            Assert.Equal(PayloadValidationStatus.NotEvaluated, Check(checks, "cp-free/horizontal").Status);
            Assert.Equal(PayloadValidationStatus.NotEvaluated, Check(checks, "cp-free/absolute-vertical").Status);
            Assert.Equal(PayloadValidationStatus.NotEvaluated, Check(checks, "cp-free/relative-vertical").Status);
            Assert.Equal(PayloadValidationStatus.NotEvaluated, Check(checks, "cp-free/red").Status);
        }
    }

    [Fact]
    public void Validate_FeatureTruth_MissingNonPointAndScopedMissingFailButFoundPasses()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);
        RewriteManifest(
            project,
            string.Join(
                ", ",
                PointJson("cp-feature", elevationMeters: null, imageryRgb: null, featureId: "wp"),
                PointJson("cp-missing", elevationMeters: null, imageryRgb: null, featureId: "no-such-feature"),
                PointJson("cp-nonpoint", elevationMeters: null, imageryRgb: null, featureId: "keepout"),
                PointJson("cp-scoped", elevationMeters: null, imageryRgb: null, featureId: "wp", featureSourceId: "extent")));
        var manifest = LoadManifest(project);

        using (reader)
        {
            var checks = Validate(reader, manifest, new GeoDiagnosticBag());
            var found = Check(checks, "cp-feature/horizontal");

            // The waypoint sits at game (100, 100); the control point at (105, 105) is
            // sqrt(50) m away, inside the 10 m spacing tolerance. The vector source
            // declares no accuracy, so spacing governs.
            Assert.Equal(PayloadValidationStatus.Passed, found.Status);
            Assert.Equal(10d, found.Tolerance);
            Assert.Equal(Math.Sqrt(50d), found.Actual!.Value, 3);

            var missing = Check(checks, "cp-missing/horizontal");
            Assert.Equal(PayloadValidationStatus.Failed, missing.Status);
            Assert.Contains("no generated feature matches feature id 'no-such-feature'", missing.Message);

            var nonPoint = Check(checks, "cp-nonpoint/horizontal");
            Assert.Equal(PayloadValidationStatus.Failed, nonPoint.Status);
            Assert.Contains("is not a point feature", nonPoint.Message);

            var scoped = Check(checks, "cp-scoped/horizontal");
            Assert.Equal(PayloadValidationStatus.Failed, scoped.Status);
            Assert.Contains("feature id 'wp' in source 'extent'", scoped.Message);
        }
    }

    [Fact]
    public void Validate_FeatureTruthUsesMatchedSourceAccuracy_NotDemAccuracy()
    {
        using var project = ImportProjectFixture.Create();
        var baseManifest = File.ReadAllText(project.ManifestPath);
        var (reader, _) = ImportAndOpen(project);

        // One displacement, two truths: the waypoint feature at (100, 100) and an
        // explicit expected game XY, both 84.853 m from the control point at
        // (160, 160). Which source's horizontal accuracy applies must flip with the
        // truth kind, not with the DEM alone.
        var controlPoints = string.Join(
            ", ",
            PointJson("cp-feature", elevationMeters: null, imageryRgb: null, featureId: "wp", gameX: 160d, gameY: 160d),
            PointJson("cp-feature-scoped", elevationMeters: null, imageryRgb: null, featureId: "wp", featureSourceId: "vectors", gameX: 160d, gameY: 160d),
            PointJson("cp-xy", elevationMeters: null, imageryRgb: null, expectedGameX: 100d, expectedGameY: 100d, gameX: 160d, gameY: 160d));
        var variants = new[]
        {
            (Dem: 0.1d, Vector: 100d, Feature: PayloadValidationStatus.Passed, FeatureTolerance: 100d, Explicit: PayloadValidationStatus.Failed, ExplicitTolerance: 10d),
            (Dem: 100d, Vector: 0.1d, Feature: PayloadValidationStatus.Failed, FeatureTolerance: 10d, Explicit: PayloadValidationStatus.Passed, ExplicitTolerance: 100d),
        };

        using (reader)
        {
            foreach (var (demAccuracy, vectorAccuracy, featureStatus, featureTolerance, explicitStatus, explicitTolerance) in variants)
            {
                File.WriteAllText(project.ManifestPath, baseManifest);
                RewriteManifest(project, controlPoints, demHorizontalAccuracy: demAccuracy, vectorHorizontalAccuracy: vectorAccuracy);
                var manifest = LoadManifest(project);
                var checks = Validate(reader, manifest, new GeoDiagnosticBag());
                var feature = Check(checks, "cp-feature/horizontal");

                Assert.Equal(featureStatus, feature.Status);
                Assert.Equal(featureTolerance, feature.Tolerance);
                Assert.Equal(Math.Sqrt(2d * 60d * 60d), feature.Actual!.Value, 3);

                // The implicit unique match resolves the same source the explicit
                // featureSourceId scopes.
                var scoped = Check(checks, "cp-feature-scoped/horizontal");
                Assert.Equal(featureStatus, scoped.Status);
                Assert.Equal(featureTolerance, scoped.Tolerance);

                var explicitCheck = Check(checks, "cp-xy/horizontal");
                Assert.Equal(explicitStatus, explicitCheck.Status);
                Assert.Equal(explicitTolerance, explicitCheck.Tolerance);
                Assert.Equal(Math.Sqrt(2d * 60d * 60d), explicitCheck.Actual!.Value, 3);
            }
        }
    }

    [Fact]
    public void Validate_AmbiguousFeatureMatch_FailsInsteadOfFirstMatch()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);
        RewriteManifest(project, PointJson("cp-ambiguous", elevationMeters: null, imageryRgb: null, featureId: "wp"));
        var manifest = LoadManifest(project);

        using (reader)
        {
            var waypoint = reader.Metadata.Features.Single(static feature => feature.FeatureId == "wp");
            var duplicated = reader.Metadata.Features.Concat(
                [
                    new GeneratedFeature(
                        waypoint.Kind,
                        waypoint.Geometry,
                        waypoint.SourceId,
                        waypoint.FeatureId,
                        "vectors/wp#duplicate",
                        waypoint.RuleIndex,
                        waypoint.Attributes,
                        waypoint.Position),
                ]);
            var metadata = new GeneratedMapPayloadMetadata(
                reader.Metadata.Grid,
                reader.Metadata.Palette,
                [.. duplicated],
                reader.Metadata.ManifestSha256Hex,
                reader.Metadata.Provenance,
                reader.Metadata.Chunks);
            using var duplicatedReader = new ReaderWithMetadata(reader, metadata);

            var checks = Validate(duplicatedReader, manifest, new GeoDiagnosticBag());
            var horizontal = Check(checks, "cp-ambiguous/horizontal");

            Assert.Equal(PayloadValidationStatus.Failed, horizontal.Status);
            Assert.Contains("multiple generated features match feature id 'wp'", horizontal.Message);
        }
    }

    [Fact]
    public void Validate_UnknownPayloadDatum_AbsoluteNotEvaluatedRelativeAgainstOrigin()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);
        RewriteManifest(project, PointJson("cp-unknown-datum"), tolerances: "{ \"relativeVerticalMeters\": 2 }");
        var manifest = LoadManifest(project);

        using (reader)
        {
            var grid = reader.Metadata.Grid;
            var unknownDatum = new PayloadGridDefinition(
                grid.ChunksX,
                grid.ChunksY,
                new GeospatialMapTransform(
                    grid.Transform.SourceCoordinateSystems,
                    grid.Transform.ProjectedCoordinateSystem,
                    grid.Transform.OriginEastingNorthing,
                    grid.Transform.MetersPerGameUnit,
                    "unknown",
                    grid.Transform.VerticalOriginMeters,
                    grid.Transform.VerticalExaggeration),
                grid.PlayableArea,
                grid.ImageryPixelsPerSample,
                grid.SlopeThresholds);
            var metadata = new GeneratedMapPayloadMetadata(
                unknownDatum,
                reader.Metadata.Palette,
                reader.Metadata.Features,
                reader.Metadata.ManifestSha256Hex,
                reader.Metadata.Provenance,
                reader.Metadata.Chunks);
            using var unknownReader = new ReaderWithMetadata(reader, metadata);

            var checks = Validate(unknownReader, manifest, new GeoDiagnosticBag());

            Assert.Equal(PayloadValidationStatus.NotEvaluated, Check(checks, "cp-unknown-datum/absolute-vertical").Status);
            var relative = Check(checks, "cp-unknown-datum/relative-vertical");

            Assert.Equal(PayloadValidationStatus.Passed, relative.Status);
            Assert.Equal(2d, relative.Tolerance);
            Assert.Equal(13.15d, relative.Expected!.Value, 3);
            Assert.Equal(13.15d, relative.Actual!.Value, 3);
        }
    }

    [Fact]
    public void Validate_UpgradedKnownDatumWithoutAbsoluteTolerance_FailsInsteadOfBorrowingRelative()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);

        // Declared datum unknown (no stated accuracy, no verticalMeters required at
        // load), while the GeoTIFF embeds EGM96: the payload's actual datum is known,
        // so the requested absolute comparison must fail for missing tolerance.
        RewriteManifest(
            project,
            PointJson("cp-upgraded"),
            tolerances: "{ \"relativeVerticalMeters\": 2 }",
            demDatumUnknown: true);
        var manifest = LoadManifest(project);

        using (reader)
        {
            var checks = Validate(reader, manifest, new GeoDiagnosticBag());
            var absolute = Check(checks, "cp-upgraded/absolute-vertical");

            Assert.Equal(PayloadValidationStatus.Failed, absolute.Status);
            Assert.Contains("missing its tolerance", absolute.Message);
            Assert.Equal(PayloadValidationStatus.Passed, Check(checks, "cp-upgraded/relative-vertical").Status);
        }
    }

    [Fact]
    public void Validate_TolerancePrecedence_SourceAccuracyAndExplicitThresholdsGovern()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);
        RewriteManifest(
            project,
            string.Join(
                ", ",
                PointJson("cp-far-but-explicit", expectedGameX: 130d, expectedGameY: 130d),
                PointJson("cp-beyond-explicit", expectedGameX: 200d, expectedGameY: 200d),
                PointJson("cp-slightly-off", elevationMeters: 13.65d),
                PointJson("cp-far-off", elevationMeters: 20d)),
            tolerances: "{ \"horizontalMeters\": 50, \"verticalMeters\": 0.2 }");
        var manifest = LoadManifest(project);

        using (reader)
        {
            var checks = Validate(reader, manifest, new GeoDiagnosticBag());

            // 35.36 m displacement passes only through the explicit 50 m operator
            // tolerance, which outranks the 10 m default spacing bound; a 134 m
            // displacement fails even against 50 m.
            var far = Check(checks, "cp-far-but-explicit/horizontal");

            Assert.Equal(PayloadValidationStatus.Passed, far.Status);
            Assert.Equal(50d, far.Tolerance);
            Assert.Equal(Math.Sqrt(2d * 25d * 25d), far.Actual!.Value, 3);
            var beyond = Check(checks, "cp-beyond-explicit/horizontal");
            Assert.Equal(PayloadValidationStatus.Failed, beyond.Status);
            Assert.Equal(50d, beyond.Tolerance);

            // The DEM's stated 1.5 m accuracy outranks the explicit 0.2 m value: a
            // 0.5 m deviation passes, a 6.85 m deviation fails, both against 1.5 m.
            var slight = Check(checks, "cp-slightly-off/absolute-vertical");

            Assert.Equal(PayloadValidationStatus.Passed, slight.Status);
            Assert.Equal(1.5d, slight.Tolerance);
            var farOff = Check(checks, "cp-far-off/absolute-vertical");
            Assert.Equal(PayloadValidationStatus.Failed, farOff.Status);
            Assert.Equal(1.5d, farOff.Tolerance);
            Assert.Contains("beyond the 1.5 m tolerance", farOff.Message);
        }
    }

    [Fact]
    public void Validate_ImageryChannelsIndependent_NoImageryLayerAndOffMapFail()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);
        RewriteManifest(
            project,
            string.Join(", ", PointJson("cp-rgb", imageryRgb: [200, 10, 60], imageryToleranceBytes: 5), PointJson("cp-no-rgb", imageryRgb: null)));
        var manifest = LoadManifest(project);

        using (reader)
        {
            var checks = Validate(reader, manifest, new GeoDiagnosticBag());

            Assert.Equal(PayloadValidationStatus.Failed, Check(checks, "cp-rgb/red").Status);
            Assert.Equal(PayloadValidationStatus.Passed, Check(checks, "cp-rgb/green").Status);
            Assert.Equal(PayloadValidationStatus.Passed, Check(checks, "cp-rgb/blue").Status);
            Assert.Equal(200d, Check(checks, "cp-rgb/red").Expected);
            Assert.Equal(10d, Check(checks, "cp-rgb/red").Actual);
            Assert.Equal(PayloadValidationStatus.NotEvaluated, Check(checks, "cp-no-rgb/red").Status);
        }

        using var bare = ImportProjectFixture.Create(imagery: false);
        var (readerWithoutImagery, _) = ImportAndOpen(bare);
        RewriteManifest(bare, PointJson("cp-wants-rgb", imageryRgb: [10, 10, 60], imageryToleranceBytes: 5));
        var noImageryManifest = LoadManifest(bare);

        using (readerWithoutImagery)
        {
            var checks = Validate(readerWithoutImagery, noImageryManifest, new GeoDiagnosticBag());

            Assert.All(
                [Check(checks, "cp-wants-rgb/red"), Check(checks, "cp-wants-rgb/green"), Check(checks, "cp-wants-rgb/blue")],
                static check =>
                {
                    Assert.Equal(PayloadValidationStatus.Failed, check.Status);
                    Assert.Contains("no imagery layer", check.Message);
                });
        }
    }

    [Fact]
    public void Validate_OffMapPoint_FailsApplicableChecksNotSilentPass()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);

        // A finite control position far outside the map rectangle: the transform
        // succeeds but terrain and imagery cannot be sampled there.
        var offMap = PointJson("cp-off-map", expectedGameX: GameX, expectedGameY: GameY, imageryRgb: [10, 10, 60], imageryToleranceBytes: 5)
            .Replace($"\"latitude\": {LatitudeOfPoint()}", "\"latitude\": -45")
            .Replace($"\"longitude\": {LongitudeOfPoint()}", "\"longitude\": 145");
        RewriteManifest(project, offMap);
        var manifest = LoadManifest(project);

        using (reader)
        {
            var checks = Validate(reader, manifest, new GeoDiagnosticBag());

            Assert.Equal(PayloadValidationStatus.Failed, Check(checks, "cp-off-map/horizontal").Status);
            Assert.Equal(PayloadValidationStatus.Failed, Check(checks, "cp-off-map/absolute-vertical").Status);
            Assert.Equal(PayloadValidationStatus.Failed, Check(checks, "cp-off-map/relative-vertical").Status);
            Assert.Equal(PayloadValidationStatus.Failed, Check(checks, "cp-off-map/red").Status);
        }
    }

    [Fact]
    public void Validate_OwnCheckBudgetEnforcedBeforeAllocation()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);
        RewriteManifest(
            project,
            string.Join(", ", PointJson("cp-a", elevationMeters: null, imageryRgb: null), PointJson("cp-b", elevationMeters: null, imageryRgb: null)));
        var manifest = LoadManifest(project);

        using (reader)
        {
            // Two points produce twelve checks; eleven leaves no room for even one.
            var tight = Validate(reader, manifest, new GeoDiagnosticBag(), new GeospatialImportLimits { MaxFeatureCount = 11 });

            Assert.Empty(tight);
            var diagnostics = new GeoDiagnosticBag();
            Validate(reader, manifest, diagnostics, new GeospatialImportLimits { MaxFeatureCount = 11 });
            var error = Assert.Single(diagnostics.Items);
            Assert.Equal(GeoDiagnosticCodes.ValidateFailure, error.Code);
            Assert.Equal(GeoStage.Validate, error.Stage);
            Assert.Contains("maxFeatureCount", error.Message);

            var exact = Validate(reader, manifest, new GeoDiagnosticBag(), new GeospatialImportLimits { MaxFeatureCount = 12 });
            Assert.Equal(12, exact.Count);
        }
    }

    [Fact]
    public void Validate_CancelledToken_Throws()
    {
        using var project = ImportProjectFixture.Create();
        var (reader, _) = ImportAndOpen(project);
        RewriteManifest(project, PointJson("cp-cancel"));
        var manifest = LoadManifest(project);

        using (reader)
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => Validate(reader, manifest, new GeoDiagnosticBag(), cancellationToken: cancelled.Token));
        }
    }

    private static string LatitudeOfPoint()
    {
        return ImportProjectFixture.LonLat(GameX, GameY).Split(',')[1];
    }

    private static string LongitudeOfPoint()
    {
        return ImportProjectFixture.LonLat(GameX, GameY).Split(',')[0];
    }
}
