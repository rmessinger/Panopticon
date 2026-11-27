#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// Pure unit coverage for the generated-bridge trim geometry: plane clipping keeps the
/// Y &lt;= yClip side with exact crossings, and cap welding, loop tracing and ear clipping
/// close each trimmed piece with +Y facing triangles.
/// </summary>
public sealed class GeneratedBridgeTrimGeometryTests
{
    [Fact]
    public void ClipBelowPlane_AllInside_KeepsVertexOrderWithoutCrossings()
    {
        var crossings = new List<(double X, double Z)>();
        var polygon = new List<(double X, double Y, double Z)> { (0d, 0d, 0d), (1d, 0d, 0d), (0d, 1d, 0d) };

        var clipped = GeneratedBridgeTrimGeometry.ClipBelowPlane(polygon, 1d, crossings);

        Assert.Equal(polygon, clipped);
        Assert.Empty(crossings);
    }

    [Fact]
    public void ClipBelowPlane_AllOutside_ReturnsNoVertexOrCrossing()
    {
        var crossings = new List<(double X, double Z)>();
        var polygon = new List<(double X, double Y, double Z)> { (0d, 2d, 0d), (1d, 2d, 0d), (0d, 3d, 0d) };

        var clipped = GeneratedBridgeTrimGeometry.ClipBelowPlane(polygon, 1d, crossings);

        Assert.Empty(clipped);
        Assert.Empty(crossings);
    }

    [Fact]
    public void ClipBelowPlane_OneVertexInside_ReturnsTriangleWithTwoCrossings()
    {
        var crossings = new List<(double X, double Z)>();
        var polygon = new List<(double X, double Y, double Z)> { (0d, 0d, 0d), (2d, 2d, 0d), (0d, 2d, 0d) };

        var clipped = GeneratedBridgeTrimGeometry.ClipBelowPlane(polygon, 1d, crossings);

        Assert.Equal(new List<(double X, double Y, double Z)> { (0d, 0d, 0d), (1d, 1d, 0d), (0d, 1d, 0d) }, clipped);
        Assert.Equal(new List<(double X, double Z)> { (1d, 0d), (0d, 0d) }, crossings);
    }

    [Fact]
    public void ClipBelowPlane_TwoVerticesInside_ReturnsQuadWithTwoCrossings()
    {
        var crossings = new List<(double X, double Z)>();
        var polygon = new List<(double X, double Y, double Z)> { (0d, 0d, 0d), (2d, 0d, 0d), (0d, 2d, 0d) };

        var clipped = GeneratedBridgeTrimGeometry.ClipBelowPlane(polygon, 1d, crossings);

        Assert.Equal(new List<(double X, double Y, double Z)> { (0d, 0d, 0d), (2d, 0d, 0d), (1d, 1d, 0d), (0d, 1d, 0d) }, clipped);
        Assert.Equal(2, crossings.Count);
    }

    [Fact]
    public void ClipBelowPlane_EmptyPolygon_ReturnsEmpty()
    {
        var crossings = new List<(double X, double Z)>();

        var clipped = GeneratedBridgeTrimGeometry.ClipBelowPlane(new List<(double X, double Y, double Z)>(), 1d, crossings);

        Assert.Empty(clipped);
        Assert.Empty(crossings);
    }

    [Fact]
    public void BuildCaps_PerturbedSquareRing_WeldsEndpointAndCapsTwoTriangles()
    {
        // The (2.00004, 2.00003) endpoint sits inside the 1e-4 weld key of (2, 2).
        var segments = new[]
        {
            ((0d, 0d), (2d, 0d)),
            ((2d, 0d), (2d, 2d)),
            ((2.00004d, 2.00003d), (0d, 2d)),
            ((0d, 2d), (0d, 0d)),
        };

        var caps = GeneratedBridgeTrimGeometry.BuildCaps(segments, 5d);

        Assert.Equal(1, caps.Loops);
        Assert.Equal(0, caps.OpenChains);
        Assert.Equal(0, caps.NonManifoldNodes);
        Assert.Equal(4d, caps.Area);
        Assert.Equal(2, caps.Triangles.Count);
        Assert.All(caps.Triangles, triangle => Assert.All(new[] { triangle.A, triangle.B, triangle.C }, vertex => Assert.Equal(5d, vertex.Y)));
        Assert.All(caps.Triangles, triangle => Assert.Equal(0, triangle.LoopIndex));

        foreach (var triangle in caps.Triangles)
        {
            var (a, b, c) = (triangle.A, triangle.B, triangle.C);
            var normalY = ((b.Z - a.Z) * (c.X - a.X)) - ((b.X - a.X) * (c.Z - a.Z));
            Assert.True(normalY >= 0d, $"cap triangle faces -Y: normal Y {normalY}.");
        }

        var summed = caps.Triangles.Sum(triangle => 0.5d * Math.Abs(((triangle.B.X - triangle.A.X) * (triangle.C.Z - triangle.A.Z)) - ((triangle.B.Z - triangle.A.Z) * (triangle.C.X - triangle.A.X))));
        Assert.Equal(4d, summed, 9);
    }

    [Fact]
    public void BuildCaps_OpenChain_CountsOpenChainWithoutCap()
    {
        var segments = new[]
        {
            ((0d, 0d), (1d, 0d)),
            ((1d, 0d), (2d, 0d)),
        };

        var caps = GeneratedBridgeTrimGeometry.BuildCaps(segments, 5d);

        Assert.Equal(0, caps.Loops);
        Assert.Equal(1, caps.OpenChains);
        Assert.Empty(caps.Triangles);
    }

    [Fact]
    public void BuildCaps_NonManifoldNode_CountsNodeAndSkipsLoop()
    {
        var segments = new[]
        {
            ((0d, 0d), (2d, 0d)),
            ((2d, 0d), (2d, 2d)),
            ((2d, 2d), (0d, 2d)),
            ((0d, 2d), (0d, 0d)),
            ((2d, 0d), (3d, 3d)),
        };

        var caps = GeneratedBridgeTrimGeometry.BuildCaps(segments, 5d);

        Assert.Equal(1, caps.NonManifoldNodes);
        Assert.Equal(1, caps.OpenChains);
        Assert.Equal(0, caps.Loops);
        Assert.Empty(caps.Triangles);
    }

    [Fact]
    public void BuildCaps_ConcaveLShape_EarClipsWithCentroidsOutsideNotch()
    {
        var ring = new[] { (0d, 0d), (3d, 0d), (3d, 1d), (1d, 1d), (1d, 3d), (0d, 3d) };
        var segments = Enumerable.Range(0, ring.Length)
            .Select(index => (ring[index], ring[(index + 1) % ring.Length]))
            .ToList();

        var caps = GeneratedBridgeTrimGeometry.BuildCaps(segments, 5d);

        Assert.Equal(1, caps.Loops);
        Assert.Equal(5d, caps.Area, 9);
        Assert.Equal(4, caps.Triangles.Count);

        foreach (var triangle in caps.Triangles)
        {
            var centroidX = (triangle.A.X + triangle.B.X + triangle.C.X) / 3d;
            var centroidZ = (triangle.A.Z + triangle.B.Z + triangle.C.Z) / 3d;
            var inNotch = centroidX > 1d && centroidX < 3d && centroidZ > 1d && centroidZ < 3d;
            Assert.False(inNotch, $"triangle centroid ({centroidX}, {centroidZ}) lies in the notch.");
        }
    }

    [Fact]
    public void BuildCaps_NoSegments_ReturnsAllZero()
    {
        var caps = GeneratedBridgeTrimGeometry.BuildCaps(Array.Empty<((double X, double Z) A, (double X, double Z) B)>(), 5d);

        Assert.Equal(0, caps.Loops);
        Assert.Equal(0, caps.OpenChains);
        Assert.Equal(0, caps.NonManifoldNodes);
        Assert.Equal(0d, caps.Area);
        Assert.Empty(caps.Triangles);
    }
}
