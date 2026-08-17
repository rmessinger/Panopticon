using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using SkiaSharp;

namespace OpenSage.Tools.ReplaySketch.Profiles;

/// <summary>
/// Pure SkiaSharp renderer for the validation-ground map editor tab: draws the roster unit markers
/// and their linked destructible targets over the bird's-eye map preview, and returns the
/// hit-testable marker positions. Contains no MAUI dependencies so it is exercised headlessly by
/// <c>OpenSage.Tools.ReplaySketch.Tests</c> and the screenshot mode. Sibling to
/// <see cref="MapPreviewRenderer"/>.
/// </summary>
public static class ValidationGroundMapRenderer
{
    /// <summary>
    /// Screen-space position of a placement, used for hit-testing. Keyed by the same
    /// (faction, roster, isTarget) tuple as <see cref="PlacementOverride"/> so a hit maps straight
    /// back to the override it edits.
    /// </summary>
    public readonly record struct PlacementMarker(int FactionIndex, int RosterIndex, bool IsTarget, float ScreenX, float ScreenY);

    private static readonly SKColor UsaColor = new(0xFF, 0x44, 0x44, 0xFF);
    private static readonly SKColor ChinaColor = new(0x44, 0xFF, 0x44, 0xFF);
    private static readonly SKColor GlaColor = new(0x44, 0x99, 0xFF, 0xFF);
    // Hollow orange outline so destructible targets read as distinct markers (not a grey mass that
    // hides the terrain) while staying clearly different from the filled faction-coloured unit circles.
    private static readonly SKColor TargetColor = new(0xFF, 0x8C, 0x00, 0xFF);
    private static readonly SKColor LinkColor = new(0xFF, 0xFF, 0xFF, 0x44);
    private static readonly SKColor SelectionColor = SKColors.White;

    private const float UnitRadius = 7f;
    private const float TargetHalfSize = 6f;
    private const float StructureHalfSize = 4.5f;
    private const float SelectionRingRadius = UnitRadius + 3f;

    // Neutral, semi-transparent so the terrain preview shows through; distinct from the faction
    // unit circles and the orange target outlines, reading as scenery rather than an actor.
    private static readonly SKColor StructureColor = new(0xC0, 0xC0, 0xC0, 0x99);

    /// <summary>Display colour for a roster faction (2=USA, 3=China, 4=GLA); white for any other.</summary>
    public static SKColor FactionColor(int factionIndex) => factionIndex switch
    {
        2 => UsaColor,
        3 => ChinaColor,
        4 => GlaColor,
        _ => SKColors.White,
    };

    /// <summary>Projects a planar world position into the square preview area (canvas pixels).</summary>
    public static SKPoint Project(Vector3 extentMin, Vector3 extentMax, Vector3 world, float offsetX, float offsetY, float previewSize)
    {
        var nx = (world.X - extentMin.X) / (extentMax.X - extentMin.X);
        var ny = 1f - (world.Y - extentMin.Y) / (extentMax.Y - extentMin.Y);
        return new SKPoint(offsetX + nx * previewSize, offsetY + ny * previewSize);
    }

    /// <summary>
    /// Computes the hit-testable marker positions for every placement without touching SkiaSharp
    /// drawing state, so the geometry is exercised headlessly by tests. <see cref="Render"/> draws
    /// from the same marker list.
    /// </summary>
    public static IReadOnlyList<PlacementMarker> ComputeMarkers(
        MapMetadataService map,
        SpawnPlan plan,
        float offsetX,
        float offsetY,
        float previewSize)
    {
        var markers = new List<PlacementMarker>();
        var extentMin = map.ExtentMin;
        var extentMax = map.ExtentMax;

        SKPoint ToScreen(Vector3 world) => Project(extentMin, extentMax, world, offsetX, offsetY, previewSize);

        foreach (var unit in plan.UnitPlacements)
        {
            if (TryGetTarget(plan, unit.TargetIndex, out var target))
            {
                var tp = ToScreen(target.Position);
                markers.Add(new PlacementMarker(unit.FactionIndex, unit.RosterIndex, IsTarget: true, tp.X, tp.Y));
            }

            var up = ToScreen(unit.Position);
            markers.Add(new PlacementMarker(unit.FactionIndex, unit.RosterIndex, IsTarget: false, up.X, up.Y));
        }

        return markers;
    }

    /// <summary>
    /// Renders the map preview plus unit/target markers into <paramref name="canvas"/> and returns
    /// the hit-testable marker positions (in pre-view-transform canvas pixel space).
    /// </summary>
    /// <param name="previewBitmap">Optional bird's-eye terrain thumbnail; pass null for a plain dark fill.</param>
    public static IReadOnlyList<PlacementMarker> Render(
        SKCanvas canvas,
        MapMetadataService map,
        SKBitmap? previewBitmap,
        SpawnPlan plan,
        (int FactionIndex, int RosterIndex, bool IsTarget)? selected,
        float offsetX,
        float offsetY,
        float previewSize)
    {
        var previewRect = new SKRect(offsetX, offsetY, offsetX + previewSize, offsetY + previewSize);
        DrawBackground(canvas, previewBitmap, previewRect);

        var markers = ComputeMarkers(map, plan, offsetX, offsetY, previewSize);

        DrawStructures(canvas, map, offsetX, offsetY, previewSize);
        DrawLinks(canvas, plan, map, offsetX, offsetY, previewSize);
        DrawMarkers(canvas, markers);
        DrawSelectionRing(canvas, markers, selected);
        return markers;
    }

    private static void DrawBackground(SKCanvas canvas, SKBitmap? previewBitmap, SKRect previewRect)
    {
        if (previewBitmap != null)
        {
            var src = new SKRect(0, 0, previewBitmap.Width, previewBitmap.Height);
            canvas.DrawBitmap(previewBitmap, src, previewRect);
        }

        using var borderPaint = new SKPaint
        {
            Color = new SKColor(0x55, 0x55, 0x55),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
        };
        canvas.DrawRect(previewRect, borderPaint);
    }

    /// <summary>
    /// Projects every map-author-placed civilian structure into the preview area, so hand-placing
    /// the formation (or eyeballing the generated layout) happens against the real town footprint.
    /// </summary>
    public static IReadOnlyList<SKRect> ComputeStructureRects(
        MapMetadataService map,
        float offsetX,
        float offsetY,
        float previewSize)
    {
        var rects = new List<SKRect>(map.CivilianStructures.Count);
        foreach (var structure in map.CivilianStructures)
        {
            var p = Project(map.ExtentMin, map.ExtentMax, structure.Position, offsetX, offsetY, previewSize);
            rects.Add(new SKRect(p.X - StructureHalfSize, p.Y - StructureHalfSize, p.X + StructureHalfSize, p.Y + StructureHalfSize));
        }
        return rects;
    }

    private static void DrawStructures(SKCanvas canvas, MapMetadataService map, float offsetX, float offsetY, float previewSize)
    {
        using var paint = new SKPaint { Color = StructureColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        foreach (var rect in ComputeStructureRects(map, offsetX, offsetY, previewSize))
        {
            canvas.DrawRect(rect, paint);
        }
    }

    private static void DrawLinks(SKCanvas canvas, SpawnPlan plan, MapMetadataService map, float offsetX, float offsetY, float previewSize)
    {
        using var linkPaint = new SKPaint
        {
            Color = LinkColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true,
        };
        var extentMin = map.ExtentMin;
        var extentMax = map.ExtentMax;
        foreach (var unit in plan.UnitPlacements)
        {
            if (!TryGetTarget(plan, unit.TargetIndex, out var target))
            {
                continue;
            }
            var a = Project(extentMin, extentMax, unit.Position, offsetX, offsetY, previewSize);
            var b = Project(extentMin, extentMax, target.Position, offsetX, offsetY, previewSize);
            canvas.DrawLine(a, b, linkPaint);
        }
    }

    private static void DrawMarkers(SKCanvas canvas, IReadOnlyList<PlacementMarker> markers)
    {
        // Targets: hollow orange outlines (terrain shows through; distinct from unit circles).
        using var targetPaint = new SKPaint { Color = TargetColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        foreach (var m in markers)
        {
            if (m.IsTarget)
            {
                var rect = new SKRect(m.ScreenX - TargetHalfSize, m.ScreenY - TargetHalfSize, m.ScreenX + TargetHalfSize, m.ScreenY + TargetHalfSize);
                canvas.DrawRect(rect, targetPaint);
            }
        }

        using var unitPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        foreach (var m in markers)
        {
            if (m.IsTarget)
            {
                continue;
            }
            unitPaint.Color = FactionColor(m.FactionIndex);
            canvas.DrawCircle(new SKPoint(m.ScreenX, m.ScreenY), UnitRadius, unitPaint);
        }
    }

    private static void DrawSelectionRing(SKCanvas canvas, IReadOnlyList<PlacementMarker> markers, (int FactionIndex, int RosterIndex, bool IsTarget)? selected)
    {
        if (!selected.HasValue)
        {
            return;
        }

        var sel = selected.Value;
        foreach (var m in markers)
        {
            if (m.FactionIndex == sel.FactionIndex && m.RosterIndex == sel.RosterIndex && m.IsTarget == sel.IsTarget)
            {
                using var selPaint = new SKPaint
                {
                    Color = SelectionColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2,
                    IsAntialias = true,
                };
                canvas.DrawCircle(new SKPoint(m.ScreenX, m.ScreenY), SelectionRingRadius, selPaint);
                return;
            }
        }
    }

    private static bool TryGetTarget(SpawnPlan plan, int targetIndex, [NotNullWhen(true)] out TargetPlacement? target)
    {
        if (targetIndex >= 0 && targetIndex < plan.TargetPlacements.Count)
        {
            target = plan.TargetPlacements[targetIndex];
            return true;
        }
        target = null;
        return false;
    }
}
