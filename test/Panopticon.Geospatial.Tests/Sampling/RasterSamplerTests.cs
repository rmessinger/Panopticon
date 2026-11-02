using System;
using System.Linq;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Sampling;
using Xunit;

namespace Panopticon.Geospatial.Tests.Sampling;

public class RasterSamplerTests
{
    private static RasterSampler IdentitySampler()
    {
        return new RasterSampler(SamplingTestSupport.IdentityTransformer());
    }

    [Fact]
    public void NearestSamplesRoundedCellCenter()
    {
        var raster = SamplingTestSupport.Raster(
            3,
            3,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => (10d * row) + column);
        var sampler = IdentitySampler();

        // Cell (1.3, 0.6) rounds to cell (column 1, row 1) = 11.
        var result = sampler.Sample(raster, 0, 1.8d, -1.1d, Footprint(1.8d, -1.1d, 0.5d, 0.5d), SampleKernel.Nearest);

        Assert.True(result.IsSampled);
        Assert.Equal(11d, result.Value, 9);
        Assert.Equal(SampleKernel.Nearest, result.Selection!.Kernel);
    }

    [Fact]
    public void BilinearReproducesPlaneExactly()
    {
        var raster = SamplingTestSupport.Raster(
            4,
            4,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 3d + (2d * column) + (5d * row));
        var sampler = IdentitySampler();

        // Cell (1.25, 0.75); the plane value is 3 + 2*1.25 + 5*0.75 = 9.25.
        var result = sampler.Sample(raster, 0, 1.75d, -1.25d, Footprint(1.75d, -1.25d, 0.5d, 0.5d), SampleKernel.Auto);

        Assert.True(result.IsSampled);
        Assert.Equal(SampleKernel.Bilinear, result.Selection!.Kernel);
        Assert.Equal(9.25d, result.Value, 9);
    }

    [Fact]
    public void BilinearAtExactCellCenterIgnoresZeroWeightNeighbours()
    {
        var raster = SamplingTestSupport.Raster(
            2,
            2,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => column == 0 && row == 0 ? 42d : -9d,
            noData: -9d,
            hasNoData: true);
        var sampler = IdentitySampler();

        // Aligned with cell (0, 0): the three neighbours carry zero weight, so their
        // no-data must not fail the sample even under the default fail policy.
        var result = sampler.Sample(raster, 0, 0.5d, -0.5d, Footprint(0.5d, -0.5d, 0.5d, 0.5d), SampleKernel.Bilinear);

        Assert.True(result.IsSampled);
        Assert.Equal(42d, result.Value, 9);
        Assert.Equal(0, result.NoDataFills);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AreaWeightedTwoByTwoDecimationEqualsBlockMean()
    {
        var raster = SamplingTestSupport.Raster(
            2,
            2,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => (10d * row) + column);
        var sampler = IdentitySampler();

        // Footprint covers both cells exactly: output spacing 2 against GSD 1.
        var result = sampler.Sample(raster, 0, 1d, -1d, Footprint(1d, -1d, 2d, 2d), SampleKernel.Auto);

        Assert.True(result.IsSampled);
        Assert.Equal(SampleKernel.AreaWeighted, result.Selection!.Kernel);
        Assert.Equal(5.5d, result.Value, 9);
    }

    [Fact]
    public void AreaWeightedFourByFourBlockEqualsBlockMean()
    {
        var raster = SamplingTestSupport.Raster(
            4,
            4,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => (10d * row) + column);
        var sampler = IdentitySampler();

        var result = sampler.Sample(raster, 0, 2d, -2d, Footprint(2d, -2d, 4d, 4d), SampleKernel.Auto);

        Assert.True(result.IsSampled);
        Assert.Equal(SampleKernel.AreaWeighted, result.Selection!.Kernel);
        Assert.Equal(16.5d, result.Value, 9);
    }

    [Fact]
    public void AutoSelectsBilinearUpToSourceGsdInclusive()
    {
        var raster = SamplingTestSupport.Raster(
            2,
            2,
            SamplingTestSupport.NorthUp(0d, 0d, 10d, 10d),
            SamplingTestSupport.Utm31,
            (column, row) => 1d);
        var sampler = IdentitySampler();

        var atHalf = sampler.SelectKernel(raster, 5d, -5d, Footprint(5d, -5d, 5d, 5d), SampleKernel.Auto);
        var atLimit = sampler.SelectKernel(raster, 5d, -5d, Footprint(5d, -5d, 10d, 10d), SampleKernel.Auto);

        Assert.Equal(SampleKernel.Bilinear, atLimit.Kernel);
        Assert.Equal(SampleKernel.Bilinear, atHalf.Kernel);
        Assert.Equal(10d, atLimit.SourceGsdXMeters, 9);
        Assert.Equal(10d, atLimit.OutputSpacingXMeters, 9);
        Assert.Equal(1d, atLimit.GsdRatioX, 9);
    }

    [Fact]
    public void AutoSelectsAreaWeightedWhenEitherAxisDecimates()
    {
        // Anisotropic source: 10 m per column step, 40 m per row step.
        var raster = SamplingTestSupport.Raster(
            3,
            3,
            SamplingTestSupport.NorthUp(0d, 0d, 10d, 40d),
            SamplingTestSupport.Utm31,
            (column, row) => 1d);
        var sampler = IdentitySampler();
        var center = Footprint(10d, -20d, 1d, 1d);

        Assert.Equal(
            SampleKernel.Bilinear,
            sampler.SelectKernel(raster, 10d, -20d, Footprint(10d, -20d, 5d, 5d), SampleKernel.Auto).Kernel);
        Assert.Equal(
            SampleKernel.AreaWeighted,
            sampler.SelectKernel(raster, 10d, -20d, Footprint(10d, -20d, 20d, 20d), SampleKernel.Auto).Kernel);
        Assert.Equal(
            SampleKernel.AreaWeighted,
            sampler.SelectKernel(raster, 10d, -20d, Footprint(10d, -20d, 5d, 80d), SampleKernel.Auto).Kernel);

        // An explicit kernel is never overridden by the ratio.
        Assert.Equal(
            SampleKernel.Nearest,
            sampler.SelectKernel(raster, 10d, -20d, center, SampleKernel.Nearest).Kernel);
    }

    [Fact]
    public void GeographicSelectionReportsGsdInMeters()
    {
        // 0.001 degrees per cell; the test transformer maps one degree to 100000 m,
        // so the local GSD is 100 m per axis.
        var raster = SamplingTestSupport.Raster(
            3,
            3,
            SamplingTestSupport.NorthUp(2d, 48d, 0.001d, 0.001d),
            SamplingTestSupport.Wgs84,
            (column, row) => 1d);
        var sampler = new RasterSampler(SamplingTestSupport.ScalingTransformer(100000d, 100000d));

        var selection = sampler.SelectKernel(
            raster,
            2.0015d,
            47.9985d,
            Footprint(200150d, 4799850d, 50d, 50d, SamplingTestSupport.MetricTarget),
            SampleKernel.Auto);

        Assert.Equal(100d, selection.SourceGsdXMeters, 9);
        Assert.Equal(100d, selection.SourceGsdYMeters, 9);
        Assert.Equal(50d, selection.OutputSpacingXMeters, 9);
        Assert.Equal(0.5d, selection.GsdRatioX, 9);
        Assert.Equal(SampleKernel.Bilinear, selection.Kernel);
    }

    [Fact]
    public void GeographicAutoKernelComparesMetersNotDegrees()
    {
        var raster = SamplingTestSupport.Raster(
            3,
            3,
            SamplingTestSupport.NorthUp(2d, 48d, 0.001d, 0.001d),
            SamplingTestSupport.Wgs84,
            (column, row) => 1d);
        var sampler = new RasterSampler(SamplingTestSupport.ScalingTransformer(100000d, 100000d));

        var fine = sampler.SelectKernel(
            raster,
            2.0015d,
            47.9985d,
            Footprint(200150d, 4799850d, 50d, 50d, SamplingTestSupport.MetricTarget),
            SampleKernel.Auto);
        var decimating = sampler.SelectKernel(
            raster,
            2.0015d,
            47.9985d,
            Footprint(200150d, 4799850d, 200d, 200d, SamplingTestSupport.MetricTarget),
            SampleKernel.Auto);

        Assert.Equal(SampleKernel.Bilinear, fine.Kernel);
        Assert.Equal(SampleKernel.AreaWeighted, decimating.Kernel);
    }

    [Fact]
    public void AreaWeightedPartialEdgeAppliesEdgePolicyToUncoveredArea()
    {
        var raster = SamplingTestSupport.Raster(
            2,
            2,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 100d);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(edge: EdgePolicy.Constant, edgeConstantMeters: 50d);

        // Footprint covers cell (0, 0) and one cell width of uncovered area to the west.
        var result = sampler.Sample(raster, 0, 0d, -0.5d, Footprint(0d, -0.5d, 2d, 1d), SampleKernel.Auto, options);

        Assert.True(result.IsSampled);
        Assert.Equal(75d, result.Value, 9);
        Assert.Equal(1, result.OutsideFills);
        Assert.Equal(0, result.NoDataFills);
    }

    [Fact]
    public void OrientationFollowsTheAffineTransform()
    {
        // Both rasters cover the same world strip: north-up row 0 is the northern row,
        // south-up row 0 is the southern row; the sample position is the northern cell.
        var northUp = SamplingTestSupport.Raster(
            1,
            2,
            SamplingTestSupport.NorthUp(0d, 100d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => (10d * row) + 5d);
        var southUp = SamplingTestSupport.Raster(
            1,
            2,
            SamplingTestSupport.SouthUp(0d, 98d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => (10d * row) + 5d);
        var sampler = IdentitySampler();
        var footprint = Footprint(0.5d, 99.5d, 0.1d, 0.1d);

        var fromNorth = sampler.Sample(northUp, 0, 0.5d, 99.5d, footprint, SampleKernel.Nearest);
        var fromSouth = sampler.Sample(southUp, 0, 0.5d, 99.5d, footprint, SampleKernel.Nearest);

        Assert.True(fromNorth.IsSampled);
        Assert.True(fromSouth.IsSampled);
        Assert.Equal(5d, fromNorth.Value, 9);
        Assert.Equal(15d, fromSouth.Value, 9);
    }

    [Fact]
    public void FootprintRejectsGeographicAndDegenerateBounds()
    {
        Assert.Throws<ArgumentException>(
            () => new SampleFootprint(new ProjectedBounds(0d, 0d, 1d, 1d), SamplingTestSupport.Wgs84));
        Assert.Throws<ArgumentException>(() => new ProjectedBounds(1d, 0d, 0d, 1d));
        Assert.Throws<ArgumentException>(() => new ProjectedBounds(0d, 1d, 1d, 1d));
        Assert.Throws<ArgumentException>(() => new ProjectedBounds(double.NaN, 0d, 1d, 1d));
    }

    [Fact]
    public void SampleValidatesArguments()
    {
        var raster = SamplingTestSupport.Raster(
            2,
            2,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 1d,
            bandCount: 2);
        var sampler = IdentitySampler();
        var footprint = Footprint(0.5d, -0.5d, 1d, 1d);

        Assert.Throws<ArgumentNullException>(() => sampler.Sample(null!, 0, 0.5d, -0.5d, footprint, SampleKernel.Nearest));
        Assert.Throws<ArgumentNullException>(() => sampler.Sample(raster, 0, 0.5d, -0.5d, null!, SampleKernel.Nearest));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => sampler.Sample(raster, -1, 0.5d, -0.5d, footprint, SampleKernel.Nearest));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => sampler.Sample(raster, 2, 0.5d, -0.5d, footprint, SampleKernel.Nearest));
        Assert.Throws<ArgumentException>(
            () => sampler.Sample(raster, 0, double.NaN, -0.5d, footprint, SampleKernel.Nearest));
        Assert.Throws<ArgumentException>(
            () => sampler.Sample(raster, 0, 0.5d, double.PositiveInfinity, footprint, SampleKernel.Nearest));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => sampler.Sample(raster, 0, 0.5d, -0.5d, footprint, (SampleKernel)99));
    }

    [Fact]
    public void FootprintLoopsAreResourceBounded()
    {
        var raster = SamplingTestSupport.Raster(
            5,
            5,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 1d);
        var sampler = IdentitySampler();
        var footprint = Footprint(2.5d, -2.5d, 3d, 3d);

        var bounded = sampler.Sample(
            raster,
            0,
            2.5d,
            -2.5d,
            footprint,
            SampleKernel.Auto,
            new SamplingOptions(maxFootprintCells: 4));
        var unbounded = sampler.Sample(raster, 0, 2.5d, -2.5d, footprint, SampleKernel.Auto);

        Assert.Equal(SampleStatus.Failed, bounded.Status);
        Assert.Equal(0d, bounded.Value, 9);
        Assert.Equal(SamplingDiagnosticCodes.ResourceLimit, bounded.Diagnostics[0].Code);
        Assert.True(unbounded.IsSampled);
    }

    [Fact]
    public void WorkBudgetIsCumulativeAcrossNestedSearches()
    {
        // The footprint covers a 2x2 block of no-data cells inside a 3x3 raster whose
        // remaining cells are valid: every overlap visit triggers a nearest-valid search,
        // so the budget must bound the cumulative visits, not each loop alone.
        var raster = SamplingTestSupport.Raster(
            3,
            3,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => column <= 1 && row <= 1 ? -9d : (10d * row) + column,
            noData: -9d,
            hasNoData: true);
        var sampler = IdentitySampler();
        var footprint = Footprint(1d, -1d, 2d, 2d);
        var search = new SamplingOptions(
            noData: NoDataPolicy.NearestValid,
            noDataSearchRadiusMeters: 2.5d);
        var constrained = new SamplingOptions(
            noData: NoDataPolicy.NearestValid,
            noDataSearchRadiusMeters: 2.5d,
            maxFootprintCells: 20);

        var exhausted = sampler.Sample(raster, 0, 1d, -1d, footprint, SampleKernel.Auto, constrained);
        var unbounded = sampler.Sample(raster, 0, 1d, -1d, footprint, SampleKernel.Auto, search);

        Assert.Equal(SampleStatus.Failed, exhausted.Status);
        Assert.Equal(SamplingDiagnosticCodes.ResourceLimit, exhausted.Diagnostics[^1].Code);
        Assert.Equal(1, exhausted.Diagnostics.Count(d => d.Code == SamplingDiagnosticCodes.ResourceLimit));
        Assert.True(unbounded.IsSampled);

        // Each no-data cell fills from its own nearest valid neighbour:
        // (2 + 2 + 20 + 12) / 4.
        Assert.Equal(9d, unbounded.Value, 9);
        Assert.Equal(4, unbounded.NoDataFills);
    }

    [Fact]
    public void ShearedTransformIsRejectedWithTypedDiagnostic()
    {
        // Column step (1, 0), row step (1, 0.01): rotated/sheared affines are rejected
        // instead of being silently mis-sampled.
        var sheared = SamplingTestSupport.Raster(
            3,
            3,
            new AffineTransform(0d, 1d, 1d, 0d, 0.01d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 1d);
        var sampler = IdentitySampler();

        var result = sampler.Sample(sheared, 0, 1.5d, -1.5d, Footprint(1.5d, -1.5d, 1d, 1d), SampleKernel.Nearest);

        Assert.Equal(SampleStatus.Failed, result.Status);
        Assert.Equal(SamplingDiagnosticCodes.InvalidSample, result.Diagnostics[0].Code);
        Assert.Contains("axis-aligned", result.Diagnostics[0].Message);
        Assert.Throws<ArgumentException>(
            () => sampler.SelectKernel(sheared, 1.5d, -1.5d, Footprint(1.5d, -1.5d, 1d, 1d), SampleKernel.Auto));
    }

    [Fact]
    public void SingleCellAxisMeasuresAffineSpacing()
    {
        // One column of two rows [0, 10]; the column axis has no in-raster neighbour, but
        // the affine still defines its spacing, so auto selects bilinear, not area.
        var raster = SamplingTestSupport.Raster(
            1,
            2,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 10d * row);
        var sampler = IdentitySampler();
        var footprint = SamplingTestSupport.Footprint(0.5d, -0.8d, 0.2d, 0.1d);

        var result = sampler.Sample(raster, 0, 0.5d, -0.8d, footprint, SampleKernel.Auto);

        Assert.True(result.IsSampled);
        Assert.Equal(SampleKernel.Bilinear, result.Selection!.Kernel);
        Assert.Equal(1d, result.Selection.SourceGsdXMeters, 9);
        Assert.Equal(3d, result.Value, 9);
    }

    [Fact]
    public void FullyInsideFootprintToleratesClippingResidual()
    {
        // The quad sits fully inside the raster, but floating-point clipping leaves a
        // ~1e-16 residual outside area that must not trip the fail edge policy.
        var raster = SamplingTestSupport.Raster(
            5,
            5,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 3d + (2d * column) + (5d * row));
        var sampler = IdentitySampler();

        var result = sampler.Sample(raster, 0, 2.6d, -2.6d, Footprint(2.6d, -2.6d, 2d, 2d), SampleKernel.Auto);

        Assert.True(result.IsSampled);
        Assert.Equal(0, result.NoDataFills);
        Assert.Equal(0, result.OutsideFills);
        Assert.Empty(result.Diagnostics);

        // Area-weighting a linear field over a symmetric footprint yields the centroid
        // value: 3 + 2*2.1 + 5*2.1.
        Assert.Equal(17.7d, result.Value, 9);
    }

    [Fact]
    public void AreaWeightedOutsideIntegratesVirtualCellsSpatially()
    {
        // One row [0, 10, 20, 30]; the footprint hangs one cell west of the raster, so the
        // uncovered area is the virtual cell at column -1, filled from the clamped edge
        // cell (column 0, value 0): (0 + 10 + 20 + 30 + 0) / 5 = 12.
        var raster = SamplingTestSupport.Raster(
            4,
            1,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 10d * column);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(edge: EdgePolicy.Nearest, edgeSearchRadiusMeters: 2d);

        var result = sampler.Sample(
            raster,
            0,
            1.5d,
            -0.5d,
            SamplingTestSupport.Footprint(1.5d, -0.5d, 5d, 1d),
            SampleKernel.Auto,
            options);
        var tooFar = sampler.Sample(
            raster,
            0,
            1.5d,
            -0.5d,
            SamplingTestSupport.Footprint(1.5d, -0.5d, 5d, 1d),
            SampleKernel.Auto,
            new SamplingOptions(edge: EdgePolicy.Nearest, edgeSearchRadiusMeters: 0.99d));

        Assert.True(result.IsSampled);
        Assert.Equal(12d, result.Value, 9);
        Assert.Equal(1, result.OutsideFills);

        // The virtual cell center is 1 m from the clamped edge cell.
        Assert.Equal(SampleStatus.Failed, tooFar.Status);
        Assert.Equal(SamplingDiagnosticCodes.EdgePolicyFailure, tooFar.Diagnostics[0].Code);
    }

    [Fact]
    public void AreaWeightedVirtualCellsFollowEachRegionsOwnEdge()
    {
        // Footprint hanging one cell west and one cell east: the west virtual cell clamps
        // to column 0 (value 0) and the east virtual cell to column 3 (value 30), so the
        // two outside regions keep their own nearest edge values:
        // (0 + 0 + 10 + 20 + 30 + 30) / 6.
        var raster = SamplingTestSupport.Raster(
            4,
            1,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 10d * column);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(edge: EdgePolicy.Nearest, edgeSearchRadiusMeters: 2d);

        var result = sampler.Sample(
            raster,
            0,
            2d,
            -0.5d,
            SamplingTestSupport.Footprint(2d, -0.5d, 6d, 1d),
            SampleKernel.Auto,
            options);

        Assert.True(result.IsSampled);
        Assert.Equal(15d, result.Value, 9);
        Assert.Equal(2, result.OutsideFills);
    }

    [Fact]
    public void TinyFootprintFarFromPixelOriginKeepsPositiveArea()
    {
        // A 1e-4-cell square near pixel (65000, 65000) sits on a virtual cell: raw shoelace
        // products (~4e9) would cancel its 1e-8 area to zero, but the translated triangle
        // fan keeps it, so the edge constant carries the sample.
        var raster = SamplingTestSupport.Raster(
            4,
            4,
            SamplingTestSupport.SouthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 7d);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(edge: EdgePolicy.Constant, edgeConstantMeters: 50d);

        var result = sampler.Sample(
            raster,
            0,
            65000.55d,
            65000.55d,
            SamplingTestSupport.Footprint(65000.55d, 65000.55d, 0.0001d, 0.0001d),
            SampleKernel.AreaWeighted,
            options);

        Assert.True(result.IsSampled);
        Assert.Equal(50d, result.Value, 9);
        Assert.Equal(1, result.OutsideFills);
    }

    [Fact]
    public void PositiveSliverWeightTriggersNoDataFail()
    {
        // The footprint hangs 1e-12 of a cell over a no-data column: a genuine positive
        // weight that must reach the fail policy, not be rounded away by a tolerance.
        var raster = SamplingTestSupport.Raster(
            2,
            1,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => column == 0 ? 10d : -9d,
            noData: -9d,
            hasNoData: true);
        var sampler = IdentitySampler();

        var result = sampler.Sample(
            raster,
            0,
            0.5000000000005d,
            -0.5d,
            SamplingTestSupport.Footprint(0.5000000000005d, -0.5d, 1.000000000001d, 1d),
            SampleKernel.Auto);

        Assert.Equal(SampleStatus.Failed, result.Status);
        Assert.Equal(SamplingDiagnosticCodes.NoDataPolicyFailure, result.Diagnostics[0].Code);
    }

    [Fact]
    public void AutoKernelFollowsSourcePixelQuadAcrossRotatedTargetAxes()
    {
        // The source affine is axis-aligned, but the target CRS sits a quarter turn away:
        // a tall-thin target footprint maps to a wide source-pixel quad, and auto must
        // see the 20-cell span along the source X axis, not the 0.5 m target width.
        var raster = SamplingTestSupport.Raster(
            3,
            3,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 1d);
        var sampler = new RasterSampler(SamplingTestSupport.RotatingTransformer());

        var tall = sampler.SelectKernel(
            raster,
            0d,
            0d,
            Footprint(0d, 0d, 0.5d, 20d, SamplingTestSupport.MetricTarget),
            SampleKernel.Auto);
        var small = sampler.SelectKernel(
            raster,
            0d,
            0d,
            Footprint(0d, 0d, 0.5d, 0.5d, SamplingTestSupport.MetricTarget),
            SampleKernel.Auto);

        Assert.Equal(SampleKernel.AreaWeighted, tall.Kernel);
        Assert.Equal(20d, tall.OutputSpacingXMeters, 9);
        Assert.Equal(0.5d, tall.OutputSpacingYMeters, 9);
        Assert.Equal(20d, tall.GsdRatioX, 9);
        Assert.Equal(SampleKernel.Bilinear, small.Kernel);
    }

    [Fact]
    public void FootprintBeyondAddressableCellBoundsFailsImmediately()
    {
        // Footprints at or beyond the int cell-index range must fail with a typed bounds
        // diagnostic up front: the old clamp let the loop induction overflow and burn the
        // whole work budget on bogus iterations under an unrelated message.
        var raster = SamplingTestSupport.Raster(
            2,
            2,
            SamplingTestSupport.NorthUp(0d, 0d, 1d, 1d),
            SamplingTestSupport.Utm31,
            (column, row) => 1d);
        var sampler = IdentitySampler();
        var options = new SamplingOptions(edge: EdgePolicy.Constant, edgeConstantMeters: 50d);

        var atLimit = sampler.Sample(
            raster,
            0,
            2147483647d,
            -0.5d,
            Footprint(2147483647d, -0.5d, 2d, 1d),
            SampleKernel.Auto,
            options);
        var farBeyond = sampler.Sample(
            raster,
            0,
            5e9d,
            -0.5d,
            Footprint(5e9d, -0.5d, 2d, 1d),
            SampleKernel.Auto,
            options);
        var negativeBeyond = sampler.Sample(
            raster,
            0,
            -2147483649d,
            -0.5d,
            Footprint(-2147483649d, -0.5d, 2d, 1d),
            SampleKernel.Auto,
            options);

        Assert.Equal(SampleStatus.Failed, atLimit.Status);
        Assert.Equal(SamplingDiagnosticCodes.ResourceLimit, atLimit.Diagnostics[0].Code);
        Assert.Contains("addressable", atLimit.Diagnostics[0].Message);
        Assert.Equal(SampleStatus.Failed, farBeyond.Status);
        Assert.Contains("addressable", farBeyond.Diagnostics[0].Message);
        Assert.Equal(SampleStatus.Failed, negativeBeyond.Status);
        Assert.Contains("addressable", negativeBeyond.Diagnostics[0].Message);
    }

    private static SampleFootprint Footprint(
        double centerX,
        double centerY,
        double spacingX,
        double spacingY,
        CoordinateSystemId? target = null)
    {
        return SamplingTestSupport.Footprint(centerX, centerY, spacingX, spacingY, target);
    }
}
