using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using SkiaSharp;

namespace Panopticon.Tools.ReplaySketch.Profiles;

/// <summary>
/// Pure SkiaSharp renderer for the validation-ground map editor tab: draws the roster unit markers
/// and their linked destructible targets over the bird's-eye map preview, and returns the
/// hit-testable marker positions. Contains no MAUI dependencies so it is exercised headlessly by
/// <c>Panopticon.Tools.ReplaySketch.Tests</c> and the screenshot mode. Sibling to
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

    // Screen gap kept between a unit circle and its own target square when the pair is too close
    // to draw separately (see <see cref="SeparateFromUnit"/>).
    private const float MinimumPairScreenGap = 3f;

    /// <summary>Closest drawn distance between a unit marker and its own target marker, in px.</summary>
    public const float MinimumMarkerSeparationPx = UnitRadius + TargetHalfSize + MinimumPairScreenGap;

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
    /// from the same marker list. A target square whose true projection lands inside its unit's
    /// circle (a proximity-pinned pair, e.g. the WI-VG-04 terrorist at 8 world units) is drawn at a
    /// displaced position along the pair axis instead; only the marker moves, never the plan.
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
            var up = ToScreen(unit.Position);
            if (TryGetTarget(plan, unit.TargetIndex, out var target))
            {
                var tp = SeparateFromUnit(up, ToScreen(target.Position));
                markers.Add(new PlacementMarker(unit.FactionIndex, unit.RosterIndex, IsTarget: true, tp.X, tp.Y));
            }

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
        DrawLinks(canvas, markers);
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

    private static void DrawLinks(SKCanvas canvas, IReadOnlyList<PlacementMarker> markers)
    {
        using var linkPaint = new SKPaint
        {
            Color = LinkColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true,
        };
        // A target marker is always emitted immediately before its own unit marker, so pairs are
        // adjacent; the line ends at the (possibly displaced) drawn square position.
        for (var i = 0; i < markers.Count - 1; i++)
        {
            var target = markers[i];
            if (!target.IsTarget)
            {
                continue;
            }

            var unit = markers[i + 1];
            if (unit.IsTarget || unit.FactionIndex != target.FactionIndex || unit.RosterIndex != target.RosterIndex)
            {
                continue;
            }

            canvas.DrawLine(
                new SKPoint(unit.ScreenX, unit.ScreenY),
                new SKPoint(target.ScreenX, target.ScreenY),
                linkPaint);
        }
    }

    /// <summary>
    /// A proximity-pinned pair (target deliberately a few world units from its unit) projects
    /// closer than the marker sizes at every usable preview scale, hiding the square under the
    /// circle. The square is nudged outward along the pair axis to the minimum drawn separation;
    /// the plan's world positions stay untouched.
    /// </summary>
    private static SKPoint SeparateFromUnit(SKPoint unitScreen, SKPoint targetScreen)
    {
        var dx = targetScreen.X - unitScreen.X;
        var dy = targetScreen.Y - unitScreen.Y;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance >= MinimumMarkerSeparationPx)
        {
            return targetScreen;
        }

        if (distance < 0.0001f)
        {
            return new SKPoint(unitScreen.X + MinimumMarkerSeparationPx, unitScreen.Y);
        }

        var scale = MinimumMarkerSeparationPx / distance;
        return new SKPoint(unitScreen.X + dx * scale, unitScreen.Y + dy * scale);
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
