using System.Collections.Generic;
using System.Linq;
using OpenSage.Tools.ReplaySketch.Model;
using SkiaSharp;

namespace OpenSage.Tools.ReplaySketch.Profiles;

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
    public static void Render(
        SKCanvas canvas,
        IReadOnlyList<NodeLayout> nodes,
        string? selectedId,
        float density = 1f)
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
        DrawNodes(canvas, nodes, selectedId);

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

    private static void DrawNodes(SKCanvas canvas, IReadOnlyList<NodeLayout> nodes, string? selectedId)
    {
        foreach (var node in nodes)
        {
            var b = NodeBounds(node);
            var isSelected = node.Action.Id == selectedId;
            var rrect = new SKRoundRect(b, 6f, 6f);

            using (var bgPaint = new SKPaint { Color = NodeFill, Style = SKPaintStyle.Fill, IsAntialias = true })
                canvas.DrawRoundRect(rrect, bgPaint);

            using (var borderPaint = new SKPaint
            {
                Color = isSelected ? NodeBorderSelect : NodeBorderNormal,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isSelected ? 2f : 1f,
                IsAntialias = true,
            })
                canvas.DrawRoundRect(rrect, borderPaint);

            // Action type label — top-left, small and dimmed
            using var typePaint = new SKPaint { Color = TextDim, IsAntialias = true };
            using var typeFont = new SKFont { Size = 9 };
            canvas.DrawText(node.Action.Type.ToString(), b.Left + 6f, b.Top + 13f, typeFont, typePaint);

            // ID badge — top-right
            using var idFont = new SKFont { Size = 9 };
            var idText = node.Action.Id;
            var idW = idFont.MeasureText(idText);
            canvas.DrawText(idText, b.Right - idW - 4f, b.Top + 13f, idFont, typePaint);

            // Label — main text, bottom-left
            using var labelPaint = new SKPaint { Color = TextMain, IsAntialias = true };
            using var labelFont = new SKFont { Size = 12 };
            var labelText = TruncateText(node.Action.Label, labelFont, NodeW - 10f);
            canvas.DrawText(labelText, b.Left + 6f, b.Bottom - 9f, labelFont, labelPaint);
        }
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
