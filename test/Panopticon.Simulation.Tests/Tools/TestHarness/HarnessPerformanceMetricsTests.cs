using Panopticon.Tools.TestHarness;
using Xunit;

namespace Panopticon.Tests.Tools.TestHarness;

public class HarnessPerformanceMetricsTests
{
    [Fact]
    public void RecordPublishesOneSecondAveragesAndResetsWindow()
    {
        var metrics = new HarnessPerformanceMetrics();
        metrics.Record(0.5, 2, 4);
        Assert.Equal(0, metrics.FramesPerSecond);
        metrics.Record(0.5, 4, 8);
        Assert.Equal(2, metrics.FramesPerSecond);
        Assert.Equal(500, metrics.FrameMilliseconds);
        Assert.Equal(3, metrics.UpdateMilliseconds);
        Assert.Equal(6, metrics.RenderMilliseconds);
        Assert.True(metrics.WorkingSetBytes > 0);
        metrics.Record(1, 10, 20);
        Assert.Equal(1, metrics.FramesPerSecond);
        Assert.Equal(10, metrics.UpdateMilliseconds);
        Assert.Equal(20, metrics.RenderMilliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidFrameDurationDoesNotAffectAverages(double seconds)
    {
        var metrics = new HarnessPerformanceMetrics();
        metrics.Record(seconds, 100, 100);
        metrics.Record(1, 2, 4);
        Assert.Equal(1, metrics.FramesPerSecond);
        Assert.Equal(2, metrics.UpdateMilliseconds);
        Assert.Equal(4, metrics.RenderMilliseconds);
    }

    [Fact]
    public void RecordGpuPublishesOneSecondAverage()
    {
        var metrics = new HarnessPerformanceMetrics();
        metrics.RecordGpu(4);
        metrics.RecordGpu(8);
        Assert.Null(metrics.GpuMilliseconds);
        metrics.Record(1, 1, 1);
        Assert.Equal(6, metrics.GpuMilliseconds);
        metrics.RecordGpu(3);
        metrics.Record(1, 1, 1);
        Assert.Equal(3, metrics.GpuMilliseconds);
    }

    [Fact]
    public void WindowWithoutGpuSamplesClearsGpuAverage()
    {
        var metrics = new HarnessPerformanceMetrics();
        metrics.RecordGpu(5);
        metrics.Record(1, 1, 1);
        Assert.Equal(5, metrics.GpuMilliseconds);
        metrics.Record(1, 1, 1);
        Assert.Null(metrics.GpuMilliseconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidGpuDurationDoesNotAffectAverages(double milliseconds)
    {
        var metrics = new HarnessPerformanceMetrics();
        metrics.RecordGpu(milliseconds);
        metrics.RecordGpu(2);
        metrics.Record(1, 1, 1);
        Assert.Equal(2, metrics.GpuMilliseconds);
    }
}
