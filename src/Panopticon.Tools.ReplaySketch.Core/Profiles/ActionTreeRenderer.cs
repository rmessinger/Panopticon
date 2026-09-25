using System.Collections.Generic;
using System.Linq;
using Panopticon.Tools.ReplaySketch.Model;
using SkiaSharp;

namespace Panopticon.Tools.ReplaySketch.Profiles;

/// <summary>
/// Carries visual state for an in-progress drag operation so the renderer
/// can show the dragged node, a drag-line, and the potential drop target.
/// </summary>
/// <param name="DraggedId">ID of the node currently being dragged.</param>
/// <param name="CurrentX">Current drag X in logical canvas units.</param>
/// <param name="CurrentY">Current drag Y in logical canvas units.</param>
/// <param name="TargetId">ID of the node the drag is hovering over, or <c>null</c>.</param>
public readonly record struct DragState(
    string DraggedId,
    float CurrentX,
    float CurrentY,
    string? TargetId);

/// <summary>
/// Pure-C# SkiaSharp renderer for the action tree node graph.
/// Extracted from the MAUI view so that rendering can be exercised headlessly
/// in unit tests (golden-image comparison) without a running MAUI host.
/// </summary>
public static class ActionTreeRenderer
{
    // ── Layout constants (mirrored from ActionTreeLayoutEngine) ──────────────
    private const float NodeW = ActionTreeLayoutEngine.NodeW;
    private const float NodeH = ActionTreeLayoutEngine.NodeH;

    // ── Palette colours ──────────────────────────────────────────────────────
    public static readonly SKColor CanvasBg = new(0x1A, 0x1A, 0x1A);
    public static readonly SKColor NodeFill = new(0x2D, 0x2D, 0x2D);
    public static readonly SKColor NodeBorderNormal = new(0x55, 0x55, 0x55);
    public static readonly SKColor NodeBorderSelect = SKColors.White;
    public static readonly SKColor NodeBorderDropTarget = new(0x00, 0xCC, 0xFF);
    public static readonly SKColor TextMain = new(0xD4, 0xD4, 0xD4);
    public static readonly SKColor TextDim = new(0x88, 0x88, 0x88);
    public static readonly SKColor EdgeDispatched = new(0xD4, 0xA0, 0x20);
    public static readonly SKColor EdgeCompleted = new(0x4E, 0xC9, 0x4E);

    // ── Public helpers ───────────────────────────────────────────────────────

    /// <summary>Returns the axis-aligned bounding rectangle for a layout node.</summary>
    public static SKRect NodeBounds(NodeLayout node) =>
        SKRect.Create(node.CenterX - NodeW / 2f, node.CenterY - NodeH / 2f, NodeW, NodeH);

    // ── Main entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Renders the node graph onto <paramref name="canvas"/>.
    /// </summary>
    /// <param name="canvas">Target canvas (already at device pixels).</param>
    /// <param name="nodes">Pre-computed layout from <see cref="ActionTreeLayoutEngine.ComputeLayout"/>.</param>
    /// <param name="selectedId">ID of the currently selected node, or <c>null</c>.</param>
    /// <param name="density">
    /// Logical-to-physical pixel ratio. The canvas is scaled by this factor before drawing
    /// so that all coordinates in <paramref name="nodes"/> are in logical units.
    /// </param>
    /// <param name="drag">
    /// Optional drag state; when set the renderer draws a ghost node, drag-line, and
    /// drop-target highlight.
    /// </param>
    public static void Render(
        SKCanvas canvas,
        IReadOnlyList<NodeLayout> nodes,
        string? selectedId,
        float density = 1f,
        DragState? drag = null)
    {
        canvas.Clear(CanvasBg);

        if (nodes.Count == 0)
        {
            using var hintPaint = new SKPaint { Color = TextDim, IsAntialias = true };
            using var hintFont = new SKFont { Size = 13 };
            canvas.DrawText("Use the palette to add actions", 24, 40, hintFont, hintPaint);
            return;
        }

        canvas.Save();
        canvas.Scale(density);

        DrawEdges(canvas, nodes);
        DrawNodes(canvas, nodes, selectedId, drag);
        if (drag.HasValue)
            DrawDragIndicator(canvas, nodes, drag.Value);

        canvas.Restore();
    }

    // ── Edge drawing ─────────────────────────────────────────────────────────

    private static void DrawEdges(SKCanvas canvas, IReadOnlyList<NodeLayout> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Action.Id);

        foreach (var child in nodes)
        {
            if (child.Action.Timing is not AfterActionTiming aat) continue;
            if (!byId.TryGetValue(aat.TriggerActionId, out var parent)) continue;

            var isDispatched = aat.TriggerPhase == ActionPhase.Dispatched;
            var edgeColor = isDispatched ? EdgeDispatched : EdgeCompleted;
            var phaseLabel = isDispatched ? "Dispatched" : "Completed";

            var startX = parent.CenterX;
            var startY = parent.CenterY + NodeH / 2f;
            var endX = child.CenterX;
            var endY = child.CenterY - NodeH / 2f;

            using var pathPaint = new SKPaint
            {
                Color = edgeColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true,
            };
            var midY = (startY + endY) / 2f;
            using var path = new SKPath();
            path.MoveTo(startX, startY);
            path.CubicTo(startX, midY, endX, midY, endX, endY);
            canvas.DrawPath(path, pathPaint);

            DrawArrowhead(canvas, endX, endY, edgeColor);

            using var textPaint = new SKPaint { Color = edgeColor, IsAntialias = true };
            using var textFont = new SKFont { Size = 10 };
            canvas.DrawText(
                phaseLabel,
                (startX + endX) / 2f + 4f,
                (startY + endY) / 2f,
                textFont,
                textPaint);
        }
    }

    private static void DrawArrowhead(SKCanvas canvas, float x, float y, SKColor color)
    {
        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(x, y);
        path.LineTo(x - 5f, y - 9f);
        path.LineTo(x + 5f, y - 9f);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    // ── Node drawing ─────────────────────────────────────────────────────────

    private static void DrawNodes(SKCanvas canvas, IReadOnlyList<NodeLayout> nodes, string? selectedId, DragState? drag = null)
    {
        foreach (var node in nodes)
        {
            var b = NodeBounds(node);
            var id = node.Action.Id;
            var isDragged = drag.HasValue && id == drag.Value.DraggedId;
            var isDropTarget = drag.HasValue && id == drag.Value.TargetId;
            var isSelected = id == selectedId && !isDragged;
            var rrect = new SKRoundRect(b, 6f, 6f);

            // Fill — dimmed while dragging
            var fillAlpha = isDragged ? (byte)0x99 : (byte)0xFF;
            using (var bgPaint = new SKPaint
            {
                Color = NodeFill.WithAlpha(fillAlpha),
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            })
                canvas.DrawRoundRect(rrect, bgPaint);

            // Border — varies by state
            SKColor borderColor;
            float borderWidth;
            SKPathEffect? dashEffect = isDragged
                ? SKPathEffect.CreateDash([6f, 4f], 0f)
                : null;

            if (isDropTarget)
            {
                borderColor = NodeBorderDropTarget;
                borderWidth = 2.5f;
            }
            else if (isSelected)
            {
                borderColor = NodeBorderSelect;
                borderWidth = 2f;
            }
            else if (isDragged)
            {
                borderColor = NodeBorderSelect.WithAlpha(0xAA);
                borderWidth = 1.5f;
            }
            else
            {
                borderColor = NodeBorderNormal;
                borderWidth = 1f;
            }

            using (var borderPaint = new SKPaint
            {
                Color = borderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = borderWidth,
                IsAntialias = true,
                PathEffect = dashEffect,
            })
                canvas.DrawRoundRect(rrect, borderPaint);

            dashEffect?.Dispose();

            // Action type label — top-left, small and dimmed
            var textAlpha = isDragged ? (byte)0x99 : (byte)0xFF;
            using var typePaint = new SKPaint { Color = TextDim.WithAlpha(textAlpha), IsAntialias = true };
            using var typeFont = new SKFont { Size = 9 };
            canvas.DrawText(node.Action.Type.ToString(), b.Left + 6f, b.Top + 13f, typeFont, typePaint);

            // ID badge — top-right
            using var idFont = new SKFont { Size = 9 };
            var idText = node.Action.Id;
            var idW = idFont.MeasureText(idText);
            canvas.DrawText(idText, b.Right - idW - 4f, b.Top + 13f, idFont, typePaint);

            // Label — main text, bottom-left
            using var labelPaint = new SKPaint { Color = TextMain.WithAlpha(textAlpha), IsAntialias = true };
            using var labelFont = new SKFont { Size = 12 };
            var labelText = TruncateText(node.Action.Label, labelFont, NodeW - 10f);
            canvas.DrawText(labelText, b.Left + 6f, b.Bottom - 9f, labelFont, labelPaint);
        }
    }

    // ── Drag indicator ───────────────────────────────────────────────────────

    private static void DrawDragIndicator(SKCanvas canvas, IReadOnlyList<NodeLayout> nodes, DragState drag)
    {
        var source = nodes.FirstOrDefault(n => n.Action.Id == drag.DraggedId);
        if (source.Action == null) return;

        // Dashed line from source centre to current cursor position
        using var lineDash = SKPathEffect.CreateDash([5f, 5f], 0f);
        using var linePaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(0x88),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            PathEffect = lineDash,
        };
        canvas.DrawLine(source.CenterX, source.CenterY, drag.CurrentX, drag.CurrentY, linePaint);

        // Ghost node at cursor position
        var ghostRect = SKRect.Create(
            drag.CurrentX - NodeW / 2f,
            drag.CurrentY - NodeH / 2f,
            NodeW, NodeH);
        var ghostRRect = new SKRoundRect(ghostRect, 6f, 6f);

        using (var ghostFill = new SKPaint
        {
            Color = NodeFill.WithAlpha(0xCC),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        })
            canvas.DrawRoundRect(ghostRRect, ghostFill);

        using (var ghostBorder = new SKPaint
        {
            Color = SKColors.White.WithAlpha(0xCC),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true,
        })
            canvas.DrawRoundRect(ghostRRect, ghostBorder);

        // Ghost label
        using var ghostLabelPaint = new SKPaint { Color = TextMain.WithAlpha(0xCC), IsAntialias = true };
        using var ghostLabelFont = new SKFont { Size = 12 };
        var ghostText = TruncateText(source.Action.Label, ghostLabelFont, NodeW - 10f);
        canvas.DrawText(ghostText, ghostRect.Left + 6f, ghostRect.Bottom - 9f, ghostLabelFont, ghostLabelPaint);
    }

    // ── Text helpers ─────────────────────────────────────────────────────────

    public static string TruncateText(string text, SKFont font, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        while (text.Length > 1 && font.MeasureText(text + "\u2026") > maxWidth)
            text = text[..^1];
        return text + "\u2026";
    }
}
