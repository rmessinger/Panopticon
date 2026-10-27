using System;
using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Navigation;

public static class PathOptimizer
{
    public static bool EnablePathSmoothing { get; set; } = true;

    public static void RemoveRedundantNodes(List<Node> nodes)
    {
        var prevDirX = 0;
        var prevDirY = 0;

        for (var i = nodes.Count - 1; i > 0; i--)
        {
            var dirX = nodes[i].X - nodes[i - 1].X;
            var dirY = nodes[i].Y - nodes[i - 1].Y;

            if (dirX == prevDirX && dirY == prevDirY)
            {
                nodes.RemoveAt(i);
            }

            prevDirX = dirX;
            prevDirY = dirY;
        }
    }

    public static void SmoothPath(List<Node> nodes, Graph graph)
    {
        if (!EnablePathSmoothing)
        {
            return;
        }

        var currentStartIndex = 0;

        while (currentStartIndex < nodes.Count - 2)
        {
            var currentEndIndex = currentStartIndex + 2;
            while (currentEndIndex < nodes.Count)
            {
                if (IsDirectLinePassable(nodes[currentStartIndex], nodes[currentEndIndex], graph))
                {
                    nodes.RemoveAt(currentStartIndex + 1);
                }
                else
                {
                    currentStartIndex++;
                    break;
                }
            }
        }
    }

    // WI-N11-01: world-position variant for legs that start or end off the node lattice
    // (live unit positions, sidestep and detour splice points). Mirrors the node walk,
    // including its skip of the start cell: a unit already standing on an impassable cell
    // can still plan its way off it.
    internal static bool IsDirectLinePassable(Navigation navigation, Vector2 start, Vector2 end)
    {
        var startNode = navigation.GetNodeAt(new Vector3(start, 0.0f));
        var endNode = navigation.GetNodeAt(new Vector3(end, 0.0f));
        if (startNode == null || endNode == null || !endNode.IsPassable)
        {
            return false;
        }

        return IsDirectLinePassable(startNode, endNode, navigation.Graph);
    }

    // WI-N7-02: shared with OffsetRouteValidator for offset retrace validation.
    internal static bool IsDirectLinePassable(Node start, Node end, Graph graph)
    {
        // Use the Bresenham algorithm for line rasterization to
        // determine which tiles we need to check:
        // https://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm

        int x1 = start.X;
        int y1 = start.Y;

        int x2 = end.X;
        int y2 = end.Y;

        int dx = Math.Abs(x2 - x1);
        int dy = Math.Abs(y2 - y1);

        int distance = Math.Max(dx, dy);
        int d, dInc1, dInc2;

        int x, xInc1, xInc2;
        int y, yInc1, yInc2;

        if (dx >= dy)
        {
            d = (2 * dy) - dx;
            dInc1 = dy << 1;
            dInc2 = (dy - dx) << 1;
            xInc1 = 1;
            xInc2 = 1;
            yInc1 = 0;
            yInc2 = 1;
        }
        else
        {
            d = (2 * dx) - dy;
            dInc1 = dx << 1;
            dInc2 = (dx - dy) << 1;
            xInc1 = 0;
            xInc2 = 1;
            yInc1 = 1;
            yInc2 = 1;
        }

        if (x1 > x2)
        {
            xInc1 = -xInc1;
            xInc2 = -xInc2;
        }

        if (y1 > y2)
        {
            yInc1 = -yInc1;
            yInc2 = -yInc2;
        }

        x = x1;
        y = y1;

        for (int i = 0; i < distance; i++)
        {
            if (d < 0)
            {
                d += dInc1;
                x += xInc1;
                y += yInc1;
            }
            else
            {
                d += dInc2;
                x += xInc2;
                y += yInc2;
            }

            if (!graph.GetNode(x, y).IsPassable)
            {
                return false;
            }
        }

        return true;
    }
}
