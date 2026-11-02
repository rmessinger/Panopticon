#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Projection;
using Xunit;

namespace Panopticon.Geospatial.Tests.Grid;

public class LocalMapGridTests
{
    // Decimal places kept when comparing projected meters; the projection round-trips
    // within nanometers, so micrometer agreement is exact for this purpose.
    private const int MeterPrecision = 6;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private static readonly CoordinateSystemId Tm4 = CoordinateSystemId.TransverseMercator(4d, 1d, 0d, 0d);

    private static readonly CoordinateSystemId Wgs84 = CoordinateSystemId.Wgs84;

    private static readonly CoordinateSystemId Utm31North = CoordinateSystemId.Utm(31, northernHemisphere: true);

    [Fact]
    public void OneChunkSquareAtAnArbitraryProjectedPosition_YieldsExactlyOneChunk()
    {
        // A square spanning exactly the chunk span, placed at coordinates that are neither
        // chunk aligned nor round numbers; the origin must stay at the projected southwest
        // corner instead of being rounded onto any global lattice.
        var extent = ProjectedSquare(12327.4d, 5432109.7d, 1280d, 1280d);
        var grid = CreateGrid(extent, Tm4);

        Assert.NotNull(grid);
        Assert.Equal(1, grid!.ChunksX);
        Assert.Equal(1, grid.ChunksY);

        var origin = grid.Transform.OriginEastingNorthing;

        Assert.Equal(12327.4d, origin.X);
        Assert.Equal(5432109.7d, origin.Y);
        Assert.Equal(origin, grid.SampleToProjected(0, 0));
        Assert.Equal(13607.4d, grid.SampleToProjected(128, 0).X, MeterPrecision);
        Assert.Equal(5433389.7d, grid.SampleToProjected(0, 128).Y, MeterPrecision);
        Assert.Equal(Tm4, grid.ProjectedCoordinateSystem);
        Assert.Equal(new[] { Tm4 }, grid.Transform.SourceCoordinateSystems);
        AssertRingsEqual(extent.OuterRing, grid.PlayableArea.OuterRing);
    }

    [Fact]
    public void Wgs84RoundTripSquareRoundsOutwardOnTheAxisBeyondTheUlpBudget()
    {
        // The extent is the WGS84 image of a 1280 m projected square. Inverse-then-forward
        // carries a real numeric error of a few nanometers. On easting, at magnitude 1.2e4,
        // the coordinate ULP budget is picometers, so the error is real coverage and rounds
        // outward; on northing, at magnitude 5.4e6, the same error stays inside the budget.
        var projection = new TransverseMercatorProjection(4d, 1d, 0d, 0d);
        var extent = GeographicImageOfSquare(projection, 12327.4d, 5432109.7d, 1280d, 1280d);
        var grid = CreateGrid(extent, Wgs84);

        Assert.NotNull(grid);
        Assert.Equal(2, grid!.ChunksX);
        Assert.Equal(1, grid.ChunksY);
        Assert.Equal(12327.4d, grid.Transform.OriginEastingNorthing.X, MeterPrecision);
        Assert.Equal(5432109.7d, grid.Transform.OriginEastingNorthing.Y, MeterPrecision);
        Assert.Equal(grid.Transform.OriginEastingNorthing, grid.SampleToProjected(0, 0));
    }

    [Theory]
    [InlineData(1280d, 1)]
    [InlineData(1280.000001d, 2)]
    [InlineData(1280.00000128d, 2)]
    [InlineData(1280.00128d, 2)]
    [InlineData(2560d, 2)]
    [InlineData(2561d, 3)]
    public void ChunkRounding_RoundsOutwardBeyondTheUlpBudget(double widthMeters, int expectedChunks)
    {
        // Only floating-point round-off (a few ULPs) may snap down to the nearest whole
        // chunk count; micrometers of real span round outward.
        var extent = ProjectedSquare(100000d, 6000000d, widthMeters, 1280d);
        var grid = CreateGrid(extent, Tm4);

        Assert.NotNull(grid);
        Assert.Equal(expectedChunks, grid!.ChunksX);
        Assert.Equal(1, grid.ChunksY);
    }

    [Fact]
    public void WidthOffByOneCoordinateUlp_StillCountsAsTheWholeChunks()
    {
        // One ULP of the projected coordinates is inside the round-off budget.
        var exact = 100000d + 1280d;
        var extent = ProjectedSquare(100000d, 6000000d, Math.BitIncrement(exact) - 100000d, 1280d);
        var grid = CreateGrid(extent, Tm4);

        Assert.NotNull(grid);
        Assert.Equal(1, grid!.ChunksX);
        Assert.Equal(1, grid.ChunksY);
    }

    [Fact]
    public void LargeRatiosSnapOnlyWithinUlpBudgetAndRoundRealSubSpansOutward()
    {
        // One axis at a million chunks: at that count a single ULP of the ratio is far
        // below any real distance, so an extra micrometer must add a chunk, not truncate
        // 12.8 m the way a relative epsilon would. The extent is a sliver one chunk tall.
        var limits = new GeospatialImportLimits(MaxChunkCount: 2_000_000);

        var exact = CreateGrid(ProjectedSquare(100000d, 6000000d, 1280d, 0.001d), Tm4, metersPerGameUnit: 1e-6d, limits: limits);
        Assert.NotNull(exact);
        Assert.Equal(1_000_000, exact!.ChunksX);
        Assert.Equal(1, exact.ChunksY);

        var micrometerLonger = CreateGrid(ProjectedSquare(100000d, 6000000d, 1280.000001d, 0.001d), Tm4, metersPerGameUnit: 1e-6d, limits: limits);
        Assert.NotNull(micrometerLonger);
        Assert.Equal(1_000_001, micrometerLonger!.ChunksX);
        Assert.Equal(1, micrometerLonger.ChunksY);
    }

    [Fact]
    public void MetersPerGameUnitScalesChunkSpanAndSampleSpacing()
    {
        var extent = ProjectedSquare(100000d, 6000000d, 2560d, 2560d);

        var natural = CreateGrid(extent, Tm4, metersPerGameUnit: 1d);
        Assert.Equal(2, natural!.ChunksX);
        Assert.Equal(2, natural.ChunksY);
        Assert.Equal(10d, natural.SampleToProjected(1, 0).X - natural.Transform.OriginEastingNorthing.X);

        var coarse = CreateGrid(extent, Tm4, metersPerGameUnit: 2d);
        Assert.Equal(1, coarse!.ChunksX);
        Assert.Equal(1, coarse.ChunksY);
        Assert.Equal(20d, coarse.SampleToProjected(1, 0).X - coarse.Transform.OriginEastingNorthing.X);
        Assert.Equal(
            coarse.Transform.OriginEastingNorthing.X + 2560d,
            coarse.SampleToProjected(128, 128).X,
            MeterPrecision);

        var fine = CreateGrid(extent, Tm4, metersPerGameUnit: 0.5d);
        Assert.Equal(4, fine!.ChunksX);
        Assert.Equal(4, fine.ChunksY);
        Assert.Equal(5d, fine.SampleToProjected(1, 0).X - fine.Transform.OriginEastingNorthing.X);
    }

    [Fact]
    public void EastingsAndNorthingsMapToPositiveGameXYFromTheSouthwestOrigin()
    {
        var extent = ProjectedSquare(123456.75d, 7654321.25d, 1000d, 800d);
        var grid = CreateGrid(extent, Tm4, metersPerGameUnit: 10d);

        Assert.NotNull(grid);
        var origin = grid!.Transform.OriginEastingNorthing;
        Assert.Equal(123456.75d, origin.X);
        Assert.Equal(7654321.25d, origin.Y);

        Assert.Equal(new Vector2d(0d, 0d), grid.ProjectedToGame(origin));
        Assert.Equal(new Vector2d(10d, 5d), grid.ProjectedToGame(new Vector2d(origin.X + 100d, origin.Y + 50d)));
        Assert.Equal(new Vector2d(50d, 30d), grid.ProjectedToGame(grid.SampleToProjected(5, 3)));
        Assert.True(grid.SampleToProjected(1, 0).X > origin.X, "Sample +x must increase easting.");
        Assert.True(grid.SampleToProjected(0, 1).Y > origin.Y, "Sample +y must increase northing.");
    }

    [Fact]
    public void ApronSamplesOnBothSidesAreAcceptedAndBeyondThemRejected()
    {
        var extent = ProjectedSquare(100000d, 6000000d, 1280d, 1280d);
        var grid = CreateGrid(extent, Tm4);

        Assert.NotNull(grid);
        var origin = grid!.Transform.OriginEastingNorthing;

        Assert.Equal(new Vector2d(origin.X - 10d, origin.Y - 10d), grid.SampleToProjected(-1, -1));
        Assert.Equal(new Vector2d(origin.X + 1290d, origin.Y + 1290d), grid.SampleToProjected(129, 129));

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SampleToProjected(-2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SampleToProjected(130, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SampleToProjected(0, -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.SampleToProjected(0, 130));
    }

    [Fact]
    public void DefaultCentralMeridianIsBboxCentreRoundedAwayFromZeroToHundredths()
    {
        var defaultTarget = new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator);

        // Centre longitude 4.517 rounds to 4.52.
        var extent = GeographicRectangle(1.5d, 49.5d, 7.534d, 50.5d);
        var grid = CreateGrid(extent, Wgs84, target: defaultTarget);

        Assert.NotNull(grid);
        Assert.Equal(4.52d, grid!.ProjectedCoordinateSystem.CentralMeridianDegrees);
        Assert.Null(grid.ProjectedCoordinateSystem.EpsgCode);

        var negative = CreateGrid(GeographicRectangle(-5d, 49.5d, -1.512d, 50.5d), Wgs84, target: defaultTarget);

        Assert.NotNull(negative);
        Assert.Equal(-3.26d, negative!.ProjectedCoordinateSystem.CentralMeridianDegrees);
    }

    [Fact]
    public void WesternFrontExtent_PassesWithCustomTm_AndFailsTheScaleCheckWithUtm31()
    {
        var extent = GeographicRectangle(1.5d, 49.5d, 7.5d, 50.5d);

        var custom = CreateGrid(extent, Wgs84, target: new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator));

        Assert.NotNull(custom);
        Assert.Equal(4.5d, custom!.ProjectedCoordinateSystem.CentralMeridianDegrees);
        var bounds = ProjectedBoundsOf(extent, Wgs84, custom.ProjectedCoordinateSystem);
        Assert.Equal(
            (int)Math.Ceiling((bounds.maxEasting - bounds.minEasting) / 1280d),
            custom.ChunksX);
        Assert.Equal(
            (int)Math.Ceiling((bounds.maxNorthing - bounds.minNorthing) / 1280d),
            custom.ChunksY);

        var diagnostics = new GeoDiagnosticBag();
        var utm = LocalMapGrid.Create(
            extent,
            Wgs84,
            CreateManifest(new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, UtmZone: 31, Hemisphere: ManifestHemisphere.North)),
            Transformer,
            diagnostics);

        Assert.Null(utm);
        Assert.True(diagnostics.HasErrors);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(GeoDiagnosticCodes.CoordinatesScaleError, diagnostic.Code);
        Assert.Equal(GeoStage.Coordinates, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
        Assert.Contains("maxScaleError", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UtmTargetResolvesTheZonePreset()
    {
        // Slightly under one span: the nanometer round-trip through WGS84 cannot push the
        // projected width past the chunk span.
        var projection = TransverseMercatorProjection.Utm(31, north: true);
        var extent = GeographicImageOfSquare(projection, 500000d, 5400000d, 1279.9d, 1279.9d);
        var grid = CreateGrid(extent, Wgs84, target: new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, UtmZone: 31, Hemisphere: ManifestHemisphere.North));

        Assert.NotNull(grid);
        Assert.Equal(1, grid!.ChunksX);
        Assert.Equal(1, grid.ChunksY);
        Assert.Equal(32631, grid.ProjectedCoordinateSystem.EpsgCode);
        Assert.Equal(500000d, grid.Transform.OriginEastingNorthing.X, MeterPrecision);
        Assert.Equal(5400000d, grid.Transform.OriginEastingNorthing.Y, MeterPrecision);
    }

    [Fact]
    public void ExtentInAnotherProjectedCrs_GoesThroughTheTransformer()
    {
        // A half-span square stays one chunk after crossing CRS: the target TM scale (1)
        // differs from the source UTM scale (0.9996), so the projected span differs from
        // the source span by well over the round-off guard.
        var extent = ProjectedSquare(500000d, 5400000d, 640d, 640d);
        var grid = CreateGrid(extent, Utm31North);

        Assert.NotNull(grid);
        Assert.Equal(1, grid!.ChunksX);
        Assert.Equal(1, grid.ChunksY);

        // The projected image of the UTM square curves, so the southwest grid origin is the
        // minimum over all projected corners, not the projected UTM southwest corner.
        var bounds = ProjectedBoundsOf(extent, Utm31North, Tm4);

        Assert.Equal(bounds.minEasting, grid.Transform.OriginEastingNorthing.X, MeterPrecision);
        Assert.Equal(bounds.minNorthing, grid.Transform.OriginEastingNorthing.Y, MeterPrecision);

        for (var i = 0; i < extent.OuterRing.Positions.Count; i++)
        {
            var expected = Transformer.Transform(extent.OuterRing.Positions[i].Coordinate, Utm31North, Tm4);
            var actual = grid.PlayableArea.OuterRing.Positions[i].Coordinate;

            Assert.Equal(expected.X, actual.X, MeterPrecision);
            Assert.Equal(expected.Y, actual.Y, MeterPrecision);
        }
    }

    [Fact]
    public void PlayableAreaRetainsHolesAndAltitudes()
    {
        var hole = new GeoRing(
        [
            new GeoPosition(new Vector2d(100100d, 6000100d)),
            new GeoPosition(new Vector2d(100300d, 6000100d)),
            new GeoPosition(new Vector2d(100300d, 6000300d)),
            new GeoPosition(new Vector2d(100100d, 6000300d)),
            new GeoPosition(new Vector2d(100100d, 6000100d)),
        ]);
        var outer = new GeoRing(
        [
            new GeoPosition(new Vector2d(100000d, 6000000d), 12.5d),
            new GeoPosition(new Vector2d(102560d, 6000000d)),
            new GeoPosition(new Vector2d(102560d, 6002560d)),
            new GeoPosition(new Vector2d(100000d, 6002560d)),
            new GeoPosition(new Vector2d(100000d, 6000000d)),
        ]);
        var extent = new GeoPolygon(outer, [hole]);

        var grid = CreateGrid(extent, Tm4);

        Assert.NotNull(grid);
        Assert.Equal(2, grid!.ChunksX);
        Assert.Equal(2, grid.ChunksY);
        var retained = Assert.Single(grid.PlayableArea.InnerRings);
        AssertRingsEqual(hole, retained);
        AssertRingsEqual(outer, grid.PlayableArea.OuterRing);
        Assert.Equal(12.5d, grid.PlayableArea.OuterRing.Positions[0].AltitudeMeters);
    }

    [Fact]
    public void ExtentFeatureMustBeASinglePolygon()
    {
        var line = new GeoLineString(
        [
            new GeoPosition(new Vector2d(4d, 47d)),
            new GeoPosition(new Vector2d(5d, 47d)),
        ]);
        var feature = new GeospatialFeature("extent.kml", "road", line, Wgs84);
        var diagnostics = new GeoDiagnosticBag();

        var grid = LocalMapGrid.Create(feature, CreateManifest(), Transformer, diagnostics);

        Assert.Null(grid);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(GeoDiagnosticCodes.CoordinatesInvalidExtent, diagnostic.Code);
        Assert.Equal(GeoStage.Coordinates, diagnostic.Stage);
        Assert.Contains("single polygon", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("road", diagnostic.Message, StringComparison.Ordinal);

        var point = new GeospatialFeature(
            "extent.kml",
            "marker",
            new GeoPointGeometry(new GeoPosition(new Vector2d(4d, 47d))),
            Wgs84);

        Assert.Null(LocalMapGrid.Create(point, CreateManifest(), Transformer, new GeoDiagnosticBag()));

        var multi = new GeospatialFeature(
            "extent.kml",
            "areas",
            new GeoMultiGeometry([ProjectedSquare(100000d, 6000000d, 1280d, 1280d)]),
            Wgs84);

        Assert.Null(LocalMapGrid.Create(multi, CreateManifest(), Transformer, new GeoDiagnosticBag()));
    }

    [Fact]
    public void DefaultCoordinateSystemIdExtentIsRejected()
    {
        var extent = ProjectedSquare(100000d, 6000000d, 1280d, 1280d);
        var diagnostics = new GeoDiagnosticBag();

        var grid = LocalMapGrid.Create(extent, default, CreateManifest(), Transformer, diagnostics);

        Assert.Null(grid);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(GeoDiagnosticCodes.CoordinatesInvalidExtent, diagnostic.Code);
        Assert.Contains("explicit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DegenerateExtentRingsAreRejected()
    {
        // Three distinct vertices on one diagonal: the bounding box is nonzero but the
        // signed area is zero.
        var diagonal = new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(100000d, 6000000d)),
                new GeoPosition(new Vector2d(100100d, 6000100d)),
                new GeoPosition(new Vector2d(100200d, 6000200d)),
                new GeoPosition(new Vector2d(100000d, 6000000d)),
            ]));

        AssertRejectedWithCode(
            diagonal,
            Tm4,
            GeoDiagnosticCodes.CoordinatesInvalidExtent,
            "zero area");

        var verticalLine = new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(100000d, 6000000d)),
                new GeoPosition(new Vector2d(100000d, 6001000d)),
                new GeoPosition(new Vector2d(100000d, 6000000d)),
            ]));

        AssertRejectedWithCode(
            verticalLine,
            Tm4,
            GeoDiagnosticCodes.CoordinatesInvalidExtent,
            "three distinct positions");

        var singlePoint = new GeoPolygon(new GeoRing([new GeoPosition(new Vector2d(100000d, 6000000d))]));

        AssertRejectedWithCode(
            singlePoint,
            Tm4,
            GeoDiagnosticCodes.CoordinatesInvalidExtent,
            "three distinct positions");

        var unclosed = new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(100000d, 6000000d)),
                new GeoPosition(new Vector2d(100100d, 6000000d)),
                new GeoPosition(new Vector2d(100000d, 6000100d)),
            ]));

        AssertRejectedWithCode(
            unclosed,
            Tm4,
            GeoDiagnosticCodes.CoordinatesInvalidExtent,
            "closed");
    }

    [Fact]
    public void ZeroAreaHoleIsRejected()
    {
        // Bow-tie hole: two triangles of opposite orientation cancel to zero area.
        var bowTieHole = new GeoRing(
        [
            new GeoPosition(new Vector2d(100100d, 6000100d)),
            new GeoPosition(new Vector2d(100300d, 6000100d)),
            new GeoPosition(new Vector2d(100100d, 6000300d)),
            new GeoPosition(new Vector2d(100300d, 6000300d)),
            new GeoPosition(new Vector2d(100100d, 6000100d)),
        ]);
        var extent = ProjectedSquare(100000d, 6000000d, 2560d, 2560d, [bowTieHole]);

        AssertRejectedWithCode(
            extent,
            Tm4,
            GeoDiagnosticCodes.CoordinatesInvalidExtent,
            "Extent hole 0 has zero area.");
    }

    [Fact]
    public void NonFiniteExtentCoordinatesAreRejected()
    {
        var nanEasting = new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(double.NaN, 6000000d)),
                new GeoPosition(new Vector2d(100100d, 6000000d)),
                new GeoPosition(new Vector2d(100000d, 6000100d)),
            ]));

        AssertRejectedWithCode(
            nanEasting,
            Tm4,
            GeoDiagnosticCodes.CoordinatesInvalidExtent,
            "not finite");

        var infiniteLatitude = new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(4d, double.PositiveInfinity)),
                new GeoPosition(new Vector2d(4.01d, 47d)),
                new GeoPosition(new Vector2d(4d, 47.01d)),
            ]));

        AssertRejectedWithCode(
            infiniteLatitude,
            Wgs84,
            GeoDiagnosticCodes.CoordinatesInvalidExtent,
            "not finite");
    }

    [Fact]
    public void ExtremeMetersPerGameUnitOverflowsChunkArithmetic()
    {
        var extent = ProjectedSquare(100000d, 6000000d, 1280d, 1280d);

        // Ratio 1e9: beyond the per-axis maximum.
        AssertRejectedWithCode(
            extent,
            Tm4,
            GeoDiagnosticCodes.CoordinatesExtentOverflow,
            "supported maximum",
            metersPerGameUnit: 1e-9d);

        // Ratio exactly 2^31: casting to int before the range check would wrap this to
        // int.MinValue and pass the bound.
        AssertRejectedWithCode(
            extent,
            Tm4,
            GeoDiagnosticCodes.CoordinatesExtentOverflow,
            "supported maximum",
            metersPerGameUnit: 1d / (1L << 31));

        // Ratio 1e300: finite but far outside int range.
        AssertRejectedWithCode(
            extent,
            Tm4,
            GeoDiagnosticCodes.CoordinatesExtentOverflow,
            "supported maximum",
            metersPerGameUnit: 1e-300d);

        AssertRejectedWithCode(
            extent,
            Tm4,
            GeoDiagnosticCodes.CoordinatesExtentOverflow,
            "supported maximum",
            metersPerGameUnit: double.Epsilon);
    }

    [Fact]
    public void OversizedDerivedGridCoordinatesAreRejected()
    {
        // metersPerGameUnit 1.4e305 keeps the chunk span finite, so the chunk counts pass,
        // but the generated corner and apron sample coordinates overflow projected space.
        var extent = ProjectedSquare(100000d, 6000000d, 1280d, 1280d);

        AssertRejectedWithCode(
            extent,
            Tm4,
            GeoDiagnosticCodes.CoordinatesExtentOverflow,
            "not finite",
            metersPerGameUnit: 1.4e305d);
    }

    [Fact]
    public void ChunkCountAboveTheLimitIsRejectedAndAtTheLimitAccepted()
    {
        var nineChunks = ProjectedSquare(100000d, 6000000d, 3840d, 3840d);

        var rejected = CreateGrid(nineChunks, Tm4, limits: new GeospatialImportLimits(MaxChunkCount: 8));

        Assert.Null(rejected);
        var diagnostics = new GeoDiagnosticBag();
        LocalMapGrid.Create(nineChunks, Tm4, CreateManifest(), Transformer, diagnostics, new GeospatialImportLimits(MaxChunkCount: 8));
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(GeoDiagnosticCodes.GenerateFailure, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Contains("3 x 3 = 9", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("8", diagnostic.Message, StringComparison.Ordinal);

        var accepted = CreateGrid(nineChunks, Tm4, limits: new GeospatialImportLimits(MaxChunkCount: 9));

        Assert.NotNull(accepted);
        Assert.Equal(3, accepted!.ChunksX);
        Assert.Equal(3, accepted.ChunksY);

        // 256 x 256 chunks hits the default maxChunkCount of 65536 exactly and is accepted.
        var boundary = ProjectedSquare(-163840d, 5100000d, 327680d, 327680d);
        var boundaryGrid = CreateGrid(boundary, Tm4);

        Assert.NotNull(boundaryGrid);
        Assert.Equal(256, boundaryGrid!.ChunksX);
        Assert.Equal(256, boundaryGrid.ChunksY);
    }

    [Fact]
    public void InvalidDirectManifestParametersAreRejected()
    {
        foreach (var manifest in InvalidManifests())
        {
            var extent = ProjectedSquare(100000d, 6000000d, 1280d, 1280d);
            var diagnostics = new GeoDiagnosticBag();
            var grid = LocalMapGrid.Create(extent, Tm4, manifest, Transformer, diagnostics);

            Assert.Null(grid);
            Assert.True(diagnostics.HasErrors);
            Assert.Contains(
                diagnostics.Items,
                d => d.Code == GeoDiagnosticCodes.ManifestValueInvalid && d.Stage == GeoStage.Manifest);
        }
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        var extent = ProjectedSquare(100000d, 6000000d, 1280d, 1280d);
        var manifest = CreateManifest();
        var diagnostics = new GeoDiagnosticBag();

        Assert.Throws<ArgumentNullException>(() => LocalMapGrid.Create(null!, manifest, Transformer, diagnostics));
        Assert.Throws<ArgumentNullException>(() => LocalMapGrid.Create(extent, Tm4, null!, Transformer, diagnostics));
        Assert.Throws<ArgumentNullException>(() => LocalMapGrid.Create(extent, Tm4, manifest, null!, diagnostics));
        Assert.Throws<ArgumentNullException>(() => LocalMapGrid.Create(extent, Tm4, manifest, Transformer, null!));
        Assert.Throws<ArgumentNullException>(
            () => LocalMapGrid.Create(
                new GeospatialFeature("s", "f", ProjectedSquare(1d, 1d, 1d, 1d), Tm4),
                null!,
                Transformer,
                diagnostics));

        // The non-polygon branch must not touch a null diagnostics bag either.
        var line = new GeospatialFeature(
            "s",
            "f",
            new GeoLineString([new GeoPosition(new Vector2d(4d, 47d)), new GeoPosition(new Vector2d(5d, 47d))]),
            Wgs84);

        Assert.Throws<ArgumentNullException>(() => LocalMapGrid.Create(line, manifest, Transformer, null!));
    }

    [Fact]
    public void TransformCarriesSourcesDatumAndVerticalPolicy()
    {
        var extent = ProjectedSquare(100000d, 6000000d, 1280d, 1280d);
        var grid = CreateGrid(
            extent,
            Tm4,
            metersPerGameUnit: 1.25d,
            verticalPolicy: new VerticalPolicy(120d, 1.5d),
            elevationDatum: "EGM96");

        Assert.NotNull(grid);
        Assert.Equal("EGM96", grid!.Transform.VerticalDatum);
        Assert.Equal(120d, grid.Transform.VerticalOriginMeters);
        Assert.Equal(1.5d, grid.Transform.VerticalExaggeration);
        Assert.Equal(1.25d, grid.Transform.MetersPerGameUnit);
        Assert.Equal(new[] { Tm4 }, grid.Transform.SourceCoordinateSystems);
        Assert.Equal(Tm4, grid.Transform.ProjectedCoordinateSystem);

        var noDatum = CreateGrid(extent, Tm4, elevationDatum: null);

        Assert.NotNull(noDatum);
        Assert.Equal("unknown", noDatum!.Transform.VerticalDatum);
    }

    private static IEnumerable<GeospatialImportManifest> InvalidManifests()
    {
        yield return CreateManifest(metersPerGameUnit: 0d);
        yield return CreateManifest(metersPerGameUnit: -1d);
        yield return CreateManifest(metersPerGameUnit: double.NaN);
        yield return CreateManifest(metersPerGameUnit: double.PositiveInfinity);
        yield return CreateManifest(metersPerGameUnit: double.MaxValue);
        yield return CreateManifest(metersPerGameUnit: 1e308d);
        yield return CreateManifest(maxScaleError: 0d);
        yield return CreateManifest(maxScaleError: double.NaN);
        yield return CreateManifest(verticalPolicy: new VerticalPolicy(double.NaN, 1d));
        yield return CreateManifest(verticalPolicy: new VerticalPolicy(0d, 0d));
        yield return CreateManifest(verticalPolicy: new VerticalPolicy(0d, double.PositiveInfinity));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec((ManifestCrsKind)99));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, UtmZone: null, Hemisphere: ManifestHemisphere.North));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, UtmZone: 0, Hemisphere: ManifestHemisphere.North));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, UtmZone: 61, Hemisphere: ManifestHemisphere.North));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, UtmZone: 31));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, UtmZone: 31, Hemisphere: (ManifestHemisphere)99));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.Utm, UtmZone: 31, Hemisphere: ManifestHemisphere.North, CentralMeridianDegrees: 4d));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, UtmZone: 31, Hemisphere: ManifestHemisphere.North));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: -180d));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: 180.01d));
        yield return CreateManifest(target: new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: double.NaN));
    }

    private static LocalMapGrid? CreateGrid(
        GeoPolygon extent,
        CoordinateSystemId extentCoordinateSystem,
        TargetCoordinateSystemSpec? target = null,
        double metersPerGameUnit = 1d,
        double maxScaleError = 0.001d,
        string? elevationDatum = "EGM96",
        VerticalPolicy? verticalPolicy = null,
        GeospatialImportLimits? limits = null)
    {
        return LocalMapGrid.Create(
            extent,
            extentCoordinateSystem,
            CreateManifest(target, metersPerGameUnit, maxScaleError, elevationDatum, verticalPolicy),
            Transformer,
            new GeoDiagnosticBag(),
            limits);
    }

    private static GeospatialImportManifest CreateManifest(
        TargetCoordinateSystemSpec? target = null,
        double metersPerGameUnit = 1d,
        double maxScaleError = 0.001d,
        string? elevationDatum = "EGM96",
        VerticalPolicy? verticalPolicy = null)
    {
        var sources = new List<ManifestSource>
        {
            new("extent", ManifestSourceKind.Kml, "area.kml", ManifestSourceRole.Extent),
        };
        if (elevationDatum is not null)
        {
            sources.Add(new ManifestSource("dem", ManifestSourceKind.Geotiff, "dem.tif", ManifestSourceRole.Elevation, elevationDatum));
        }

        return new GeospatialImportManifest
        {
            Sources = sources,
            TargetCoordinateSystem = target ?? new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: 4d),
            MetersPerGameUnit = metersPerGameUnit,
            DefaultMaterial = "grass",
            VerticalPolicy = verticalPolicy ?? new VerticalPolicy(),
            MaxScaleError = maxScaleError,
        };
    }

    private static void AssertRejectedWithCode(
        GeoPolygon extent,
        CoordinateSystemId extentCoordinateSystem,
        string code,
        string messagePart,
        double metersPerGameUnit = 1d)
    {
        var diagnostics = new GeoDiagnosticBag();
        var grid = LocalMapGrid.Create(
            extent,
            extentCoordinateSystem,
            CreateManifest(metersPerGameUnit: metersPerGameUnit),
            Transformer,
            diagnostics);

        Assert.Null(grid);
        Assert.True(diagnostics.HasErrors);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(GeoStage.Coordinates, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
    }

    private static GeoPolygon ProjectedSquare(double west, double south, double width, double height, IReadOnlyList<GeoRing>? holes = null)
    {
        return new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(west, south)),
                new GeoPosition(new Vector2d(west + width, south)),
                new GeoPosition(new Vector2d(west + width, south + height)),
                new GeoPosition(new Vector2d(west, south + height)),
                new GeoPosition(new Vector2d(west, south)),
            ]),
            holes);
    }

    private static GeoPolygon GeographicRectangle(double west, double south, double east, double north)
    {
        return new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(west, south)),
                new GeoPosition(new Vector2d(east, south)),
                new GeoPosition(new Vector2d(east, north)),
                new GeoPosition(new Vector2d(west, north)),
                new GeoPosition(new Vector2d(west, south)),
            ]));
    }

    private static GeoPolygon GeographicImageOfSquare(
        TransverseMercatorProjection projection,
        double west,
        double south,
        double width,
        double height)
    {
        var southWest = projection.Inverse(west, south);
        var southEast = projection.Inverse(west + width, south);
        var northEast = projection.Inverse(west + width, south + height);
        var northWest = projection.Inverse(west, south + height);

        return new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(southWest.ToVector2d()),
                new GeoPosition(southEast.ToVector2d()),
                new GeoPosition(northEast.ToVector2d()),
                new GeoPosition(northWest.ToVector2d()),
                new GeoPosition(southWest.ToVector2d()),
            ]));
    }

    private static void AssertRingsEqual(GeoRing expected, GeoRing actual)
    {
        Assert.Equal(expected.Positions.Count, actual.Positions.Count);
        for (var i = 0; i < expected.Positions.Count; i++)
        {
            Assert.Equal(expected.Positions[i].Coordinate.X, actual.Positions[i].Coordinate.X);
            Assert.Equal(expected.Positions[i].Coordinate.Y, actual.Positions[i].Coordinate.Y);
        }
    }

    private static (double minEasting, double maxEasting, double minNorthing, double maxNorthing) ProjectedBoundsOf(
        GeoPolygon extent,
        CoordinateSystemId source,
        CoordinateSystemId target)
    {
        var minEasting = double.MaxValue;
        var maxEasting = double.MinValue;
        var minNorthing = double.MaxValue;
        var maxNorthing = double.MinValue;
        foreach (var position in extent.OuterRing.Positions)
        {
            var projected = Transformer.Transform(position.Coordinate, source, target);
            minEasting = Math.Min(minEasting, projected.X);
            maxEasting = Math.Max(maxEasting, projected.X);
            minNorthing = Math.Min(minNorthing, projected.Y);
            maxNorthing = Math.Max(maxNorthing, projected.Y);
        }

        return (minEasting, maxEasting, minNorthing, maxNorthing);
    }
}
