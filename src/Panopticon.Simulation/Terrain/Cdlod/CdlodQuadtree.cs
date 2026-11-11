#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Terrain.Cdlod;

public sealed class CdlodQuadtree
{
    private const float HysteresisFactor = 1.25f;

    private static readonly CdlodEdges[] Edges = [CdlodEdges.North, CdlodEdges.East, CdlodEdges.South, CdlodEdges.West];

    private readonly int _latticeWidth;
    private readonly int _latticeHeight;
    private readonly ICdlodErrorProvider _errorProvider;
    private readonly int _maxNodes;
    private readonly float _errorThresholdPixels;
    private readonly int _rootLevel;

    public CdlodQuadtree(
        int latticeWidth,
        int latticeHeight,
        ICdlodErrorProvider errorProvider,
        int maxNodes,
        float errorThresholdPixels)
    {
        ArgumentNullException.ThrowIfNull(errorProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(latticeWidth, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(latticeHeight, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        Ensure.FinitePositive(errorThresholdPixels, "Error threshold pixels must be finite and positive.");

        _latticeWidth = latticeWidth;
        _latticeHeight = latticeHeight;
        _errorProvider = errorProvider;
        _maxNodes = maxNodes;
        _errorThresholdPixels = errorThresholdPixels;

        var intervals = Math.Max((long)latticeWidth, latticeHeight) - 1;
        var level = 0;
        while ((long)CdlodNode.LeafIntervals << level < intervals)
        {
            level++;
        }

        _rootLevel = level;
    }

    public CdlodSelection Select(CdlodCamera camera, CdlodSelection? previous = null)
    {
        ArgumentNullException.ThrowIfNull(camera.Frustum);
        Ensure.FinitePositive(camera.ProjectedScale, "Projected scale must be finite and positive.");

        var pass = new SelectionPass(camera, CollectAncestors(previous));
        var root = new CdlodNode(0, 0, _rootLevel);
        if (!IsVisible(root, pass))
        {
            return new CdlodSelection(Array.Empty<CdlodSelectedNode>());
        }

        pass.Cut.Add(root);
        SplitGreedily(pass);
        EnforceRestrictedRule(pass);
        return BuildSelection(pass);
    }

    private sealed class SelectionPass(CdlodCamera camera, HashSet<CdlodNode> previousAncestors)
    {
        public CdlodCamera Camera { get; } = camera;

        public HashSet<CdlodNode> PreviousAncestors { get; } = previousAncestors;

        public Dictionary<CdlodNode, (float Min, float Max)> Ranges { get; } = new();

        public HashSet<CdlodNode> Cut { get; } = new();
    }

    private HashSet<CdlodNode> CollectAncestors(CdlodSelection? previous)
    {
        var ancestors = new HashSet<CdlodNode>();
        if (previous is null)
        {
            return ancestors;
        }

        foreach (var selected in previous.Nodes)
        {
            for (var level = selected.Node.Level + 1; level <= _rootLevel; level++)
            {
                ancestors.Add(Ancestor(selected.Node, level));
            }
        }

        return ancestors;
    }

    private void SplitGreedily(SelectionPass pass)
    {
        var queue = new PriorityQueue<CdlodNode, (float NegativeError, int Level, int Y, int X)>();
        TryEnqueue(new CdlodNode(0, 0, _rootLevel), pass, queue);

        while (queue.TryDequeue(out var node, out _))
        {
            if (!pass.Cut.Contains(node))
            {
                continue;
            }

            var children = VisibleChildren(node, pass);
            if (children.Count == 0 || pass.Cut.Count + children.Count - 1 > _maxNodes)
            {
                continue;
            }

            pass.Cut.Remove(node);
            foreach (var child in children)
            {
                pass.Cut.Add(child);
                TryEnqueue(child, pass, queue);
            }
        }
    }

    private void TryEnqueue(CdlodNode node, SelectionPass pass, PriorityQueue<CdlodNode, (float, int, int, int)> queue)
    {
        if (node.Level == 0)
        {
            return;
        }

        var error = ScreenError(node, pass);
        if (error > SplitThreshold(node, pass))
        {
            queue.Enqueue(node, (-error, node.Level, node.Y, node.X));
        }
    }

    private void EnforceRestrictedRule(SelectionPass pass)
    {
        var passLimit = (long)_maxNodes * (_rootLevel + 2) * 4;
        var fixes = 0L;
        while (fixes < passLimit && FixFirstViolation(pass))
        {
            fixes++;
        }

        VerifyRestrictedRule(pass);
    }

    private void VerifyRestrictedRule(SelectionPass pass)
    {
        foreach (var node in pass.Cut)
        {
            foreach (var edge in Edges)
            {
                foreach (var neighbour in TouchingCut(node, edge, pass))
                {
                    if (Math.Abs(node.Level - neighbour.Level) > 1)
                    {
                        throw new InvalidOperationException(
                            $"Restricted quadtree rule violated between node {node} (level {node.Level}) and neighbour {neighbour} (level {neighbour.Level}).");
                    }
                }
            }
        }
    }

    private bool FixFirstViolation(SelectionPass pass)
    {
        foreach (var node in SortedCut(pass))
        {
            foreach (var edge in Edges)
            {
                foreach (var neighbour in TouchingCut(node, edge, pass))
                {
                    if (neighbour.Level > node.Level - 2)
                    {
                        continue;
                    }

                    if (TrySplit(neighbour, pass))
                    {
                        return true;
                    }

                    MergeIntoParent(node, pass);
                    return true;
                }
            }
        }

        return false;
    }

    private bool TrySplit(CdlodNode node, SelectionPass pass)
    {
        if (node.Level == 0)
        {
            return false;
        }

        var children = VisibleChildren(node, pass);
        if (children.Count == 0 || pass.Cut.Count + children.Count - 1 > _maxNodes)
        {
            return false;
        }

        pass.Cut.Remove(node);
        foreach (var child in children)
        {
            pass.Cut.Add(child);
        }

        return true;
    }

    private static void MergeIntoParent(CdlodNode node, SelectionPass pass)
    {
        var parent = Ancestor(node, node.Level + 1);
        pass.Cut.RemoveWhere(candidate => IsWithin(candidate, parent));
        pass.Cut.Add(parent);
    }

    private CdlodSelection BuildSelection(SelectionPass pass)
    {
        var nodes = new List<CdlodSelectedNode>(pass.Cut.Count);
        foreach (var node in pass.Cut)
        {
            var error = ScreenError(node, pass);
            var morph = Math.Clamp(1f - error / _errorThresholdPixels, 0f, 1f);
            nodes.Add(new CdlodSelectedNode(node, morph, CoarserEdgesOf(node, pass)));
        }

        nodes.Sort(static (a, b) => CompareNodes(a.Node, b.Node));
        return new CdlodSelection(nodes);
    }

    private CdlodEdges CoarserEdgesOf(CdlodNode node, SelectionPass pass)
    {
        var flags = CdlodEdges.None;
        foreach (var edge in Edges)
        {
            foreach (var neighbour in TouchingCut(node, edge, pass))
            {
                if (neighbour.Level == node.Level + 1)
                {
                    flags |= edge;
                }
            }
        }

        return flags;
    }

    private List<CdlodNode> TouchingCut(CdlodNode node, CdlodEdges edge, SelectionPass pass)
    {
        var result = new List<CdlodNode>();
        var extent = node.ClippedExtent(_latticeWidth, _latticeHeight);
        var alongX = edge is CdlodEdges.North or CdlodEdges.South;
        var (line, segmentLo, segmentHi) = alongX
            ? (edge == CdlodEdges.North ? extent.Bottom : extent.Y - 1, extent.X, extent.Right - 1)
            : (edge == CdlodEdges.East ? extent.Right : extent.X - 1, extent.Y, extent.Bottom - 1);

        var lineLimit = alongX ? (long)_latticeHeight - 1 : (long)_latticeWidth - 1;
        if (line < 0 || line > lineLimit)
        {
            return result;
        }

        CollectTouching(new CdlodNode(0, 0, _rootLevel), alongX, line, segmentLo, segmentHi, pass, result);
        return result;
    }

    private void CollectTouching(
        CdlodNode cell,
        bool lineIsRow,
        int line,
        int segmentLo,
        int segmentHi,
        SelectionPass pass,
        List<CdlodNode> result)
    {
        var extent = cell.ClippedExtent(_latticeWidth, _latticeHeight);
        var (lineLo, lineHi, cellLo, cellHi) = lineIsRow
            ? (extent.Y, extent.Bottom - 1, extent.X, extent.Right - 1)
            : (extent.X, extent.Right - 1, extent.Y, extent.Bottom - 1);

        if (line < lineLo || line > lineHi || Math.Min(cellHi, segmentHi) <= Math.Max(cellLo, segmentLo))
        {
            return;
        }

        if (pass.Cut.Contains(cell))
        {
            result.Add(cell);
            return;
        }

        if (cell.Level == 0 || !IsVisible(cell, pass))
        {
            return;
        }

        foreach (var child in ChildrenOf(cell))
        {
            CollectTouching(child, lineIsRow, line, segmentLo, segmentHi, pass, result);
        }
    }

    private List<CdlodNode> SortedCut(SelectionPass pass)
    {
        var nodes = new List<CdlodNode>(pass.Cut);
        nodes.Sort(CompareNodes);
        return nodes;
    }

    private List<CdlodNode> VisibleChildren(CdlodNode node, SelectionPass pass)
    {
        var visible = new List<CdlodNode>(4);
        foreach (var child in ChildrenOf(node))
        {
            if (IsVisible(child, pass))
            {
                visible.Add(child);
            }
        }

        return visible;
    }

    private List<CdlodNode> ChildrenOf(CdlodNode node)
    {
        var children = new List<CdlodNode>(4);
        if (node.Level == 0)
        {
            return children;
        }

        var childLevel = node.Level - 1;
        var span = (long)CdlodNode.LeafIntervals << childLevel;
        var lastX = (long)_latticeWidth - 1;
        var lastY = (long)_latticeHeight - 1;
        for (var j = 0; j < 2; j++)
        {
            for (var i = 0; i < 2; i++)
            {
                var x = (node.X * 2) + i;
                var y = (node.Y * 2) + j;
                if (x * span < lastX && y * span < lastY)
                {
                    children.Add(new CdlodNode(x, y, childLevel));
                }
            }
        }

        return children;
    }

    private bool IsVisible(CdlodNode node, SelectionPass pass)
    {
        var box = WorldBox(node, GetRange(node, pass));
        return pass.Camera.Frustum.Intersects(box) || ContainsXY(box, pass.Camera.Position);
    }

    private float ScreenError(CdlodNode node, SelectionPass pass)
    {
        var box = WorldBox(node, GetRange(node, pass));
        var range = box.Max.Z - box.Min.Z;
        var closest = Vector3.Clamp(pass.Camera.Position, box.Min, box.Max);
        var distance = Vector3.Distance(pass.Camera.Position, closest);
        if (distance <= 0f)
        {
            return float.PositiveInfinity;
        }

        return range * pass.Camera.ProjectedScale / distance;
    }

    private float SplitThreshold(CdlodNode node, SelectionPass pass)
    {
        return pass.PreviousAncestors.Contains(node)
            ? _errorThresholdPixels / HysteresisFactor
            : _errorThresholdPixels;
    }

    private (float Min, float Max) GetRange(CdlodNode node, SelectionPass pass)
    {
        if (pass.Ranges.TryGetValue(node, out var range))
        {
            return range;
        }

        range = _errorProvider.GetHeightRange(node);
        if (!float.IsFinite(range.Min) || !float.IsFinite(range.Max) || range.Min > range.Max)
        {
            throw new InvalidOperationException(
                $"Error provider returned invalid height range [{range.Min}, {range.Max}] for node {node}.");
        }

        pass.Ranges[node] = range;
        return range;
    }

    private AxisAlignedBoundingBox WorldBox(CdlodNode node, (float Min, float Max) heights)
    {
        var extent = node.ClippedExtent(_latticeWidth, _latticeHeight);
        var scale = HeightMap.HorizontalScale;
        return new AxisAlignedBoundingBox(
            new Vector3((float)extent.X * scale, (float)extent.Y * scale, heights.Min),
            new Vector3((float)(extent.Right - 1) * scale, (float)(extent.Bottom - 1) * scale, heights.Max));
    }

    private static bool ContainsXY(AxisAlignedBoundingBox box, Vector3 point)
    {
        return point.X >= box.Min.X && point.X <= box.Max.X && point.Y >= box.Min.Y && point.Y <= box.Max.Y;
    }

    private static CdlodNode Ancestor(CdlodNode node, int level)
    {
        var shift = level - node.Level;
        return new CdlodNode(node.X >> shift, node.Y >> shift, level);
    }

    private static bool IsWithin(CdlodNode node, CdlodNode ancestor)
    {
        if (node.Level > ancestor.Level)
        {
            return false;
        }

        return Ancestor(node, ancestor.Level) == ancestor;
    }

    private static int CompareNodes(CdlodNode a, CdlodNode b)
    {
        var order = a.Level.CompareTo(b.Level);
        if (order != 0)
        {
            return order;
        }

        order = a.Y.CompareTo(b.Y);
        return order != 0 ? order : a.X.CompareTo(b.X);
    }
}
