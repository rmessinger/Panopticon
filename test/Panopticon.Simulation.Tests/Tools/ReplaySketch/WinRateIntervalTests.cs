using System;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-M5-06: the Wilson score interval matches the published reference values (the classic
/// 10-trial demonstrations), degenerates safely at 0 successes, 0 trials, and all successes,
/// and rejects unsupported inputs.
/// </summary>
public sealed class WinRateIntervalTests
{
    [Theory]
    [InlineData(0, 10, 0.0, 0.2775)]
    [InlineData(5, 10, 0.2366, 0.7634)]
    [InlineData(10, 10, 0.7225, 1.0)]
    [InlineData(0, 0, 0.0, 0.0)]
    public void Wilson_MatchesPublishedValues(int successes, int trials, double expectedLow, double expectedHigh)
    {
        var (low, high) = WinRateInterval.Wilson(successes, trials);

        Assert.Equal(expectedLow, low, 3);
        Assert.Equal(expectedHigh, high, 3);
    }

    [Fact]
    public void Wilson_AllSuccessesStaysInsideUnitInterval()
    {
        var (_, high) = WinRateInterval.Wilson(40, 40);
        Assert.Equal(1.0, high, 6);
    }

    [Fact]
    public void Wilson_NinetyNinePercentIsWiderThanNinetyFive()
    {
        var narrow = WinRateInterval.Wilson(5, 10, 0.95);
        var wide = WinRateInterval.Wilson(5, 10, 0.99);

        Assert.True(wide.Low < narrow.Low, $"99% low {wide.Low} should sit below 95% low {narrow.Low}");
        Assert.True(narrow.High < wide.High, $"95% high {narrow.High} should sit below 99% high {wide.High}");
    }

    [Fact]
    public void Wilson_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WinRateInterval.Wilson(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => WinRateInterval.Wilson(11, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => WinRateInterval.Wilson(5, 10, 0.80));
    }
}
