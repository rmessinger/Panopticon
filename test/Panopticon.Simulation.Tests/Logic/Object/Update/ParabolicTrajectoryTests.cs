using System.Numerics;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic.Object.Update;

public class ParabolicTrajectoryTests
{
    [Fact]
    public void CurvePassesThroughLaunchAndImpact()
    {
        var t = ParabolicTrajectory.FromApexHeightFactor(new Vector3(0, 0, 10), new Vector3(100, 0, 0), 1.0f);

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
        var t = ParabolicTrajectory.FromApexHeightFactor(Vector3.Zero, new Vector3(300, 0, 0), 1.0f);
        Assert.Equal(300f, t.PointAt(150).Z, 3);

        var half = ParabolicTrajectory.FromApexHeightFactor(Vector3.Zero, new Vector3(300, 0, 0), 0.5f);
        Assert.Equal(150f, half.PointAt(150).Z, 3);
    }

    [Fact]
    public void ApexIgnoresEndpointHeights()
    {
        var t = ParabolicTrajectory.FromApexHeightFactor(new Vector3(0, 0, 20), new Vector3(200, 0, 40), 1.0f);

        Assert.Equal(200f, t.PointAt(100).Z, 3);
    }

    [Fact]
    public void ProgressProjectsOntoAxisAndClamps()
    {
        var t = ParabolicTrajectory.FromApexHeightFactor(Vector3.Zero, new Vector3(100, 0, 0), 1.0f);

        Assert.Equal(0f, t.Progress(new Vector3(-10, 0, 0)), 3);
        Assert.Equal(40f, t.Progress(new Vector3(40, 5, 0)), 3);
        Assert.Equal(100f, t.Progress(new Vector3(500, 0, 0)), 3);
    }

    [Fact]
    public void AxisFollowsImpactDirection()
    {
        var t = ParabolicTrajectory.FromApexHeightFactor(Vector3.Zero, new Vector3(0, 100, 0), 1.0f);

        Assert.Equal(30f, t.Progress(new Vector3(7, 30, 0)), 3);
        Assert.Equal(50f, t.PointAt(50).Y, 3);
    }

    [Fact]
    public void SteerPointClampsToImpact()
    {
        var t = ParabolicTrajectory.FromApexHeightFactor(Vector3.Zero, new Vector3(100, 0, 0), 1.0f);

        var end = t.SteerPoint(new Vector3(90, 0, 0), 80f);

        Assert.Equal(100f, end.X, 3);
        Assert.Equal(0f, end.Z, 3);
    }

    [Fact]
    public void SteerPointLooksAheadAlongCurve()
    {
        var t = ParabolicTrajectory.FromApexHeightFactor(Vector3.Zero, new Vector3(100, 0, 0), 1.0f);

        var point = t.SteerPoint(new Vector3(10, 0, 0), 20f);

        Assert.Equal(30f, point.X, 3);
        // z(0.3) = 100 * 4 * 0.3 * 0.7 = 84
        Assert.Equal(84f, point.Z, 3);
    }

    [Fact]
    public void DegenerateZeroRangeSteersAtLaunch()
    {
        var t = ParabolicTrajectory.FromApexHeightFactor(new Vector3(5, 5, 5), new Vector3(5, 5, 5), 1.0f);

        Assert.Equal(new Vector3(5, 5, 5), t.SteerPoint(new Vector3(5, 5, 5), 80f));
    }

    [Fact]
    public void LaunchDirectionSetsTheLaunchTangent()
    {
        // Battery angled 45 degrees up, flat 100-unit shot: the initial slope is 1, so the
        // apex is range/4 = 25 and the first stretch of the curve climbs at 45 degrees.
        var t = ParabolicTrajectory.FromLaunchDirection(Vector3.Zero, new Vector3(100, 0, 0), new Vector3(1, 0, 1));

        Assert.Equal(25f, t.PointAt(50).Z, 3);
        // z(5) = 100 * 4 * 0.05 * 0.95 = 4.75, just under the 45-degree tangent line (5).
        Assert.Equal(4.75f, t.PointAt(5).Z, 3);
        Assert.Equal(0f, t.PointAt(100).Z, 3);
    }

    [Fact]
    public void LaunchDirectionScalesApexWithRange()
    {
        var shortShot = ParabolicTrajectory.FromLaunchDirection(Vector3.Zero, new Vector3(50, 0, 0), new Vector3(1, 0, 1));
        var longShot = ParabolicTrajectory.FromLaunchDirection(Vector3.Zero, new Vector3(300, 0, 0), new Vector3(1, 0, 1));

        Assert.Equal(12.5f, shortShot.PointAt(25).Z, 3);
        Assert.Equal(75f, longShot.PointAt(150).Z, 3);
    }

    [Fact]
    public void LaunchDirectionBelowChordFliesTheChord()
    {
        // Aiming level at an elevated target: no arch can match a level launch tangent there,
        // so the arc degenerates to the straight chord line.
        var t = ParabolicTrajectory.FromLaunchDirection(Vector3.Zero, new Vector3(100, 0, 50), Vector3.UnitX);

        Assert.Equal(25f, t.PointAt(50).Z, 3);
        Assert.Equal(0f, t.PointAt(0).Z, 3);
        Assert.Equal(50f, t.PointAt(100).Z, 3);
    }
}
