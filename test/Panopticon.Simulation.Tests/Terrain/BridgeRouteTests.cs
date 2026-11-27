#nullable enable

using System.Numerics;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// Unit coverage for the planar route frame shared by the generated-bridge layout,
/// corridor plan and tyre pose: axis directions and signs, projection, reconstruction,
/// and the row-vector world/local matrix pair.
/// </summary>
public sealed class BridgeRouteTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void FixtureRoute_AxesLengthAndValidity()
    {
        var route = new BridgeRoute(new Vector2(550f, 300f), new Vector2(550f, 1000f));

        Assert.True(route.IsValid);
        Assert.Equal(700, route.Length, Tolerance);
        Assert.Equal(0, (double)route.Along.X, Tolerance);
        Assert.Equal(1, (double)route.Along.Y, Tolerance);
        Assert.Equal(1, (double)route.Lateral.X, Tolerance);
        Assert.Equal(0, (double)route.Lateral.Y, Tolerance);
    }

    [Fact]
    public void Project_FixtureRoute()
    {
        var route = new BridgeRoute(new Vector2(550f, 300f), new Vector2(550f, 1000f));

        var (s0, n0) = route.Project(550f, 300f);
        Assert.Equal(0, s0, Tolerance);
        Assert.Equal(0, n0, Tolerance);

        var (s1, n1) = route.Project(550f, 1000f);
        Assert.Equal(700, s1, Tolerance);
        Assert.Equal(0, n1, Tolerance);

        var (s2, n2) = route.Project(560f, 350f);
        Assert.Equal(50, s2, Tolerance);
        Assert.Equal(10, n2, Tolerance);
    }

    [Fact]
    public void ToWorld_FixtureRoute()
    {
        var route = new BridgeRoute(new Vector2(550f, 300f), new Vector2(550f, 1000f));

        var world = route.ToWorld(700, -40, 1101.25);

        Assert.Equal(510f, world.X, Tolerance);
        Assert.Equal(1000f, world.Y, Tolerance);
        Assert.Equal(1101.25f, world.Z, Tolerance);
    }

    [Fact]
    public void Project_EastboundRoute_LateralSign()
    {
        var route = new BridgeRoute(new Vector2(0f, 0f), new Vector2(100f, 0f));

        Assert.Equal(1, (double)route.Along.X, Tolerance);
        Assert.Equal(0, (double)route.Along.Y, Tolerance);
        Assert.Equal(0, (double)route.Lateral.X, Tolerance);
        Assert.Equal(-1, (double)route.Lateral.Y, Tolerance);

        var (s0, n0) = route.Project(100f, 0f);
        Assert.Equal(100, s0, Tolerance);
        Assert.Equal(0, n0, Tolerance);

        // A point above the along axis projects to negative N: Lateral points right.
        var (s1, n1) = route.Project(0f, 1f);
        Assert.Equal(0, s1, Tolerance);
        Assert.Equal(-1, n1, Tolerance);

        var (s2, n2) = route.Project(0f, -1f);
        Assert.Equal(0, s2, Tolerance);
        Assert.Equal(1, n2, Tolerance);

        var (s3, n3) = route.Project(50f, 0f);
        Assert.Equal(50, s3, Tolerance);
        Assert.Equal(0, n3, Tolerance);
    }

    // Axes are float-normalized, so the diagonal projections sit about 1.1e-6 off the
    // ideal 100/sqrt(2); this axis coverage pins within 1e-5.
    private const double DiagonalTolerance = 1e-5;

    [Fact]
    public void Project_DiagonalRoute()
    {
        var route = new BridgeRoute(new Vector2(0f, 0f), new Vector2(100f, 100f));

        Assert.Equal(0.70710678, (double)route.Along.X, DiagonalTolerance);
        Assert.Equal(0.70710678, (double)route.Along.Y, DiagonalTolerance);
        Assert.Equal(0.70710678, (double)route.Lateral.X, DiagonalTolerance);
        Assert.Equal(-0.70710678, (double)route.Lateral.Y, DiagonalTolerance);

        var (s0, n0) = route.Project(100f, 0f);
        Assert.Equal(70.710678, s0, DiagonalTolerance);
        Assert.Equal(70.710678, n0, DiagonalTolerance);

        var (s1, n1) = route.Project(0f, 100f);
        Assert.Equal(70.710678, s1, DiagonalTolerance);
        Assert.Equal(-70.710678, n1, DiagonalTolerance);
    }

    [Fact]
    public void WorldToLocal_LocalToWorld_RoundTripsIdentity()
    {
        var routes = new[]
        {
            new BridgeRoute(new Vector2(550f, 300f), new Vector2(550f, 1000f)),
            new BridgeRoute(new Vector2(0f, 0f), new Vector2(100f, 0f)),
            new BridgeRoute(new Vector2(0f, 0f), new Vector2(100f, 100f))
        };

        foreach (var route in routes)
        {
            foreach (var point in new[] { new Vector3(550f, 300f, 1101.25f), new Vector3(123.4f, 567.8f, 9.5f), Vector3.Zero })
            {
                var roundTrip = Vector3.Transform(point, route.WorldToLocal * route.LocalToWorld);

                Assert.Equal(point.X, roundTrip.X, 1e-3f);
                Assert.Equal(point.Y, roundTrip.Y, 1e-3f);
                Assert.Equal(point.Z, roundTrip.Z, 1e-3f);
            }
        }
    }

    [Fact]
    public void LocalToWorld_LocalXIsLateral_LocalYIsAlong()
    {
        var routes = new[]
        {
            new BridgeRoute(new Vector2(0f, 0f), new Vector2(100f, 0f)),
            new BridgeRoute(new Vector2(0f, 0f), new Vector2(100f, 100f))
        };

        foreach (var route in routes)
        {
            foreach (var (s, n, z) in new[] { (10d, 50d, 7d), (33.3d, -12.5d, 4.25d) })
            {
                var local = new Vector3((float)n, (float)s, (float)z);
                var world = Vector3.Transform(local, route.LocalToWorld);
                var expected = route.ToWorld(s, n, z);

                Assert.Equal(expected.X, world.X, 1e-3f);
                Assert.Equal(expected.Y, world.Y, 1e-3f);
                Assert.Equal(expected.Z, world.Z, 1e-3f);
            }
        }
    }

    [Fact]
    public void Project_ToWorld_RoundTripsPoint()
    {
        var route = new BridgeRoute(new Vector2(0f, 0f), new Vector2(100f, 100f));
        var point = new Vector3(37f, 81f, 5f);

        var (s, n) = route.Project(point.X, point.Y);
        var world = route.ToWorld(s, n, 5);

        Assert.Equal(point.X, world.X, 1e-3f);
        Assert.Equal(point.Y, world.Y, 1e-3f);
        Assert.Equal(point.Z, world.Z, 1e-3f);
    }

    [Fact]
    public void DefaultRoute_IsInvalidWithZeroAxes()
    {
        var route = default(BridgeRoute);

        Assert.False(route.IsValid);
        Assert.Equal(Vector2.Zero, route.Along);
        Assert.Equal(Vector2.Zero, route.Lateral);
    }
}
