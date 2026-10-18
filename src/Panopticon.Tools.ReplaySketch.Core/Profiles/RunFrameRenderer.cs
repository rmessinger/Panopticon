#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Services;
using SkiaSharp;

namespace Panopticon.Tools.ReplaySketch.Profiles;

/// <summary>One rendered point in screen space, paired with entity identity for hit-testing.</summary>
public sealed record FrameViewMarker(
    uint ObjectId,
    int OwnerIndex,
    string KindName,
    string BucketKey,
    float ScreenX,
    float ScreenY);

/// <summary>
/// Card WI-A5-18: domain-neutral Skia renderer over the card-12 visual primitives. Any domain
/// that emits primitives renders through this one view contract (RTS points, graph nodes and
/// edges, labels). Contract mirrors <see cref="MapPreviewRenderer"/>: the caller clears the
/// canvas and passes a sub-rect; world X/Y map to the rect with Y flipped so +Y is up; returned
/// markers carry screen positions for hit-testing.
/// </summary>
public static class RunFrameRenderer
{
    private const float Padding = 12f;
    private const float PointRadius = 4f;
    private const float SelectRadius = 8f;

    private static readonly SKColor[] OwnerPalette =
    [
        new(0x4F, 0x83, 0xCC),
        new(0xE0, 0x7B, 0x39),
        new(0x7A, 0x9E, 0x5A),
        new(0xA5, 0x6D, 0xC0),
    ];

    public static IReadOnlyList<FrameViewMarker> Render(
        SKCanvas canvas,
        float offsetX,
        float offsetY,
        float width,
        float height,
        IReadOnlyList<DomainVisualPrimitive> primitives,
        IReadOnlyList<FramePointMarker>? pointMarkers = null,
        uint? selectedObjectId = null)
    {
        if (width < Padding * 2 || height < Padding * 2)
        {
            return [];
        }

        var points = primitives.OfType<DomainVisualPoint>().ToList();
        if (primitives.Count == 0)
        {
            DrawLabel(canvas, offsetX + width / 2f, offsetY + height / 2f, "no state at this frame", SKTextAlign.Center);
            return [];
        }

        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        foreach (var primitive in primitives)
        {
            foreach (var p in PositionsOf(primitive))
            {
                minX = Math.Min(minX, p.x);
                maxX = Math.Max(maxX, p.x);
                minY = Math.Min(minY, p.y);
                maxY = Math.Max(maxY, p.y);
            }
        }

        var spanX = MathF.Max(maxX - minX, 1f);
        var spanY = MathF.Max(maxY - minY, 1f);
        var plotLeft = offsetX + Padding;
        var plotTop = offsetY + Padding;
        var plotWidth = width - Padding * 2;
        var plotHeight = height - Padding * 2;

        float ScreenX(float x) => plotLeft + plotWidth * (x - minX) / spanX;
        float ScreenY(float y) => plotTop + plotHeight * (1f - (y - minY) / spanY);

        foreach (var polyline in primitives.OfType<DomainVisualPolyline>())
        {
            using var path = new SKPath();
            foreach (var world in polyline.Points)
            {
                if (path.IsEmpty)
                {
                    path.MoveTo(ScreenX(world.X), ScreenY(world.Y));
                }
                else
                {
                    path.LineTo(ScreenX(world.X), ScreenY(world.Y));
                }
            }

            using var linePaint = new SKPaint { Color = new SKColor(0x90, 0x90, 0x98), StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
            canvas.DrawPath(path, linePaint);
        }

        foreach (var polygon in primitives.OfType<DomainVisualPolygon>())
        {
            using var path = new SKPath();
            foreach (var world in polygon.Points)
            {
                if (path.IsEmpty)
                {
                    path.MoveTo(ScreenX(world.X), ScreenY(world.Y));
                }
                else
                {
                    path.LineTo(ScreenX(world.X), ScreenY(world.Y));
                }
            }

            path.Close();
            using var fillPaint = new SKPaint { Color = new SKColor(0x60, 0x8A, 0xB0, 0x40), IsAntialias = true };
            canvas.DrawPath(path, fillPaint);
        }

        var markers = new List<FrameViewMarker>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var sx = ScreenX(point.Position.X);
            var sy = ScreenY(point.Position.Y);
            using var fill = new SKPaint { Color = OwnerColor(point.OwnerIndex), IsAntialias = true };
            canvas.DrawCircle(sx, sy, PointRadius * MathF.Max(point.Size, 0.5f), fill);
            var objectId = i < pointMarkers?.Count ? pointMarkers[i].ObjectId : 0;
            if (selectedObjectId is { } selected && selected == objectId)
            {
                using var ring = new SKPaint { Color = SKColors.White, StrokeWidth = 1.5f, IsStroke = true, IsAntialias = true };
                canvas.DrawCircle(sx, sy, SelectRadius, ring);
            }

            markers.Add(new FrameViewMarker(objectId, point.OwnerIndex, point.KindName, point.BucketKey, sx, sy));
        }

        foreach (var label in primitives.OfType<DomainVisualLabel>())
        {
            DrawLabel(canvas, ScreenX(label.Position.X), ScreenY(label.Position.Y) - 6f, label.Text, SKTextAlign.Center);
        }

        return markers;
    }

    /// <summary>Nearest marker within <paramref name="radius"/> screen pixels, null when none.</summary>
    public static FrameViewMarker? HitTest(IReadOnlyList<FrameViewMarker> markers, float x, float y, float radius = SelectRadius)
    {
        FrameViewMarker? nearest = null;
        var best = radius * radius;
        foreach (var marker in markers)
        {
            var dx = marker.ScreenX - x;
            var dy = marker.ScreenY - y;
            var distance = dx * dx + dy * dy;
            if (distance <= best)
            {
                best = distance;
                nearest = marker;
            }
        }

        return nearest;
    }

    internal static SKColor OwnerColor(int ownerIndex) => OwnerPalette[Math.Abs(ownerIndex) % OwnerPalette.Length];

    private static IEnumerable<(float x, float y)> PositionsOf(DomainVisualPrimitive primitive)
    {
        return primitive switch
        {
            DomainVisualPoint p => [(p.Position.X, p.Position.Y)],
            DomainVisualPolyline l => l.Points.Select(w => (w.X, w.Y)),
            DomainVisualPolygon g => g.Points.Select(w => (w.X, w.Y)),
            DomainVisualLabel b => [(b.Position.X, b.Position.Y)],
            _ => [],
        };
    }

    private static void DrawLabel(SKCanvas canvas, float x, float y, string text, SKTextAlign align)
    {
        using var font = new SKFont(SKTypeface.Default, 10f);
        using var paint = new SKPaint { Color = new SKColor(0xAA, 0xAA, 0xAA), IsAntialias = true };
        canvas.DrawText(text, x, y, align, font, paint);
    }
}
