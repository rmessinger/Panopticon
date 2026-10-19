#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace Panopticon.Tools.ReplaySketch.Profiles;

/// <summary>
/// WI-A5-04: pure-Skia line chart for run metric series, following the MapPreviewRenderer
/// contract: no UI dependencies, the caller clears the canvas and passes the target sub-rect,
/// and the same code renders into a window canvas or an offscreen surface for tests and
/// screenshots.
/// </summary>
public static class MetricChartRenderer
{
    private const float LeftPadding = 44f;
    private const float BottomPadding = 24f;
    private const float TopPadding = 10f;
    private const float RightPadding = 10f;
    private const float LegendLineLength = 14f;
    private const float LegendRowHeight = 16f;

    private static readonly SKColor[] Palette =
    [
        new(0x2C, 0x7F, 0xB8),
        new(0xD6, 0x62, 0x2A),
        new(0x4C, 0xA0, 0x2C),
        new(0x8E, 0x44, 0xAD),
        new(0x97, 0x6E, 0x2B),
        new(0xC0, 0x39, 0x3B),
    ];

    public static void Render(
        SKCanvas canvas,
        float offsetX,
        float offsetY,
        float width,
        float height,
        IReadOnlyList<(string Name, IReadOnlyList<(uint Frame, double Value)> Points)> series,
        uint? cursorFrame = null)
    {
        if (width <= LeftPadding + RightPadding || height <= TopPadding + BottomPadding)
        {
            return;
        }

        var plotLeft = offsetX + LeftPadding;
        var plotTop = offsetY + TopPadding;
        var plotRight = offsetX + width - RightPadding;
        var plotBottom = offsetY + height - BottomPadding;
        var plotWidth = plotRight - plotLeft;
        var plotHeight = plotBottom - plotTop;

        var visible = series.Where(s => s.Points.Count > 0).ToList();
        using var axisPaint = new SKPaint { Color = new SKColor(0x99, 0x99, 0x99), StrokeWidth = 1f, IsStroke = true };
        canvas.DrawLine(plotLeft, plotTop, plotLeft, plotBottom, axisPaint);
        canvas.DrawLine(plotLeft, plotBottom, plotRight, plotBottom, axisPaint);

        if (visible.Count == 0)
        {
            DrawLabel(canvas, offsetX + LeftPadding, offsetY + TopPadding, "no series selected", new SKColor(0xAA, 0xAA, 0xAA));
            return;
        }

        var minFrame = visible.Min(s => s.Points[0].Frame);
        var maxFrame = visible.Max(s => s.Points[^1].Frame);
        var minValue = Math.Min(0d, visible.Min(s => s.Points.Min(p => p.Value)));
        var maxValue = visible.Max(s => s.Points.Max(p => p.Value));
        if (maxFrame == minFrame)
        {
            maxFrame = minFrame + 1;
        }

        if (maxValue <= minValue)
        {
            maxValue = minValue + 1;
        }

        DrawAxisTicks(canvas, plotLeft, plotTop, plotRight, plotBottom, minValue, maxValue, minFrame, maxFrame);

        for (var i = 0; i < visible.Count; i++)
        {
            var color = Palette[i % Palette.Length];
            using var linePaint = new SKPaint { Color = color, StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
            using var path = new SKPath();
            var points = visible[i].Points;
            for (var p = 0; p < points.Count; p++)
            {
                var x = plotLeft + plotWidth * ((float)points[p].Frame - minFrame) / (maxFrame - minFrame);
                var y = plotBottom - plotHeight * ((float)points[p].Value - (float)minValue) / ((float)maxValue - (float)minValue);
                if (p == 0)
                {
                    path.MoveTo(x, y);
                    continue;
                }

                path.LineTo(x, y);
            }

            canvas.DrawPath(path, linePaint);
        }

        DrawLegend(canvas, offsetX + LeftPadding, offsetY + TopPadding, visible.Select(s => s.Name).ToList());

        if (cursorFrame is { } frame && frame >= minFrame && frame <= maxFrame)
        {
            var cursorX = plotLeft + plotWidth * ((float)frame - minFrame) / (maxFrame - minFrame);
            using var cursorPaint = new SKPaint { Color = new SKColor(0xE6, 0xE6, 0xE6, 0xB0), StrokeWidth = 1f, IsStroke = true };
            canvas.DrawLine(cursorX, plotTop, cursorX, plotBottom, cursorPaint);
        }
    }

    private static void DrawAxisTicks(
        SKCanvas canvas,
        float plotLeft,
        float plotTop,
        float plotRight,
        float plotBottom,
        double minValue,
        double maxValue,
        uint minFrame,
        uint maxFrame)
    {
        using var tickPaint = new SKPaint { Color = new SKColor(0x99, 0x99, 0x99), StrokeWidth = 1f, IsStroke = true };
        var labelColor = new SKColor(0xCC, 0xCC, 0xCC);
        for (var i = 0; i <= 4; i++)
        {
            var fraction = i / 4f;
            var y = plotBottom - (plotBottom - plotTop) * fraction;
            canvas.DrawLine(plotLeft - 3f, y, plotLeft, y, tickPaint);
            var value = minValue + (maxValue - minValue) * fraction;
            DrawLabel(canvas, plotLeft - 6f, y - 6f, FormatNumber(value), labelColor, SKTextAlign.Right);

            var x = plotLeft + (plotRight - plotLeft) * fraction;
            canvas.DrawLine(x, plotBottom, x, plotBottom + 3f, tickPaint);
            var frame = minFrame + (uint)Math.Round((maxFrame - minFrame) * fraction);
            DrawLabel(canvas, x, plotBottom + 6f, frame.ToString(CultureInfo.InvariantCulture), labelColor, SKTextAlign.Center);
        }
    }

    private static void DrawLegend(SKCanvas canvas, float x, float y, IReadOnlyList<string> names)
    {
        using var swatchPaint = new SKPaint { StrokeWidth = 3f, IsStroke = true, IsAntialias = true };
        for (var i = 0; i < names.Count; i++)
        {
            var rowY = y + i * LegendRowHeight;
            swatchPaint.Color = Palette[i % Palette.Length];
            canvas.DrawLine(x, rowY + 8f, x + LegendLineLength, rowY + 8f, swatchPaint);
            DrawLabel(canvas, x + LegendLineLength + 4f, rowY, names[i], new SKColor(0xDD, 0xDD, 0xDD));
        }
    }

    private static void DrawLabel(SKCanvas canvas, float x, float y, string text, SKColor color, SKTextAlign align = SKTextAlign.Left)
    {
        using var font = new SKFont(SKTypeface.Default, 10f);
        using var textPaint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y + 10f, align, font, textPaint);
    }

    private static string FormatNumber(double value) =>
        Math.Abs(value) >= 1000 ? ((int)value).ToString("N0", CultureInfo.InvariantCulture) : value.ToString("0.#", CultureInfo.InvariantCulture);
}
