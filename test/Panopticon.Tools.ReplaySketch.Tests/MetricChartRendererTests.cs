using System.Collections.Generic;
using Panopticon.Tools.ReplaySketch.Profiles;
using SkiaSharp;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-04: the metric chart renderer draws on an offscreen surface without throwing,
/// including the no-series label path.
/// </summary>
public sealed class MetricChartRendererTests
{
    [Fact]
    public void Render_MultiSeriesWithOutliers_DoesNotThrow()
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var canvas = surface.Canvas;

        var series = new List<(string Name, IReadOnlyList<(uint Frame, double Value)> Points)>
        {
            ("money:USA", [(0u, 1000d), (10u, 900d), (20u, 800d), (30u, 1_000_000d)]),
            ("money:GLA", [(0u, 2000d), (10u, 1950d), (20u, 1900d), (30u, 1850d)]),
            ("unitCount:infantry", [(0u, 4d), (10u, 6d), (20u, 2d), (30u, 8d)]),
        };

        canvas.Clear(SKColors.White);
        MetricChartRenderer.Render(canvas, 0, 0, 300, 200, series);
    }

    [Fact]
    public void Render_NoVisibleSeries_DrawsLabelInstead()
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var canvas = surface.Canvas;

        canvas.Clear(SKColors.White);
        MetricChartRenderer.Render(canvas, 0, 0, 300, 200, []);

        canvas.Clear(SKColors.White);
        MetricChartRenderer.Render(canvas, 0, 0, 300, 200, new List<(string, IReadOnlyList<(uint, double)>)>
        {
            ("empty", []),
        });
    }

    [Fact]
    public void Render_TooSmallCanvas_IsNoOp()
    {
        using var surface = SKSurface.Create(new SKImageInfo(8, 8));
        var canvas = surface.Canvas;

        canvas.Clear(SKColors.White);
        MetricChartRenderer.Render(canvas, 0, 0, 8, 8, [("x", [(0u, 1d), (1u, 2d)])]);
    }
}
