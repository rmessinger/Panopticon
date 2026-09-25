using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using SkiaSharp;

namespace Panopticon.Tools.ReplaySketch.Profiles;

/// <summary>
/// Pure SkiaSharp renderer for the bird's-eye map preview.
/// Contains no MAUI dependencies so it can be exercised by the test harness.
/// </summary>
public static class MapPreviewRenderer
{
    // ── Colours ───────────────────────────────────────────────────────────────

    public static readonly SKColor[] SlotColors =
    [
        new SKColor(0xFF, 0x44, 0x44, 0xFF), // slot 0 – red
        new SKColor(0x44, 0x44, 0xFF, 0xFF), // slot 1 – blue
        new SKColor(0x44, 0xFF, 0x44, 0xFF), // slot 2 – green
        new SKColor(0x44, 0xFF, 0xFF, 0xFF), // slot 3 – cyan
    ];

    private static readonly SKColor AnchorColor = SKColors.White;
    private static readonly SKColor ArcColor = new SKColor(0xFF, 0xFF, 0xFF, 0x66);
    private static readonly SKColor SelectionRingColor = SKColors.White;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Screen-space position of an action marker, used for hit-testing.
    /// </summary>
    public readonly record struct MarkerResult(int PlayerIdx, string ActionId, float ScreenX, float ScreenY);

    /// <summary>
    /// Renders the map preview into <paramref name="canvas"/> and returns the
    /// hit-testable marker positions (in the pre-view-transform canvas pixel space).
    /// </summary>
    /// <param name="canvas">Target canvas, already cleared by the caller.</param>
    /// <param name="map">Map geometry and player-start data.</param>
    /// <param name="previewBitmap">
    /// Optional bird's-eye terrain thumbnail to draw as the background.
    /// Typically built once from <see cref="MapMetadataService.PreviewImagePixels"/> and cached
    /// by the caller.  Pass <c>null</c> to render a plain dark rectangle instead.
    /// </param>
    /// <param name="players">Ordered list of player slots from the scenario.</param>
    /// <param name="baseRadiusWorldUnits">Radius used for landmark-relative distances.</param>
    /// <param name="selectedSlotIndex">Index of the currently active player slot.</param>
    /// <param name="selectedActionId">Id of the action that is currently selected, or null.</param>
    /// <param name="offsetX">Left edge of the square preview area in canvas pixels.</param>
    /// <param name="offsetY">Top edge of the square preview area in canvas pixels.</param>
    /// <param name="previewSize">Side length of the square preview area in canvas pixels.</param>
    public static IReadOnlyList<MarkerResult> Render(
        SKCanvas canvas,
        MapMetadataService map,
        SKBitmap? previewBitmap,
        IReadOnlyList<PlayerSlotConfig> players,
        float baseRadiusWorldUnits,
        int selectedSlotIndex,
        string? selectedActionId,
        float offsetX,
        float offsetY,
        float previewSize)
    {
        var markers = new List<MarkerResult>();

        // ── Background: terrain thumbnail or plain dark fill ──────────────
        var previewRect = new SKRect(offsetX, offsetY, offsetX + previewSize, offsetY + previewSize);

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

        var extentMin = map.ExtentMin;
        var extentMax = map.ExtentMax;

        SKPoint WorldToScreen(Vector2 world)
        {
            var nx = (world.X - extentMin.X) / (extentMax.X - extentMin.X);
            var ny = 1f - (world.Y - extentMin.Y) / (extentMax.Y - extentMin.Y);
            return new SKPoint(offsetX + nx * previewSize, offsetY + ny * previewSize);
        }

        float WorldRadiusToPixels(float worldRadius)
        {
            var mapWidthWorld = extentMax.X - extentMin.X;
            return worldRadius / mapWidthWorld * previewSize;
        }

        // Pass 1 — action markers (drawn beneath player anchors so anchors are always legible)
        for (var slotIdx = 0; slotIdx < players.Count; slotIdx++)
        {
            var player = players[slotIdx];
            var color = slotIdx < SlotColors.Length ? SlotColors[slotIdx] : AnchorColor;
            var isSlotSelected = slotIdx == selectedSlotIndex;

            for (var actionIdx = 0; actionIdx < player.Actions.Count; actionIdx++)
            {
                var action = player.Actions[actionIdx];
                var isActionSelected = action.Id == selectedActionId;

                DrawActionMarker(canvas, action, actionIdx, slotIdx, map,
                    WorldToScreen, WorldRadiusToPixels, color,
                    isSlotSelected, isActionSelected, baseRadiusWorldUnits);

                if (action.Position != null)
                {
                    var pos = GetMarkerScreen(action, slotIdx, map,
                        WorldToScreen, WorldRadiusToPixels, baseRadiusWorldUnits);
                    markers.Add(new MarkerResult(slotIdx, action.Id, pos.X, pos.Y));
                }
            }
        }

        // Pass 2 — player-start anchors drawn on top so they are never obscured by action rings
        for (var slotIdx = 0; slotIdx < players.Count; slotIdx++)
        {
            var color = slotIdx < SlotColors.Length ? SlotColors[slotIdx] : AnchorColor;
            var isSlotSelected = slotIdx == selectedSlotIndex;

            var startWorld = map.PlayerStart(slotIdx);
            var startScreen = WorldToScreen(new Vector2(startWorld.X, startWorld.Y));

            using var anchorPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
            canvas.DrawCircle(startScreen, isSlotSelected ? 7f : 5f, anchorPaint);

            using var textPaint = new SKPaint { Color = color };
            using var textFont = new SKFont { Size = 12 };
            canvas.DrawText($"P{slotIdx + 1}", startScreen.X + 6, startScreen.Y - 4, textFont, textPaint);
        }

        return markers;
    }

    // ── Internal helpers (internal so GoldenImageHelper can call Render directly) ──

    internal static SKPoint GetMarkerScreen(
        ActionEntry action,
        int slotIdx,
        MapMetadataService map,
        Func<Vector2, SKPoint> worldToScreen,
        Func<float, float> worldRadiusToPixels,
        float baseRadius)
    {
        switch (action.Position)
        {
            case NormalizedPosition np:
                {
                    var wx = map.ExtentMin.X + np.NormX * (map.ExtentMax.X - map.ExtentMin.X);
                    var wy = map.ExtentMin.Y + np.NormY * (map.ExtentMax.Y - map.ExtentMin.Y);
                    return worldToScreen(new Vector2(wx, wy));
                }

            case LandmarkRelativePosition lrp when lrp.Angle is FixedAngle fa:
                {
                    var anchor = GetLandmarkWorld(lrp.Landmark, slotIdx, 1 - slotIdx, map);
                    var anchorScreen = worldToScreen(new Vector2(anchor.X, anchor.Y));
                    var distPixels = worldRadiusToPixels(lrp.DistanceInBaseWidths * baseRadius);
                    var rad = fa.Degrees * MathF.PI / 180f;
                    return new SKPoint(
                        anchorScreen.X + MathF.Cos(rad) * distPixels,
                        anchorScreen.Y - MathF.Sin(rad) * distPixels);
                }

            case LandmarkRelativePosition lrp when lrp.Angle is RandomAngle ra:
                {
                    var anchor = GetLandmarkWorld(lrp.Landmark, slotIdx, 1 - slotIdx, map);
                    var anchorScreen = worldToScreen(new Vector2(anchor.X, anchor.Y));
                    var distPixels = worldRadiusToPixels(lrp.DistanceInBaseWidths * baseRadius);
                    var midRad = (ra.MinDegrees + ra.MaxDegrees) * 0.5f * MathF.PI / 180f;
                    return new SKPoint(
                        anchorScreen.X + MathF.Cos(midRad) * distPixels,
                        anchorScreen.Y - MathF.Sin(midRad) * distPixels);
                }

            default:
                return SKPoint.Empty;
        }
    }

    private static void DrawActionMarker(
        SKCanvas canvas,
        ActionEntry action,
        int actionIdx,
        int slotIdx,
        MapMetadataService map,
        Func<Vector2, SKPoint> worldToScreen,
        Func<float, float> worldRadiusToPixels,
        SKColor color,
        bool isSlotSelected,
        bool isActionSelected,
        float baseRadius)
    {
        if (action.Position == null) return;

        var label = $"{actionIdx + 1}";

        using var dotPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
        using var arcFillPaint = new SKPaint { Color = ArcColor, Style = SKPaintStyle.Fill };
        using var selPaint = new SKPaint
        {
            Color = SelectionRingColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isActionSelected ? 2f : 1.5f,
        };
        using var textPaint = new SKPaint { Color = color };
        using var textFont = new SKFont { Size = 11 };

        switch (action.Position)
        {
            case NormalizedPosition np:
                {
                    var wx = map.ExtentMin.X + np.NormX * (map.ExtentMax.X - map.ExtentMin.X);
                    var wy = map.ExtentMin.Y + np.NormY * (map.ExtentMax.Y - map.ExtentMin.Y);
                    var screen = worldToScreen(new Vector2(wx, wy));
                    var dotR = isActionSelected ? 6f : isSlotSelected ? 5f : 3f;
                    canvas.DrawCircle(screen, dotR, dotPaint);
                    if (isActionSelected) canvas.DrawCircle(screen, dotR + 3f, selPaint);
                    canvas.DrawText(label, screen.X + 6, screen.Y - 4, textFont, textPaint);
                    break;
                }

            case LandmarkRelativePosition lrp:
                {
                    var anchor = GetLandmarkWorld(lrp.Landmark, slotIdx, 1 - slotIdx, map);
                    var anchorScreen = worldToScreen(new Vector2(anchor.X, anchor.Y));
                    var distPixels = worldRadiusToPixels(lrp.DistanceInBaseWidths * baseRadius);

                    switch (lrp.Angle)
                    {
                        case FixedAngle fa:
                            {
                                var rad = fa.Degrees * MathF.PI / 180f;
                                var tip = new SKPoint(
                                    anchorScreen.X + MathF.Cos(rad) * distPixels,
                                    anchorScreen.Y - MathF.Sin(rad) * distPixels);
                                var dotR = isActionSelected ? 6f : isSlotSelected ? 5f : 3f;
                                canvas.DrawCircle(tip, dotR, dotPaint);
                                if (isActionSelected) canvas.DrawCircle(tip, dotR + 3f, selPaint);
                                canvas.DrawText(label, tip.X + 6, tip.Y - 4, textFont, textPaint);
                                break;
                            }

                        case RandomAngle ra:
                            {
                                DrawArcSector(canvas, anchorScreen, distPixels,
                                    ra.MinDegrees, ra.MaxDegrees, arcFillPaint, dotPaint);
                                var midRad = (ra.MinDegrees + ra.MaxDegrees) * 0.5f * MathF.PI / 180f;
                                var midTip = new SKPoint(
                                    anchorScreen.X + MathF.Cos(midRad) * distPixels,
                                    anchorScreen.Y - MathF.Sin(midRad) * distPixels);
                                if (isActionSelected) canvas.DrawCircle(midTip, 9f, selPaint);
                                canvas.DrawText(label, midTip.X + 4, midTip.Y - 4, textFont, textPaint);
                                break;
                            }
                    }

                    break;
                }
        }
    }

    internal static Vector3 GetLandmarkWorld(
        LandmarkType landmark, int ownerIdx, int enemyIdx, MapMetadataService map)
        => landmark switch
        {
            LandmarkType.OwnBase => map.PlayerStart(ownerIdx),
            LandmarkType.EnemyBase => map.PlayerStart(enemyIdx),
            LandmarkType.MapCenter => map.MapCenter,
            _ => map.MapCenter,
        };

    private static void DrawArcSector(
        SKCanvas canvas,
        SKPoint center,
        float radius,
        float minDeg,
        float maxDeg,
        SKPaint fillPaint,
        SKPaint edgePaint)
    {
        const int segments = 24;
        var minRad = minDeg * MathF.PI / 180f;
        var maxRad = maxDeg * MathF.PI / 180f;
        var step = (maxRad - minRad) / segments;

        using var path = new SKPath();
        path.MoveTo(center);
        for (var i = 0; i <= segments; i++)
        {
            var a = minRad + i * step;
            path.LineTo(center.X + MathF.Cos(a) * radius, center.Y - MathF.Sin(a) * radius);
        }

        path.Close();
        canvas.DrawPath(path, fillPaint);

        using var outlinePaint = new SKPaint
        {
            Color = edgePaint.Color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
        };
        var minTip = new SKPoint(center.X + MathF.Cos(minRad) * radius, center.Y - MathF.Sin(minRad) * radius);
        var maxTip = new SKPoint(center.X + MathF.Cos(maxRad) * radius, center.Y - MathF.Sin(maxRad) * radius);
        canvas.DrawLine(center, minTip, outlinePaint);
        canvas.DrawLine(center, maxTip, outlinePaint);
        canvas.DrawCircle(minTip, 3f, edgePaint);
        canvas.DrawCircle(maxTip, 3f, edgePaint);
    }
}
