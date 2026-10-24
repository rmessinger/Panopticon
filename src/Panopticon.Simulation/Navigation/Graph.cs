using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Graphics.Cameras;
using Panopticon.Gui;
using Panopticon.Mathematics;
using Priority_Queue;

namespace Panopticon.Navigation;

public class Graph
{
    private readonly Node[,] _nodes;

    public int Width => _nodes.GetLength(0);
    public int Height => _nodes.GetLength(1);

    private readonly FastPriorityQueue<Node> _unexpandedNodes;
    private readonly List<(Node, int)> _adjacentNodes;
    private readonly List<Node> _route = new();
    private long _searchGeneration;
    private long _searchNodesExpanded;

    /// <summary>Total nodes dequeued (expanded) by <see cref="Search"/> on this graph instance
    /// (WI-N0-01). Per-instance lifetime counter; Navigation reads before/after deltas around a
    /// search. Each graph is searched by one thread only (live graph on the logic thread,
    /// snapshot clones on the async worker), so plain increments suffice.</summary>
    public long SearchNodesExpanded => _searchNodesExpanded;

    public Node GetNode(int x, int y)
    {
        return _nodes[x, y];
    }

    public bool TryGetNode(int x, int y, out Node node)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            node = _nodes[x, y];
            return true;
        }

        node = default;
        return false;
    }

    public Graph(int w, int h)
    {
        _nodes = new Node[w, h];
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                _nodes[x, y] = new Node(this, x, y);
            }
        }

        _unexpandedNodes = new FastPriorityQueue<Node>(w * h);
        _adjacentNodes = new List<(Node, int)>(8);
    }

    // Submission-time snapshot for async path workers (WI-B4-05): copies passability only.
    // Search scratch is per-instance, so each clone searches independently of the live graph.
    internal Graph ClonePassability()
    {
        var clone = new Graph(Width, Height);

        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                clone.GetNode(x, y).Passability = GetNode(x, y).Passability;
            }
        }

        return clone;
    }

    // Fills the reusable route scratch (WI-N1-01): the returned list is shared per graph and
    // stays valid only until the next Search on the same graph.
    private List<Node> GetPath(Node start, Node end)
    {
        _route.Clear();
        _route.Add(end);
        var iter = end.CameFrom;

        while (iter != start)
        {
            _route.Add(iter);
            iter = iter.CameFrom;
        }

        _route.Reverse();

        return _route;
    }

    // A* pathfinding. Returns the graph's shared route list; callers must consume it before
    // issuing the next Search (WI-N1-01).
    public List<Node> Search(Node start, Node end)
    {
        _unexpandedNodes.Clear();
        _searchGeneration++;

        start.CameFrom = start;
        start.ShortestKnownDistance = 0;
        _unexpandedNodes.Enqueue(start, 0);

        while (_unexpandedNodes.Count > 0)
        {
            var current = _unexpandedNodes.Dequeue();
            current.SearchGeneration = _searchGeneration;
            _unexpandedNodes.ResetNode(current);
            _searchNodesExpanded++;

            if (current == end)
            {
                return GetPath(start, end);
            }

            var distanceToCurrent = current.ShortestKnownDistance;

            _adjacentNodes.Clear();
            current.GetAdjacentNodes(_adjacentNodes);

            foreach (var (next, cost) in _adjacentNodes)
            {
                if (next.SearchGeneration == _searchGeneration)
                {
                    continue;
                }

                var distanceToNext = distanceToCurrent + cost;
                var estimatedDistanceToEnd = distanceToNext + next.EstimateDistance(end);

                if (_unexpandedNodes.Contains(next))
                {
                    if (distanceToNext < next.ShortestKnownDistance)
                    {
                        next.ShortestKnownDistance = distanceToNext;
                        _unexpandedNodes.UpdatePriority(next, estimatedDistanceToEnd);
                        next.CameFrom = current;
                    }
                }
                else
                {
                    next.ShortestKnownDistance = distanceToNext;
                    _unexpandedNodes.Enqueue(next, estimatedDistanceToEnd);
                    next.CameFrom = current;
                }
            }
        }

        return null;
    }

    public void DebugDraw(DrawingContext2D context, Camera camera, Func<Node, Vector2> getNodePosition, Func<float, float, float> getHeight)
    {
        foreach (var node in _nodes)
        {
            if (node.IsPassable) continue;

            var xy = getNodePosition(node);
            var xyz = new Vector3(xy, getHeight(xy.X, xy.Y));

            if (!camera.BoundingFrustum.Contains(xyz)) continue;

            var xy_screen = camera.WorldToScreenPoint(xyz).Vector2XY();
            context.DrawRectangle(new RectangleF(xy_screen, new SizeF(10.0f)), ColorRgbaF.Red, 1.0f);
        }
    }
}
