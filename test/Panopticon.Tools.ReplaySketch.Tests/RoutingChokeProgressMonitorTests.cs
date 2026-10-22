using System.Numerics;
using Panopticon.Tools.TestHarness;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class RoutingChokeProgressMonitorTests
{
    [Fact]
    public void RetainedWaypointsDoNotPreventDeadlockTermination()
    {
        var monitor = new RoutingChokeProgressMonitor([Vector2.Zero], 90);
        Assert.Null(monitor.Observe(89, [Vector2.Zero], true, false));
        Assert.Equal("NoProgress", monitor.Observe(90, [Vector2.Zero], true, false));
    }

    [Fact]
    public void AnyMovingUnitResetsTimeoutIncludingSlowAccumulatedMovement()
    {
        var monitor = new RoutingChokeProgressMonitor([Vector2.Zero, Vector2.Zero], 90);
        Assert.Null(monitor.Observe(89, [Vector2.Zero, new Vector2(0.5f, 0)], true, false));
        Assert.Null(monitor.Observe(90, [Vector2.Zero, Vector2.UnitX], true, false));
        Assert.Null(monitor.Observe(179, [Vector2.Zero, Vector2.UnitX], true, false));
        Assert.Equal("NoProgress", monitor.Observe(180, [Vector2.Zero, Vector2.UnitX], true, false));
    }

    [Fact]
    public void ExhaustedPathsStopImmediately()
    {
        var monitor = new RoutingChokeProgressMonitor([Vector2.Zero], 90);
        Assert.Equal("PathsExhausted", monitor.Observe(1, [Vector2.Zero], false, false));
    }

    [Fact]
    public void SubUnitJitterDoesNotKeepRunAlive()
    {
        var monitor = new RoutingChokeProgressMonitor([Vector2.Zero], 90);
        Assert.Null(monitor.Observe(89, [new Vector2(0.1f, 0)], true, false));
        Assert.Equal("NoProgress", monitor.Observe(90, [new Vector2(-0.1f, 0)], true, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TimedGateSuppressesEarlyTermination(bool hasPaths)
    {
        var monitor = new RoutingChokeProgressMonitor([Vector2.Zero], 90);
        Assert.Null(monitor.Observe(900, [Vector2.Zero], hasPaths, true));
    }
}
