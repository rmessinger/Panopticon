using System;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Sampling;
using Xunit;

namespace Panopticon.Geospatial.Tests.Sampling;

public class RasterSamplerPolicyTests
{
    private static RasterSampler IdentitySampler()
    {
        return new RasterSampler(SamplingTestSupport.IdentityTransformer());
    }

    private static GeospatialRaster<double> Grid(int width, int height, double step)
    {
        return SamplingTestSupport.Raster(
            width,
            height,
            SamplingTestSupport.NorthUp(0d, 0d, step, step),
            SamplingTestSupport.Utm31,
            (column, row) => column == 1 && row == 1 ? -9d : (10d * row) + column,
            noData: -9d,
            hasNoData: true);
    }

    [Fact]
    public void NoDataFailPolicyReturnsTypedFailure()
    {
        var raster = Grid(3, 3, 1d);
        var sampler = IdentitySampler();

        var result = sampler.Sample(raster, 0, 1.5d, -1.5d, Footprint(1.5d, -1.5d), SampleKernel.Nearest);

        Assert.Equal(SampleStatus.Failed, result.Status);
        Assert.Equal(0d, result.Value, 9);
        Assert.False(double.IsNaN(result.Value));
        Assert.Equal(SamplingDiagnosticCodes.NoDataPolicyFailure, result.Diagnostics[0].Code);
        Assert.Equal(GeoSeverity.Error, result.Diagnostics[0].Severity);
        Assert.Equal(GeoStage.Raster, result.Diagnostics[0].Stage);
    }

    [Fact]
    public void NoDataConstantPolicyFillsAndCounts()
    {
        var raster = Grid(3, 3, 1d);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(noData: NoDataPolicy.Constant, noDataConstantMeters: 7d);
        var stats = new SamplingStats();

        var result = sampler.Sample(raster, 0, 1.5d, -1.5d, Footprint(1.5d, -1.5d), SampleKernel.Nearest, options, stats);

        Assert.True(result.IsSampled);
        Assert.Equal(7d, result.Value, 9);
        Assert.Equal(1, result.NoDataFills);
        Assert.Equal(SamplingDiagnosticCodes.NoDataFilled, result.Diagnostics[0].Code);
        Assert.Equal(GeoSeverity.Info, result.Diagnostics[0].Severity);
        Assert.Equal(1, stats.NoDataFillCount);
        Assert.Equal(0, stats.OutsideFillCount);
    }

    [Fact]
    public void NearestValidTieBreaksLowestRowThenColumn()
    {
        var raster = Grid(3, 3, 1d);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(
            noData: NoDataPolicy.NearestValid,
            noDataSearchRadiusMeters: 15d);

        // All four cardinal neighbours sit exactly 1 m away; the tie resolves to
        // row 0, column 1 (value 1), not row 1 column 0, row 1 column 2, or row 2 column 1.
        var result = sampler.Sample(raster, 0, 1.5d, -1.5d, Footprint(1.5d, -1.5d), SampleKernel.Nearest, options);

        Assert.True(result.IsSampled);
        Assert.Equal(1d, result.Value, 9);
        Assert.Equal(1, result.NoDataFills);
    }

    [Fact]
    public void NearestValidRowTiePrefersTheLowerRow()
    {
        // A single column of three cells; the middle cell is no-data, rows 0 and 2 are
        // both exactly 1 m away, and the lower row wins with value 5.
        var raster = SamplingTestSupport.Raster(
            1,
            3,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => row == 1 ? -9d : (10d * row) + 5d,
            noData: -9d,
            hasNoData: true);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(
            noData: NoDataPolicy.NearestValid,
            noDataSearchRadiusMeters: 10d);

        var result = sampler.Sample(raster, 0, 0.5d, -1.5d, Footprint(0.5d, -1.5d), SampleKernel.Nearest, options);

        Assert.True(result.IsSampled);
        Assert.Equal(5d, result.Value, 9);
    }

    [Fact]
    public void NearestValidRadiusLimitIsInclusiveInMeters()
    {
        var atLimit = Grid(3, 3, 1d);
        var beyondLimit = Grid(3, 3, 1d);
        var sampler = IdentitySampler();

        var accepted = sampler.Sample(
            atLimit,
            0,
            1.5d,
            -1.5d,
            Footprint(1.5d, -1.5d),
            SampleKernel.Nearest,
            new SamplingOptions(noData: NoDataPolicy.NearestValid, noDataSearchRadiusMeters: 1d));
        var rejected = sampler.Sample(
            beyondLimit,
            0,
            1.5d,
            -1.5d,
            Footprint(1.5d, -1.5d),
            SampleKernel.Nearest,
            new SamplingOptions(noData: NoDataPolicy.NearestValid, noDataSearchRadiusMeters: 0.99d));

        // The nearest valid cells are exactly 1 m away: at the limit the fill succeeds.
        Assert.True(accepted.IsSampled);
        Assert.Equal(1d, accepted.Value, 9);
        Assert.Equal(SampleStatus.Failed, rejected.Status);
        Assert.Equal(SamplingDiagnosticCodes.NoDataPolicyFailure, rejected.Diagnostics[0].Code);
    }

    [Fact]
    public void GeographicNearestValidRadiusUsesMeters()
    {
        // 0.001 degrees per cell mapped to 100 m per cell by the transformer. Only cell
        // (row 1, column 2) is valid; it sits about 100 m from the no-data query cell.
        var raster = SamplingTestSupport.Raster(
            3,
            3,
            SamplingTestSupport.NorthUp(2d, 48d, 0.001d, 0.001d),
            SamplingTestSupport.Wgs84,
            (column, row) => column == 2 && row == 1 ? 12d : -9d,
            noData: -9d,
            hasNoData: true);
        var sampler = new RasterSampler(SamplingTestSupport.ScalingTransformer(100000d, 100000d));
        var footprint = SamplingTestSupport.Footprint(200150d, 4799850d, 50d, 50d, SamplingTestSupport.MetricTarget);

        var accepted = sampler.Sample(
            raster,
            0,
            2.0015d,
            47.9985d,
            footprint,
            SampleKernel.Nearest,
            new SamplingOptions(noData: NoDataPolicy.NearestValid, noDataSearchRadiusMeters: 150d));
        var rejected = sampler.Sample(
            raster,
            0,
            2.0015d,
            47.9985d,
            footprint,
            SampleKernel.Nearest,
            new SamplingOptions(noData: NoDataPolicy.NearestValid, noDataSearchRadiusMeters: 50d));

        Assert.True(accepted.IsSampled);
        Assert.Equal(12d, accepted.Value, 9);
        Assert.Equal(1, accepted.NoDataFills);
        Assert.Equal(SampleStatus.Failed, rejected.Status);
    }

    [Fact]
    public void EdgeFailPolicyReturnsTypedFailure()
    {
        var raster = Grid(2, 2, 1d);
        var sampler = IdentitySampler();

        // Cell (2, 0) is one column beyond the two-column raster.
        var result = sampler.Sample(raster, 0, 2.5d, -0.5d, Footprint(2.5d, -0.5d), SampleKernel.Nearest);

        Assert.Equal(SampleStatus.Failed, result.Status);
        Assert.Equal(0d, result.Value, 9);
        Assert.Equal(SamplingDiagnosticCodes.EdgePolicyFailure, result.Diagnostics[0].Code);
        Assert.Equal(GeoSeverity.Error, result.Diagnostics[0].Severity);
    }

    [Fact]
    public void EdgeConstantPolicyFillsOutsideCoverage()
    {
        var raster = Grid(2, 2, 1d);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(edge: EdgePolicy.Constant, edgeConstantMeters: 50d);

        var result = sampler.Sample(
            raster,
            0,
            2.5d,
            -0.5d,
            Footprint(2.5d, -0.5d),
            SampleKernel.Nearest,
            options);

        Assert.True(result.IsSampled);
        Assert.Equal(50d, result.Value, 9);
        Assert.Equal(1, result.OutsideFills);
        Assert.Equal(0, result.NoDataFills);
        Assert.Equal(SamplingDiagnosticCodes.OutsideCoverageFilled, result.Diagnostics[0].Code);
        Assert.Equal(GeoSeverity.Info, result.Diagnostics[0].Severity);
    }

    [Fact]
    public void EdgeNearestFillsWithinRadiusAndFailsBeyondIt()
    {
        var raster = Grid(3, 3, 1d);
        var sampler = IdentitySampler();

        // Query cell (3, 1) is exactly 1 m east of edge cell (1, 2) with value 12.
        var accepted = sampler.Sample(
            raster,
            0,
            3.5d,
            -1.5d,
            Footprint(3.5d, -1.5d),
            SampleKernel.Nearest,
            new SamplingOptions(edge: EdgePolicy.Nearest, edgeSearchRadiusMeters: 1d));
        var rejected = sampler.Sample(
            raster,
            0,
            3.5d,
            -1.5d,
            Footprint(3.5d, -1.5d),
            SampleKernel.Nearest,
            new SamplingOptions(edge: EdgePolicy.Nearest, edgeSearchRadiusMeters: 0.99d));

        Assert.True(accepted.IsSampled);
        Assert.Equal(12d, accepted.Value, 9);
        Assert.Equal(1, accepted.OutsideFills);
        Assert.Equal(SampleStatus.Failed, rejected.Status);
        Assert.Equal(SamplingDiagnosticCodes.EdgePolicyFailure, rejected.Diagnostics[0].Code);
    }

    [Fact]
    public void EdgeNearestAppliesNoDataPolicyToTheEdgeSampleIndependently()
    {
        var raster = SamplingTestSupport.Raster(
            1,
            1,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => -9d,
            noData: -9d,
            hasNoData: true);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(
            noData: NoDataPolicy.Constant,
            noDataConstantMeters: 7d,
            edge: EdgePolicy.Nearest,
            edgeSearchRadiusMeters: 10d);

        // Query cell (1, 0) is 1 m from the single edge cell, which itself is no-data.
        var result = sampler.Sample(raster, 0, 1.5d, -0.5d, Footprint(1.5d, -0.5d), SampleKernel.Nearest, options);

        Assert.True(result.IsSampled);
        Assert.Equal(7d, result.Value, 9);
        Assert.Equal(1, result.OutsideFills);
        Assert.Equal(1, result.NoDataFills);
    }

    [Fact]
    public void BilinearPartialEdgeWeightsTheEdgeFill()
    {
        var raster = SamplingTestSupport.Raster(
            2,
            1,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => column == 0 ? 100d : 200d);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(edge: EdgePolicy.Constant, edgeConstantMeters: 50d);

        // Cell (1.25, 0): 75 percent from cell 1, 25 percent from uncovered cell 2.
        var result = sampler.Sample(
            raster,
            0,
            1.75d,
            -0.5d,
            SamplingTestSupport.Footprint(1.75d, -0.5d, 0.5d, 0.5d),
            SampleKernel.Bilinear,
            options);

        Assert.True(result.IsSampled);
        Assert.Equal(162.5d, result.Value, 9);
        Assert.Equal(1, result.OutsideFills);
    }

    [Fact]
    public void StatsSeparateNoDataOutsideAndFailures()
    {
        var raster = Grid(3, 3, 1d);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(
            noData: NoDataPolicy.Constant,
            noDataConstantMeters: 7d,
            edge: EdgePolicy.Constant,
            edgeConstantMeters: 50d);
        var stats = new SamplingStats();

        sampler.Sample(raster, 0, 0.5d, -0.5d, Footprint(0.5d, -0.5d), SampleKernel.Nearest, options, stats);
        sampler.Sample(
            raster,
            0,
            1.5d,
            -1.5d,
            Footprint(1.5d, -1.5d),
            SampleKernel.Nearest,
            options,
            stats);
        sampler.Sample(
            raster,
            0,
            3.5d,
            -0.5d,
            Footprint(3.5d, -0.5d),
            SampleKernel.Nearest,
            options,
            stats);
        sampler.Sample(
            raster,
            0,
            1.5d,
            -1.5d,
            Footprint(1.5d, -1.5d),
            SampleKernel.Nearest,
            new SamplingOptions(),
            stats);

        Assert.Equal(4, stats.SampleCount);
        Assert.Equal(1, stats.NoDataFillCount);
        Assert.Equal(1, stats.OutsideFillCount);
        Assert.Equal(1, stats.FailedSampleCount);
        Assert.NotNull(stats.AffectedBounds);
        Assert.Equal(1.25d, stats.AffectedBounds!.MinEasting, 9);
        Assert.Equal(3.75d, stats.AffectedBounds!.MaxEasting, 9);
        Assert.Equal(-1.75d, stats.AffectedBounds!.MinNorthing, 9);
        Assert.Equal(-0.25d, stats.AffectedBounds!.MaxNorthing, 9);
    }

    [Fact]
    public void UnsupportedCoordinatePairFailsWithTypedDiagnostic()
    {
        var raster = Grid(2, 2, 1d);
        var sampler = IdentitySampler();

        // The identity transformer supports only equal CRS ids; the metric target differs
        // from the raster's UTM source, so every internal transform throws.
        var result = sampler.Sample(
            raster,
            0,
            0.5d,
            -0.5d,
            SamplingTestSupport.Footprint(0.5d, -0.5d, 1d, 1d, SamplingTestSupport.MetricTarget),
            SampleKernel.Nearest);

        Assert.Equal(SampleStatus.Failed, result.Status);
        Assert.Equal(0d, result.Value, 9);
        Assert.Null(result.Selection);
        Assert.Equal(SamplingDiagnosticCodes.InvalidSample, result.Diagnostics[0].Code);
        Assert.NotNull(result.Diagnostics[0].Detail);
    }

    [Fact]
    public void NonFiniteValuesNeverSurfaceAsNaN()
    {
        var overflowing = SamplingTestSupport.Raster(
            2,
            1,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 1e308d);
        var nanCell = SamplingTestSupport.Raster(
            1,
            1,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => double.NaN);
        var sampler = IdentitySampler();

        // 1e308 + 1e308 overflows the accumulator to infinity.
        var overflow = sampler.Sample(
            overflowing,
            0,
            1d,
            -0.5d,
            SamplingTestSupport.Footprint(1d, -0.5d, 2d, 1d),
            SampleKernel.Auto);
        // An undeclared NaN sample behaves as no-data, not as a silent NaN result.
        var nan = sampler.Sample(nanCell, 0, 0.5d, -0.5d, Footprint(0.5d, -0.5d), SampleKernel.Nearest);

        Assert.Equal(SampleStatus.Failed, overflow.Status);
        Assert.Equal(0d, overflow.Value, 9);
        Assert.Equal(SamplingDiagnosticCodes.InvalidSample, overflow.Diagnostics[0].Code);
        Assert.Equal(SampleStatus.Failed, nan.Status);
        Assert.Equal(SamplingDiagnosticCodes.NoDataPolicyFailure, nan.Diagnostics[0].Code);
    }

    [Fact]
    public void OptionsValidationMirrorsManifestRequirements()
    {
        Assert.Throws<ArgumentException>(() => new SamplingOptions(noData: NoDataPolicy.NearestValid));
        Assert.Throws<ArgumentException>(() => new SamplingOptions(noData: NoDataPolicy.Constant));
        Assert.Throws<ArgumentException>(() => new SamplingOptions(edge: EdgePolicy.Nearest));
        Assert.Throws<ArgumentException>(() => new SamplingOptions(edge: EdgePolicy.Constant));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SamplingOptions(
                noData: NoDataPolicy.NearestValid,
                noDataSearchRadiusMeters: -1d));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SamplingOptions(noData: NoDataPolicy.Constant, noDataConstantMeters: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SamplingOptions(maxFootprintCells: 0));
        Assert.Throws<ArgumentNullException>(() => new RasterSampler(null!));
    }

    [Fact]
    public void EdgeNearestRadiusUsesActualQueryPosition()
    {
        // Two unit cells with centers at world X 0.5 and 1.5; the edge radius is measured
        // from the actual fractional query, not the rounded nearest cell.
        var raster = SamplingTestSupport.Raster(
            2,
            1,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => column == 0 ? 5d : 7d);
        var sampler = IdentitySampler();

        SampleFootprint FootprintAt(double x) => SamplingTestSupport.Footprint(x, -0.5d, 0.5d, 0.5d);

        RasterSampleResult EdgeSampled(double x, double radius) => sampler.Sample(
            raster,
            0,
            x,
            -0.5d,
            FootprintAt(x),
            SampleKernel.Nearest,
            new SamplingOptions(edge: EdgePolicy.Nearest, edgeSearchRadiusMeters: radius));

        var near = EdgeSampled(2.01d, 0.75d);
        var far = EdgeSampled(2.99d, 1.1d);
        var atLimit = EdgeSampled(3d, 1.5d);
        var beyondLimit = EdgeSampled(3d, 1.49d);

        Assert.True(near.IsSampled);
        Assert.Equal(7d, near.Value, 9);
        Assert.Equal(SampleStatus.Failed, far.Status);
        Assert.Equal(SamplingDiagnosticCodes.EdgePolicyFailure, far.Diagnostics[0].Code);
        Assert.True(atLimit.IsSampled);
        Assert.Equal(7d, atLimit.Value, 9);
        Assert.Equal(SampleStatus.Failed, beyondLimit.Status);
    }

    [Fact]
    public void HugeRadiusSearchesTheRasterWithoutOverflow()
    {
        var raster = Grid(3, 3, 1d);
        var sampler = IdentitySampler();

        var result = sampler.Sample(
            raster,
            0,
            1.5d,
            -1.5d,
            Footprint(1.5d, -1.5d),
            SampleKernel.Nearest,
            new SamplingOptions(noData: NoDataPolicy.NearestValid, noDataSearchRadiusMeters: 3e9d));

        // The clamped window is the 3x3 raster itself; the tie of cardinal neighbours
        // resolves to row 0, column 1.
        Assert.True(result.IsSampled);
        Assert.Equal(1d, result.Value, 9);
    }

    [Fact]
    public void OutsideFillAccountingDefersUntilTheFillResolves()
    {
        // The query is outside coverage and the clamped edge cell is no-data under the
        // fail policy: the sample fails, and no outside or no-data fill may be counted.
        var raster = SamplingTestSupport.Raster(
            1,
            1,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => -9d,
            noData: -9d,
            hasNoData: true);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(edge: EdgePolicy.Nearest, edgeSearchRadiusMeters: 10d);
        var stats = new SamplingStats();

        var result = sampler.Sample(
            raster,
            0,
            1.5d,
            -0.5d,
            Footprint(1.5d, -0.5d),
            SampleKernel.Nearest,
            options,
            stats);

        Assert.Equal(SampleStatus.Failed, result.Status);
        Assert.Equal(SamplingDiagnosticCodes.NoDataPolicyFailure, result.Diagnostics[0].Code);
        Assert.Equal(0, result.OutsideFills);
        Assert.Equal(0, result.NoDataFills);
        Assert.Equal(0, stats.OutsideFillCount);
        Assert.Equal(0, stats.NoDataFillCount);
        Assert.Equal(1, stats.FailedSampleCount);
        Assert.Null(stats.AffectedBounds);
    }

    private static SampleFootprint Footprint(double centerX, double centerY)
    {
        return SamplingTestSupport.Footprint(centerX, centerY, 0.5d, 0.5d);
    }
}
