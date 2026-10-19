using System.Collections.Generic;
using System.Numerics;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Profiles;
using Panopticon.Tools.ReplaySketch.Services;
using SkiaSharp;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>WI-A5-18: one domain-neutral Skia view contract over card-12 primitives.</summary>
public sealed class RunFrameRendererTests
{
    [Fact]
    public void Render_Points_ReturnsMarkersForHitTesting()
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var canvas = surface.Canvas;
        var primitives = new List<DomainVisualPrimitive>
        {
            new DomainVisualPoint("cell:0,0", new Vector3(0f, 0f, 0f), 0, "Crusader"),
            new DomainVisualPoint("cell:2,0", new Vector3(300f, 0f, 0f), 1, "Scorpion"),
        };
        var identity = new List<FramePointMarker>
        {
            new(1u, (DomainVisualPoint)primitives[0]),
            new(2u, (DomainVisualPoint)primitives[1]),
        };

        var markers = RunFrameRenderer.Render(canvas, 0, 0, 300, 200, primitives, identity);

        Assert.Equal(2, markers.Count);
        Assert.Equal(1u, markers[0].ObjectId);
        Assert.Equal(2u, markers[1].ObjectId);

        var nearFirst = RunFrameRenderer.HitTest(markers, markers[0].ScreenX + 2f, markers[0].ScreenY + 2f);
        Assert.NotNull(nearFirst);
        Assert.Equal(1u, nearFirst!.ObjectId);

        Assert.Null(RunFrameRenderer.HitTest(markers, 150f, 100f));
        Assert.Null(RunFrameRenderer.HitTest([], 150f, 100f));
    }

    [Fact]
    public void Render_GraphPrimitives_UseTheSameContract()
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var primitives = new List<DomainVisualPrimitive>
        {
            new DomainVisualPolyline("edge:0->1", [new Vector3(0f, 0f, 0f), new Vector3(100f, 80f, 0f)], "edge"),
            new DomainVisualPolygon("zone:a", [new Vector3(10f, 10f, 0f), new Vector3(40f, 10f, 0f), new Vector3(20f, 40f, 0f)], "zone"),
            new DomainVisualLabel("node:0", new Vector3(0f, 0f, 0f), "Depot 0"),
            new DomainVisualPoint("node:1", new Vector3(100f, 80f, 0f), 0, "node"),
        };

        var markers = RunFrameRenderer.Render(surface.Canvas, 0, 0, 300, 200, primitives);

        Assert.Single(markers);
        Assert.Equal("node:1", markers[0].BucketKey);
    }

    [Fact]
    public void Render_EmptyFrame_DrawsPlaceholderWithoutMarkers()
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var markers = RunFrameRenderer.Render(surface.Canvas, 0, 0, 300, 200, []);
        Assert.Empty(markers);
    }

    [Fact]
    public void Render_TooSmallCanvas_IsNoOp()
    {
        using var surface = SKSurface.Create(new SKImageInfo(8, 8));
        var primitives = new List<DomainVisualPrimitive>
        {
            new DomainVisualPoint("cell:0,0", new Vector3(0f, 0f, 0f), 0, "Anything"),
        };

        Assert.Empty(RunFrameRenderer.Render(surface.Canvas, 0, 0, 8, 8, primitives));
    }

    [Fact]
    public void ChartCursor_InAndOutOfRange_RendersWithoutThrowing()
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var series = new List<(string Name, IReadOnlyList<(uint Frame, double Value)> Points)>
        {
            ("money:USA", new[] { (0u, 100d), (10u, 90d) }),
        };

        MetricChartRenderer.Render(surface.Canvas, 0, 0, 300, 200, series, cursorFrame: 5u);
        MetricChartRenderer.Render(surface.Canvas, 0, 0, 300, 200, series, cursorFrame: 500u);
    }
}
