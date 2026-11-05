using System;

namespace Panopticon.Geospatial.Sampling;

/// <summary>
/// Report accumulator for sampling. The sampler records each result; fills and failures
/// are counted, and every result carrying a <see cref="KernelSelection"/> contributes a
/// bounded kernel count plus the finite min and max of each observed GSD, spacing, and
/// ratio value, failed samples included; non-finite observations are skipped.
/// </summary>
public sealed class SamplingStats
{
    private readonly long[] _kernelCounts = new long[Enum.GetValues<SampleKernel>().Length];

    private double _minSourceGsdX = double.PositiveInfinity;
    private double _maxSourceGsdX = double.NegativeInfinity;
    private double _minSourceGsdY = double.PositiveInfinity;
    private double _maxSourceGsdY = double.NegativeInfinity;
    private double _minOutputSpacingX = double.PositiveInfinity;
    private double _maxOutputSpacingX = double.NegativeInfinity;
    private double _minOutputSpacingY = double.PositiveInfinity;
    private double _maxOutputSpacingY = double.NegativeInfinity;
    private double _minGsdRatioX = double.PositiveInfinity;
    private double _maxGsdRatioX = double.NegativeInfinity;
    private double _minGsdRatioY = double.PositiveInfinity;
    private double _maxGsdRatioY = double.NegativeInfinity;

    public long SampleCount { get; private set; }

    public long FailedSampleCount { get; private set; }

    /// <summary>Total in-raster no-data fill events.</summary>
    public long NoDataFillCount { get; private set; }

    /// <summary>Total outside-coverage fill events.</summary>
    public long OutsideFillCount { get; private set; }

    public ProjectedBounds? AffectedBounds { get; private set; }

    public void Record(RasterSampleResult result, SampleFootprint footprint)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(footprint);

        SampleCount++;
        NoDataFillCount += result.NoDataFills;
        OutsideFillCount += result.OutsideFills;

        if (result.Status == SampleStatus.Failed)
        {
            FailedSampleCount++;
        }

        if (result.NoDataFills > 0 || result.OutsideFills > 0)
        {
            AffectedBounds = AffectedBounds?.Union(footprint.Bounds) ?? footprint.Bounds;
        }

        if (result.Selection is { } selection)
        {
            RecordSelection(selection);
        }
    }

    /// <summary>Samples recorded so far that selected the given kernel.</summary>
    public long GetKernelSampleCount(SampleKernel kernel)
    {
        if (!Enum.IsDefined(kernel))
        {
            throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Undefined kernel.");
        }

        return _kernelCounts[(int)kernel];
    }

    /// <summary>Observed source GSD range on the X axis; false before the first finite observation.</summary>
    public bool TryGetObservedSourceGsdXMeters(out double min, out double max) =>
        TryGetRange(_minSourceGsdX, _maxSourceGsdX, out min, out max);

    /// <summary>Observed source GSD range on the Y axis; false before the first finite observation.</summary>
    public bool TryGetObservedSourceGsdYMeters(out double min, out double max) =>
        TryGetRange(_minSourceGsdY, _maxSourceGsdY, out min, out max);

    /// <summary>Observed output-spacing range on the X axis; false before the first finite observation.</summary>
    public bool TryGetObservedOutputSpacingXMeters(out double min, out double max) =>
        TryGetRange(_minOutputSpacingX, _maxOutputSpacingX, out min, out max);

    /// <summary>Observed output-spacing range on the Y axis; false before the first finite observation.</summary>
    public bool TryGetObservedOutputSpacingYMeters(out double min, out double max) =>
        TryGetRange(_minOutputSpacingY, _maxOutputSpacingY, out min, out max);

    /// <summary>Observed GSD-ratio range on the X axis; false before the first finite observation.</summary>
    public bool TryGetObservedGsdRatioX(out double min, out double max) =>
        TryGetRange(_minGsdRatioX, _maxGsdRatioX, out min, out max);

    /// <summary>Observed GSD-ratio range on the Y axis; false before the first finite observation.</summary>
    public bool TryGetObservedGsdRatioY(out double min, out double max) =>
        TryGetRange(_minGsdRatioY, _maxGsdRatioY, out min, out max);

    private void RecordSelection(KernelSelection selection)
    {
        if (!Enum.IsDefined(selection.Kernel))
        {
            throw new ArgumentOutOfRangeException(nameof(selection), selection.Kernel, "Undefined kernel.");
        }

        _kernelCounts[(int)selection.Kernel]++;
        Observe(selection.SourceGsdXMeters, ref _minSourceGsdX, ref _maxSourceGsdX);
        Observe(selection.SourceGsdYMeters, ref _minSourceGsdY, ref _maxSourceGsdY);
        Observe(selection.OutputSpacingXMeters, ref _minOutputSpacingX, ref _maxOutputSpacingX);
        Observe(selection.OutputSpacingYMeters, ref _minOutputSpacingY, ref _maxOutputSpacingY);
        Observe(selection.GsdRatioX, ref _minGsdRatioX, ref _maxGsdRatioX);
        Observe(selection.GsdRatioY, ref _minGsdRatioY, ref _maxGsdRatioY);
    }

    private static void Observe(double value, ref double min, ref double max)
    {
        if (!double.IsFinite(value))
        {
            return;
        }

        if (value < min)
        {
            min = value;
        }

        if (value > max)
        {
            max = value;
        }
    }

    private static bool TryGetRange(double min, double max, out double minResult, out double maxResult)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            minResult = 0d;
            maxResult = 0d;
            return false;
        }

        minResult = min;
        maxResult = max;
        return true;
    }
}
