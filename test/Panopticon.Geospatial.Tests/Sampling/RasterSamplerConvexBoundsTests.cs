// GEO-RAS-001 regression: area-weighted and bilinear sample averages must stay within the
// contributing source range. Both kernels return sum(value * w) / sum(w) over positive-weight
// samples, which is a convex combination, so the exact quotient lies within [min, max] of the
// contributing values (including policy fills). Double accumulation can overshoot that hull by
// a few ulps (a constant 255 source yields 255.00000000000006). The sampler clamps the quotient
// into the contributing range, which removes only that overshoot. Genuine out-of-range source
// values keep their value because the bounds carry them, the clamp applies only to finite
// quotients so overflow still fails, and NaN never reaches the clamp because non-finite samples
// are rejected upstream. Physical averages lie strictly inside the hull and are unchanged.
using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Sampling;
using Xunit;

namespace Panopticon.Geospatial.Tests.Sampling;

public class RasterSamplerConvexBoundsTests
{
    private const double OriginEasting = 639837d;

    private const double OriginNorthing = 5507944d;

    private const double SourceGsd = 0.2d;

    private const double OutputSpacing = 0.3048d;

    private const int RasterSize = 64;

    private const int SweepCount = 256;

    private const double GoldenRatioConjugate = 0.6180339887498949d;

    private const double SqrtTwoMinusOne = 0.41421356237309503d;

    [Theory]
    [InlineData(1d)]
    [InlineData(127d)]
    [InlineData(254d)]
    [InlineData(255d)]
    public void ConstantAreaWeightedSampleEqualsTheConstantAtFractionalFootprints(double value)
    {
        var raster = Raster((_, _) => value, RasterSampleFormat.UInt8);

        foreach (var (fractionX, fractionY) in PhaseSweep())
        {
            var result = SampleAt(raster, fractionX, fractionY, SampleKernel.AreaWeighted);

            Assert.True(result.IsSampled, Describe(result));
            Assert.Equal(SampleKernel.AreaWeighted, result.Selection!.Kernel);
            Assert.InRange(result.Value, 0d, 255d);
            Assert.Equal(value, result.Value);
        }
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(127d)]
    [InlineData(254d)]
    [InlineData(255d)]
    public void ConstantBilinearSampleEqualsTheConstantAtFractionalPositions(double value)
    {
        var raster = Raster((_, _) => value, RasterSampleFormat.UInt8);

        foreach (var (fractionX, fractionY) in PhaseSweep())
        {
            var result = SampleAt(raster, fractionX, fractionY, SampleKernel.Bilinear);

            Assert.True(result.IsSampled, Describe(result));
            Assert.Equal(SampleKernel.Bilinear, result.Selection!.Kernel);
            Assert.InRange(result.Value, 0d, 255d);
            Assert.Equal(value, result.Value);
        }
    }

    [Theory]
    [InlineData(300d, SampleKernel.AreaWeighted)]
    [InlineData(-1d, SampleKernel.AreaWeighted)]
    [InlineData(300d, SampleKernel.Bilinear)]
    [InlineData(-1d, SampleKernel.Bilinear)]
    public void GenuineOutOfRangeSourceValuesRemainOutOfRange(double value, SampleKernel kernel)
    {
        var raster = Raster((_, _) => value, RasterSampleFormat.Float64);

        foreach (var (fractionX, fractionY) in PhaseSweep())
        {
            var result = SampleAt(raster, fractionX, fractionY, kernel);

            Assert.True(result.IsSampled, Describe(result));
            Assert.Equal(value, result.Value);
            Assert.True(result.Value > 255d || result.Value < 0d);
        }
    }

    [Theory]
    [InlineData(SampleKernel.AreaWeighted)]
    [InlineData(SampleKernel.Bilinear)]
    public void NonFiniteSourceValuesStillFailTheSample(SampleKernel kernel)
    {
        var raster = Raster((_, _) => double.NaN, RasterSampleFormat.Float32);

        var result = SampleAt(raster, 0.25d, 0.5d, kernel);

        Assert.False(result.IsSampled);
        Assert.Contains(result.Diagnostics, d => d.Code == SamplingDiagnosticCodes.NoDataPolicyFailure);
    }

    [Fact]
    public void OverflowingAreaWeightedAccumulationStillFailsTheSample()
    {
        // The footprint's clipped cell areas sum to about 2.32, so 1e308 per unit area
        // overflows the accumulator; the non-finite quotient must not be clamped to a
        // finite bound.
        var raster = Raster((_, _) => 1e308d, RasterSampleFormat.Float64);

        var result = SampleAt(raster, 0.25d, 0.5d, SampleKernel.AreaWeighted);

        Assert.False(result.IsSampled);
        Assert.Contains(result.Diagnostics, d => d.Code == SamplingDiagnosticCodes.InvalidSample);
    }

    [Fact]
    public void AreaWeightedSampleAcrossAZeroAndTwoFiftyFiveBoundaryAveragesWithinTheSourceRange()
    {
        var raster = Raster((column, _) => column < 20 ? 0d : 255d, RasterSampleFormat.UInt8);

        // The footprint is centered exactly on the edge between columns 19 and 20, so each
        // side carries equal area and the physical average is the midpoint.
        var centerX = OriginEasting + 4d;
        var centerY = OriginNorthing - 4d;
        var result = Sampler().Sample(
            raster,
            0,
            centerX,
            centerY,
            SamplingTestSupport.Footprint(centerX, centerY, OutputSpacing, OutputSpacing),
            SampleKernel.AreaWeighted);

        Assert.True(result.IsSampled, Describe(result));
        Assert.InRange(result.Value, 0d, 255d);
        Assert.Equal(127.5d, result.Value, 9);
    }

    private static RasterSampler Sampler()
    {
        return new RasterSampler(SamplingTestSupport.IdentityTransformer());
    }

    private static GeospatialRaster<double> Raster(Func<int, int, double> value, RasterSampleFormat sampleFormat)
    {
        var samples = new double[RasterSize * RasterSize];

        for (var row = 0; row < RasterSize; row++)
        {
            for (var column = 0; column < RasterSize; column++)
            {
                samples[(row * RasterSize) + column] = value(column, row);
            }
        }

        return new GeospatialRaster<double>(
            RasterSize,
            RasterSize,
            1,
            SamplingTestSupport.NorthUp(OriginEasting, OriginNorthing, SourceGsd, SourceGsd),
            SamplingTestSupport.Utm31,
            samples,
            sampleFormat: sampleFormat);
    }

    private static RasterSampleResult SampleAt(
        GeospatialRaster<double> raster,
        double fractionX,
        double fractionY,
        SampleKernel kernel)
    {
        var centerX = OriginEasting + 4d + (SourceGsd * fractionX);
        var centerY = OriginNorthing - 4d - (SourceGsd * fractionY);

        return Sampler().Sample(
            raster,
            0,
            centerX,
            centerY,
            SamplingTestSupport.Footprint(centerX, centerY, OutputSpacing, OutputSpacing),
            kernel);
    }

    private static IEnumerable<(double FractionX, double FractionY)> PhaseSweep()
    {
        for (var i = 1; i <= SweepCount; i++)
        {
            yield return (Fraction(i * GoldenRatioConjugate), Fraction(i * SqrtTwoMinusOne));
        }
    }

    private static double Fraction(double value)
    {
        return value - Math.Floor(value);
    }

    private static string Describe(RasterSampleResult result)
    {
        return string.Join("; ", result.Diagnostics.Select(d => d.Message));
    }
}
