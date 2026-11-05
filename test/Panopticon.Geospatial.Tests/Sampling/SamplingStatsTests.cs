#nullable enable

using System;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Sampling;
using Xunit;
using static Panopticon.Geospatial.Tests.Sampling.SamplingTestSupport;

namespace Panopticon.Geospatial.Tests.Sampling;

public class SamplingStatsTests
{
    [Fact]
    public void Record_CountsSamplesFillsFailuresAndBounds()
    {
        var stats = new SamplingStats();

        stats.Record(Sampled(), Footprint(0d, 0d, 10d, 10d));
        stats.Record(Failed(fills: (NoData: 2, Outside: 1)), Footprint(10d, 0d, 20d, 10d));
        stats.Record(Sampled(fills: (NoData: 1, Outside: 0)), Footprint(20d, 0d, 30d, 10d));

        Assert.Equal(3, stats.SampleCount);
        Assert.Equal(1, stats.FailedSampleCount);
        Assert.Equal(3, stats.NoDataFillCount);
        Assert.Equal(1, stats.OutsideFillCount);
        Assert.NotNull(stats.AffectedBounds);

        // The first sample needed no fill, so the union starts at the second footprint.
        Assert.Equal(10d, stats.AffectedBounds!.MinEasting);
        Assert.Equal(30d, stats.AffectedBounds.MaxEasting);
    }

    [Fact]
    public void Record_ChargesKernelCountsFromEverySelectionIncludingFailed()
    {
        var stats = new SamplingStats();

        stats.Record(Sampled(Selection(SampleKernel.Bilinear, 10d, 10d, 10d, 10d)), Footprint());
        stats.Record(Failed(selection: Selection(SampleKernel.Bilinear, 10d, 10d, 10d, 10d)), Footprint());
        stats.Record(Sampled(Selection(SampleKernel.AreaWeighted, 10d, 10d, 30d, 30d)), Footprint());
        stats.Record(Failed(selection: null), Footprint());

        Assert.Equal(2, stats.GetKernelSampleCount(SampleKernel.Bilinear));
        Assert.Equal(1, stats.GetKernelSampleCount(SampleKernel.AreaWeighted));
        Assert.Equal(0, stats.GetKernelSampleCount(SampleKernel.Nearest));
        Assert.Equal(0, stats.GetKernelSampleCount(SampleKernel.Auto));
        Assert.Equal(2, stats.FailedSampleCount);
    }

    [Fact]
    public void Record_TracksObservedMinAndMaxFromSelections()
    {
        var stats = new SamplingStats();

        stats.Record(Sampled(Selection(SampleKernel.Bilinear, 10d, 12d, 10d, 12d)), Footprint());
        stats.Record(Sampled(Selection(SampleKernel.Bilinear, 20d, 18d, 40d, 36d)), Footprint());
        stats.Record(Failed(selection: Selection(SampleKernel.Nearest, 5d, 5d, 5d, 5d)), Footprint());

        Assert.True(stats.TryGetObservedSourceGsdXMeters(out var gsdMinX, out var gsdMaxX));
        Assert.Equal(5d, gsdMinX);
        Assert.Equal(20d, gsdMaxX);

        Assert.True(stats.TryGetObservedSourceGsdYMeters(out var gsdMinY, out var gsdMaxY));
        Assert.Equal(5d, gsdMinY);
        Assert.Equal(18d, gsdMaxY);

        Assert.True(stats.TryGetObservedOutputSpacingXMeters(out var spacingMinX, out var spacingMaxX));
        Assert.Equal(5d, spacingMinX);
        Assert.Equal(40d, spacingMaxX);

        Assert.True(stats.TryGetObservedOutputSpacingYMeters(out _, out var spacingMaxY));
        Assert.Equal(36d, spacingMaxY);

        Assert.True(stats.TryGetObservedGsdRatioX(out var ratioMinX, out var ratioMaxX));
        Assert.Equal(1d, ratioMinX);
        Assert.Equal(2d, ratioMaxX);

        Assert.True(stats.TryGetObservedGsdRatioY(out var ratioMinY, out var ratioMaxY));
        Assert.Equal(1d, ratioMinY);
        Assert.Equal(2d, ratioMaxY);
    }

    [Fact]
    public void Record_SkipsNonFiniteObservations()
    {
        var stats = new SamplingStats();

        // The NaN source GSD and the zero-GSD infinite ratio are skipped; the finite
        // values of both selections are still observed.
        stats.Record(Sampled(Selection(SampleKernel.Bilinear, 10d, 0d, 10d, 10d)), Footprint());
        stats.Record(Sampled(Selection(SampleKernel.Bilinear, double.NaN, 8d, 4d, 4d)), Footprint());

        Assert.True(stats.TryGetObservedSourceGsdXMeters(out _, out var gsdMaxX));
        Assert.Equal(10d, gsdMaxX);

        Assert.True(stats.TryGetObservedSourceGsdYMeters(out var gsdMinY, out var gsdMaxY));
        Assert.Equal(0d, gsdMinY);
        Assert.Equal(8d, gsdMaxY);

        Assert.True(stats.TryGetObservedGsdRatioX(out var ratioMinX, out var ratioMaxX));
        Assert.Equal(1d, ratioMinX);
        Assert.Equal(1d, ratioMaxX);

        Assert.True(stats.TryGetObservedGsdRatioY(out var ratioMinY, out var ratioMaxY));
        Assert.Equal(0.5d, ratioMinY);
        Assert.Equal(0.5d, ratioMaxY);
    }

    [Fact]
    public void ObservedRanges_AreUnavailableBeforeAnySelection()
    {
        var stats = new SamplingStats();

        stats.Record(Sampled(selection: null), Footprint());

        Assert.False(stats.TryGetObservedSourceGsdXMeters(out _, out _));
        Assert.False(stats.TryGetObservedSourceGsdYMeters(out _, out _));
        Assert.False(stats.TryGetObservedOutputSpacingXMeters(out _, out _));
        Assert.False(stats.TryGetObservedOutputSpacingYMeters(out _, out _));
        Assert.False(stats.TryGetObservedGsdRatioX(out _, out _));
        Assert.False(stats.TryGetObservedGsdRatioY(out _, out _));
    }

    [Fact]
    public void GetKernelSampleCount_RejectsUndefinedKernel()
    {
        var stats = new SamplingStats();

        Assert.Throws<ArgumentOutOfRangeException>(() => stats.GetKernelSampleCount((SampleKernel)99));
    }

    [Fact]
    public void Record_RejectsUndefinedKernelInSelection()
    {
        var stats = new SamplingStats();
        var result = Sampled(Selection((SampleKernel)99, 1d, 1d, 1d, 1d));

        Assert.Throws<ArgumentOutOfRangeException>(() => stats.Record(result, Footprint()));
    }

    private static RasterSampleResult Sampled(
        KernelSelection? selection = null,
        (int NoData, int Outside) fills = default)
    {
        return Result(SampleStatus.Sampled, selection, fills);
    }

    private static RasterSampleResult Failed(
        KernelSelection? selection = null,
        (int NoData, int Outside) fills = default)
    {
        return Result(SampleStatus.Failed, selection, fills);
    }

    private static RasterSampleResult Result(SampleStatus status, KernelSelection? selection, (int NoData, int Outside) fills)
    {
        return new RasterSampleResult
        {
            Status = status,
            Value = status == SampleStatus.Sampled ? 5d : 0d,
            Selection = selection,
            NoDataFills = fills.NoData,
            OutsideFills = fills.Outside,
            Diagnostics = [],
        };
    }

    private static KernelSelection Selection(SampleKernel kernel, double gsdX, double gsdY, double spacingX, double spacingY)
    {
        return new KernelSelection(kernel, gsdX, gsdY, spacingX, spacingY);
    }

    internal static SampleFootprint Footprint(double minEasting, double minNorthing, double maxEasting, double maxNorthing)
    {
        return new SampleFootprint(new ProjectedBounds(minEasting, minNorthing, maxEasting, maxNorthing), Utm31);
    }

    private static SampleFootprint Footprint()
    {
        return Footprint(0d, 0d, 10d, 10d);
    }
}
