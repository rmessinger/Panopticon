using System.Numerics;
using OpenSage.Logic.Object;
using Xunit;

namespace OpenSage.Tests.Logic.Object.Update;

public class ParabolicTrajectoryTests
{
    [Fact]
    public void CurvePassesThroughLaunchAndImpact()
    {
        var t = new ParabolicTrajectory(new Vector3(0, 0, 10), new Vector3(100, 0, 0), 1.0f);

        var launch = t.PointAt(0);
        var impact = t.PointAt(100);

        Assert.Equal(0f, launch.X, 3);
        Assert.Equal(10f, launch.Z, 3);
        Assert.Equal(100f, impact.X, 3);
        Assert.Equal(0f, impact.Z, 3);
    }

    [Fact]
    public void ApexHeightScalesWithRangeAndFactor()
    {
        var t = new ParabolicTrajectory(Vector3.Zero, new Vector3(300, 0, 0), 1.0f);
        Assert.Equal(300f, t.PointAt(150).Z, 3);

        var half = new ParabolicTrajectory(Vector3.Zero, new Vector3(300, 0, 0), 0.5f);
        Assert.Equal(150f, half.PointAt(150).Z, 3);
    }

    [Fact]
    public void ApexIgnoresEndpointHeights()
    {
        var t = new ParabolicTrajectory(new Vector3(0, 0, 20), new Vector3(200, 0, 40), 1.0f);

        Assert.Equal(200f, t.PointAt(100).Z, 3);
    }

    [Fact]
    public void ProgressProjectsOntoAxisAndClamps()
    {
        var t = new ParabolicTrajectory(Vector3.Zero, new Vector3(100, 0, 0), 1.0f);

        Assert.Equal(0f, t.Progress(new Vector3(-10, 0, 0)), 3);
        Assert.Equal(40f, t.Progress(new Vector3(40, 5, 0)), 3);
        Assert.Equal(100f, t.Progress(new Vector3(500, 0, 0)), 3);
    }

    [Fact]
    public void AxisFollowsImpactDirection()
    {
        var t = new ParabolicTrajectory(Vector3.Zero, new Vector3(0, 100, 0), 1.0f);

        Assert.Equal(30f, t.Progress(new Vector3(7, 30, 0)), 3);
        Assert.Equal(50f, t.PointAt(50).Y, 3);
    }

    [Fact]
    public void SteerPointClampsToImpact()
    {
        var t = new ParabolicTrajectory(Vector3.Zero, new Vector3(100, 0, 0), 1.0f);

        var end = t.SteerPoint(new Vector3(90, 0, 0), 80f);

        Assert.Equal(100f, end.X, 3);
        Assert.Equal(0f, end.Z, 3);
    }

    [Fact]
    public void SteerPointLooksAheadAlongCurve()
    {
        var t = new ParabolicTrajectory(Vector3.Zero, new Vector3(100, 0, 0), 1.0f);

        var point = t.SteerPoint(new Vector3(10, 0, 0), 20f);

        Assert.Equal(30f, point.X, 3);
        // z(0.3) = 100 * 4 * 0.3 * 0.7 = 84
        Assert.Equal(84f, point.Z, 3);
    }

    [Fact]
    public void DegenerateZeroRangeSteersAtLaunch()
    {
        var t = new ParabolicTrajectory(new Vector3(5, 5, 5), new Vector3(5, 5, 5), 1.0f);

        Assert.Equal(new Vector3(5, 5, 5), t.SteerPoint(new Vector3(5, 5, 5), 80f));
    }
}
