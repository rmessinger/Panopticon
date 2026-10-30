using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-N11-01: finds the narrow passages a route crosses, from the passability grid
/// alone. A passage span is a run of route samples whose perpendicular cross-section is
/// closed on both sides and narrower than the lane limit in pitches; the narrowest sample
/// is the throat. Pure detection: no movement code calls this (movement is WI-N12).
/// </summary>
internal static class PassageFinder
{
    // Eight-way directions, clockwise from east; index = quantized octant.
    private static readonly (int DX, int DY)[] Octants =
    [
        (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
    ];

    /// <summary>WI-N11-01 debug counter: ray casts issued (surfaced via PathStatistics).</summary>
    internal static long Casts;

    /// <summary>WI-N11-01 debug counter: casts avoided by the cross-section cache.</summary>
    internal static long CacheHits;

    internal static void ResetCounters()
    {
        Interlocked.Exchange(ref Casts, 0);
        Interlocked.Exchange(ref CacheHits, 0);
    }

    /// <summary>
    /// World-route entry point: maps the route onto grid cells (one sample per cell) and
    /// applies the navigation options. Navigation is used only for world-to-cell mapping;
    /// the span scan itself runs on the passability grid alone.
    /// </summary>
    internal static IReadOnlyList<PassageSpan> FindSpans(
        Navigation navigation,
        IReadOnlyList<Vector3> route,
        float pitch,
        CrossSectionCache cache)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(cache);

        if (route.Count < 2)
        {
            return [];
        }

        var options = navigation.Options;
        var cells = new List<(int X, int Y)>(route.Count);
        foreach (var point in route)
        {
            var node = navigation.GetNodeAt(point);
            if (node == null)
            {
                continue;
            }

            if (cells.Count > 0 && cells[^1] == (node.X, node.Y))
            {
                continue;
            }

            cells.Add((node.X, node.Y));
        }

        return FindSpans(
            navigation.Graph,
            cells,
            pitch,
            DeriveCellSize(navigation),
            options.MaxPassageWidthCells,
            options.PassageLaneLimit,
            options.PassageMergeCells,
            cache);
    }

    /// <summary>
    /// Grid-cell core: each route sample casts two perpendicular rays capped at
    /// <paramref name="maxWidthCells"/>; samples whose section is closed on both sides and
    /// at most <paramref name="laneLimit"/> pitches wide form spans, merged across gaps of
    /// at most <paramref name="mergeCells"/> samples. The narrowest sample is the throat.
    /// </summary>
    internal static IReadOnlyList<PassageSpan> FindSpans(
        Graph graph,
        IReadOnlyList<(int X, int Y)> route,
        float pitch,
        float cellSize,
        int maxWidthCells,
        int laneLimit,
        int mergeCells,
        CrossSectionCache cache)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(cache);
        if (pitch <= 0 || cellSize <= 0 || maxWidthCells < 1 || laneLimit < 1 || route.Count < 2)
        {
            return [];
        }

        var pitchCells = Math.Max(1, (int)MathF.Ceiling(pitch / cellSize));
        var maxSpanWidthCells = laneLimit * pitchCells;
        var spans = new List<PassageSpan>();
        var corridor = new HashSet<(int, int)>();
        var start = -1;
        var end = -1;
        var throatCell = (X: 0, Y: 0);
        var throatWidth = int.MaxValue;

        for (var i = 0; i < route.Count; i++)
        {
            var (width, closed, cells) = CrossSection(graph, route, i, maxWidthCells, cache);
            var inSpan = closed && width <= maxSpanWidthCells;
            if (inSpan)
            {
                corridor.UnionWith(cells);
                if (start < 0)
                {
                    start = i;
                }

                end = i;
                if (width < throatWidth)
                {
                    throatCell = route[i];
                    throatWidth = width;
                }
            }

            var spanEnded = start >= 0 && (!inSpan || i == route.Count - 1);
            if (!spanEnded)
            {
                continue;
            }

            spans.Add(new PassageSpan(
                start,
                end,
                throatCell,
                throatWidth,
                Math.Max(1, throatWidth / pitchCells),
                SampleDirection(route, start, end),
                [.. corridor]));
            corridor.Clear();
            start = -1;
            end = -1;
            throatCell = (0, 0);
            throatWidth = int.MaxValue;
        }

        return MergeSpans(spans, pitchCells, mergeCells);
    }

    private static (int Width, bool Closed, IReadOnlyCollection<(int, int)> Cells) CrossSection(
        Graph graph,
        IReadOnlyList<(int X, int Y)> route,
        int index,
        int maxWidthCells,
        CrossSectionCache cache)
    {
        var direction = SampleDirection(route, index);
        var octant = QuantizeOctant(direction);

        // Cross-sections run perpendicular to travel: two octant steps = 90 degrees.
        var left = CastRay(graph, route[index], (octant + 2) % 8, maxWidthCells, cache);
        var right = CastRay(graph, route[index], (octant + 6) % 8, maxWidthCells, cache);

        // Open on either side (ray capped) is never a passage, per the deep dive.
        if (left.Width is null || right.Width is null)
        {
            return (int.MaxValue, false, []);
        }

        var width = left.Width.Value + right.Width.Value + 1;
        var cells = new List<(int, int)>(width);
        cells.AddRange(left.Cells);
        cells.Add(route[index]);
        cells.AddRange(right.Cells);

        return (width, true, cells);
    }

    private static (int? Width, IReadOnlyList<(int, int)> Cells) CastRay(
        Graph graph,
        (int X, int Y) origin,
        int octant,
        int maxWidthCells,
        CrossSectionCache cache)
    {
        if (cache.TryGetWidth(origin.X, origin.Y, octant, out var cached))
        {
            return (cached, []);
        }

        var (dx, dy) = Octants[octant];
        var walked = new List<(int, int)>(maxWidthCells);
        var steps = 0;
        while (steps < maxWidthCells)
        {
            var x = origin.X + dx * (steps + 1);
            var y = origin.Y + dy * (steps + 1);
            if (!graph.TryGetNode(x, y, out var node) || !node.IsPassable)
            {
                break;
            }

            walked.Add((x, y));
            steps++;
        }

        // Reaching the cap means open terrain in that direction, not a wall.
        int? width = steps < maxWidthCells ? steps : null;
        cache.Store(origin.X, origin.Y, octant, width);
        return (width, walked);
    }

    private static Vector2 SampleDirection(IReadOnlyList<(int X, int Y)> route, int index)
    {
        var next = index + 1 < route.Count ? route[index + 1] : route[index];
        var previous = index > 0 ? route[index - 1] : route[index];
        return DirectionOf(next.X - previous.X, next.Y - previous.Y);
    }

    private static Vector2 SampleDirection(IReadOnlyList<(int X, int Y)> route, int start, int end)
    {
        return DirectionOf(route[end].X - route[start].X, route[end].Y - route[start].Y);
    }

    private static Vector2 DirectionOf(int dx, int dy)
    {
        return dx == 0 && dy == 0 ? Vector2.UnitX : Vector2.Normalize(new Vector2(dx, dy));
    }

    private static int QuantizeOctant(Vector2 direction)
    {
        var angle = MathF.Atan2(direction.Y, direction.X);
        if (angle < 0)
        {
            angle += MathF.Tau;
        }

        return (int)MathF.Round(angle / (MathF.Tau / 8)) % 8;
    }

    private static List<PassageSpan> MergeSpans(List<PassageSpan> spans, int pitchCells, int mergeCells)
    {
        if (spans.Count < 2)
        {
            return spans;
        }

        var merged = new List<PassageSpan> { spans[0] };
        for (var i = 1; i < spans.Count; i++)
        {
            var last = merged[^1];
            var current = spans[i];
            if (current.EntryIndex - last.ExitIndex > mergeCells)
            {
                merged.Add(current);
                continue;
            }

            var throat = current.WidthCells < last.WidthCells ? current.ThroatCell : last.ThroatCell;
            var width = Math.Min(last.WidthCells, current.WidthCells);
            var corridor = new HashSet<(int, int)>(last.Corridor);
            corridor.UnionWith(current.Corridor);
            merged[^1] = last with
            {
                ExitIndex = current.ExitIndex,
                ThroatCell = throat,
                WidthCells = width,
                Lanes = Math.Max(1, width / pitchCells),
                Corridor = [.. corridor],
            };
        }

        return merged;
    }

    private static float DeriveCellSize(Navigation navigation)
    {
        var origin = navigation.GetNodeWorldPosition(navigation.Graph.GetNode(0, 0));
        var next = navigation.GetNodeWorldPosition(navigation.Graph.GetNode(
            Math.Min(1, navigation.Graph.Width - 1),
            0));
        var size = MathF.Abs(next.X - origin.X);
        return size > 0.001f ? size : 10.0f;
    }
}
