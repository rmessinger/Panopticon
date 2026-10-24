using System;
using System.Collections.Generic;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-N1-02: connected-region labels over a graph's grid. The flood fill uses exactly the
/// neighbour rule of <see cref="Node.GetAdjacentNodes"/>, so two passable cells share a label
/// iff <see cref="Graph.Search"/> can walk between them. Labels are computed lazily on the
/// first query and recomputed after <see cref="MarkDirty"/>; passability mutations between
/// queries cost nothing. Impassable cells keep label 0.
/// </summary>
public sealed class NavigationRegions
{
    private readonly Graph _graph;
    private readonly int[] _labels;
    private readonly Queue<Node> _frontier = new();
    private readonly List<(Node, int)> _adjacentNodes = new(8);
    private bool _dirty = true;

    public NavigationRegions(Graph graph)
    {
        _graph = graph;
        _labels = new int[graph.Width * graph.Height];
    }

    /// <summary>Marks labels stale; the next <see cref="SameRegion"/> query relabels.</summary>
    public void MarkDirty() => _dirty = true;

    public bool SameRegion(Node a, Node b)
    {
        if (_dirty)
        {
            Relabel();
        }

        return _labels[Index(a)] != 0 && _labels[Index(a)] == _labels[Index(b)];
    }

    private int Index(Node node) => node.Y * _graph.Width + node.X;

    private void Relabel()
    {
        Array.Clear(_labels);
        var label = 0;

        for (var y = 0; y < _graph.Height; y++)
        {
            for (var x = 0; x < _graph.Width; x++)
            {
                var node = _graph.GetNode(x, y);
                if (!node.IsPassable || _labels[Index(node)] != 0)
                {
                    continue;
                }

                label++;
                _labels[Index(node)] = label;
                _frontier.Enqueue(node);

                while (_frontier.Count > 0)
                {
                    var current = _frontier.Dequeue();
                    _adjacentNodes.Clear();
                    current.GetAdjacentNodes(_adjacentNodes);

                    foreach (var (next, _) in _adjacentNodes)
                    {
                        if (_labels[Index(next)] == 0)
                        {
                            _labels[Index(next)] = label;
                            _frontier.Enqueue(next);
                        }
                    }
                }
            }
        }

        _dirty = false;
    }
}
