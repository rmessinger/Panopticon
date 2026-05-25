using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Profiles;

/// <summary>
/// Pure, MAUI-free layout engine for the action tree node graph.
/// Extracted so that layout math (row assignment, column centering, hit bounds)
/// can be unit-tested without a running MAUI host.
/// </summary>
public static class ActionTreeLayoutEngine
{
    // ── Layout constants (device-independent units, mirrored in ActionTreeView) ──
    public const float NodeW = 140f;
    public const float NodeH = 44f;
    public const float ColGap = 16f;
    public const float RowGap = 80f;
    public const float CanvasPad = 20f;

    /// <summary>
    /// Assigns a grid row/column and centre coordinates to every action.
    /// Returns an empty list when <paramref name="actions"/> is empty.
    /// </summary>
    public static IReadOnlyList<NodeLayout> ComputeLayout(IReadOnlyList<ActionEntry> actions)
    {
        if (actions.Count == 0)
            return [];

        var byType = new Dictionary<ActionType, ActionEntry>();
        foreach (var a in actions)
            byType[a.Type] = a;

        // Classify: root (FixedTiming / RandomTiming) or child (AfterActionTiming).
        var parentOf = new Dictionary<ActionEntry, ActionEntry>();
        var roots = new List<ActionEntry>();
        foreach (var a in actions)
        {
            if (a.Timing is AfterActionTiming aat && byType.TryGetValue(aat.TriggerAction, out var parent))
                parentOf[a] = parent;
            else
                roots.Add(a);
        }

        // BFS to assign row depths.
        var rowOf = new Dictionary<ActionEntry, int>();
        var queue = new Queue<ActionEntry>(roots);
        foreach (var r in roots)
            rowOf[r] = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var depth = rowOf[current];
            foreach (var a in actions)
            {
                if (!rowOf.ContainsKey(a) && parentOf.TryGetValue(a, out var p) && p == current)
                {
                    rowOf[a] = depth + 1;
                    queue.Enqueue(a);
                }
            }
        }

        // Orphans (AfterActionTiming referencing a non-existent predecessor).
        var maxAssigned = rowOf.Values.DefaultIfEmpty(0).Max();
        foreach (var a in actions)
        {
            if (!rowOf.ContainsKey(a))
                rowOf[a] = ++maxAssigned;
        }

        // Group by row, preserving original list order within each row.
        var byRow = actions
            .GroupBy(a => rowOf[a])
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Canvas size.
        var numRows = byRow.Keys.Max() + 1;
        var maxRowWidth = byRow.Values.Max(r => r.Count * NodeW + Math.Max(0, r.Count - 1) * ColGap);
        var canvasW = maxRowWidth + 2 * CanvasPad;
        var canvasH = numRows * NodeH + Math.Max(0, numRows - 1) * RowGap + 2 * CanvasPad;

        // Place nodes: centre each row horizontally within the canvas.
        var nodes = new List<NodeLayout>();
        foreach (var (row, rowActions) in byRow)
        {
            var rowWidth = rowActions.Count * NodeW + Math.Max(0, rowActions.Count - 1) * ColGap;
            var rowStartX = (canvasW - rowWidth) / 2f + NodeW / 2f;
            var centerY = CanvasPad + row * (NodeH + RowGap) + NodeH / 2f;

            for (var col = 0; col < rowActions.Count; col++)
            {
                nodes.Add(new NodeLayout(
                    Action: rowActions[col],
                    Row: row,
                    Col: col,
                    CenterX: rowStartX + col * (NodeW + ColGap),
                    CenterY: centerY));
            }
        }

        return nodes;
    }

    /// <summary>
    /// Returns true when the point (<paramref name="x"/>, <paramref name="y"/>) falls
    /// inside the bounding rectangle of <paramref name="node"/>.
    /// </summary>
    public static bool HitTest(NodeLayout node, float x, float y)
    {
        var left = node.CenterX - NodeW / 2f;
        var top = node.CenterY - NodeH / 2f;
        return x >= left && x <= left + NodeW && y >= top && y <= top + NodeH;
    }

    /// <summary>Applies toggle semantics: click same node → deselect; click other → select.</summary>
    public static NodeLayout? ToggleSelection(NodeLayout? current, NodeLayout? clicked)
        => clicked == current ? null : clicked;
}

/// <summary>Platform-free representation of a positioned action node.</summary>
public readonly record struct NodeLayout(
    ActionEntry Action,
    int Row,
    int Col,
    float CenterX,
    float CenterY);
