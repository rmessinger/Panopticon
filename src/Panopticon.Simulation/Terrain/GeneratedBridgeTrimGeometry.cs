#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Terrain;

/// <summary>
/// Planar clipping and capping used to trim one native-scale bridge piece to the length left
/// between its neighbours. Coordinates are route-local (X lateral, Y along the route, Z up).
/// </summary>
internal static class GeneratedBridgeTrimGeometry
{
    /// <summary>
    /// Sutherland-Hodgman clip of a polygon to Y &lt;= yClip in double precision. Each plane
    /// crossing is appended to <paramref name="crossings"/> as an (X, Z) pair in edge order.
    /// </summary>
    internal static List<(double X, double Y, double Z)> ClipBelowPlane(
        IReadOnlyList<(double X, double Y, double Z)> polygon,
        double yClip,
        List<(double X, double Z)> crossings)
    {
        return ClipBelowPlaneWithSources(polygon, yClip, crossings).Select(vertex => vertex.Position).ToList();
    }

    /// <summary>
    /// <see cref="ClipBelowPlane"/> with provenance: an original vertex reports itself (From == To, T 0); a
    /// crossing reports the edge it lies on and the interpolation parameter it was placed with.
    /// </summary>
    internal static List<((double X, double Y, double Z) Position, int From, int To, double T)> ClipBelowPlaneWithSources(
        IReadOnlyList<(double X, double Y, double Z)> polygon,
        double yClip,
        List<(double X, double Z)> crossings)
    {
        var output = new List<((double X, double Y, double Z) Position, int From, int To, double T)>(4);

        for (var k = 0; k < polygon.Count; k++)
        {
            var p = polygon[k];
            var next = (k + 1) % polygon.Count;
            var q = polygon[next];
            var dp = yClip - p.Y;
            var dq = yClip - q.Y;

            if (dp >= 0d)
            {
                output.Add((p, k, k, 0d));
            }

            if ((dp >= 0d) != (dq >= 0d))
            {
                var t = dp / (dp - dq);
                var ix = p.X + ((q.X - p.X) * t);
                var iz = p.Z + ((q.Z - p.Z) * t);
                output.Add(((ix, yClip, iz), k, next, t));
                crossings.Add((ix, iz));
            }
        }

        return output;
    }

    /// <summary>
    /// Welds the cut segments at the plane Y = yClip, traces each closed loop, ear-clips it and
    /// emits cap triangles facing +Y. Loops that cannot be traced or triangulated count as open chains.
    /// </summary>
    internal static GeneratedBridgeCaps BuildCaps(IReadOnlyList<((double X, double Z) A, (double X, double Z) B)> segments, double yClip)
    {
        var nodePositions = new List<(double X, double Z)>();
        var nodeIds = new Dictionary<(long, long), int>();
        var adjacency = new List<List<int>>();

        int NodeOf((double X, double Z) point)
        {
            var key = ((long)Math.Round(point.X * 1e4d), (long)Math.Round(point.Z * 1e4d));

            if (!nodeIds.TryGetValue(key, out var id))
            {
                id = nodePositions.Count;
                nodePositions.Add(point);
                nodeIds[key] = id;
                adjacency.Add([]);
            }

            return id;
        }

        foreach (var segment in segments)
        {
            var a = NodeOf(segment.A);
            var b = NodeOf(segment.B);

            if (a != b)
            {
                adjacency[a].Add(b);
                adjacency[b].Add(a);
            }
        }

        var loops = 0;
        var openChains = 0;
        var nonManifoldNodes = adjacency.Count(list => list.Count > 2);
        var area = 0d;
        var triangles = new List<GeneratedBridgeCapTriangle>();
        var visited = new bool[nodePositions.Count];

        for (var start = 0; start < nodePositions.Count; start++)
        {
            if (visited[start])
            {
                continue;
            }

            var component = new List<int>();
            var stack = new Stack<int>();
            stack.Push(start);
            visited[start] = true;

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                component.Add(node);

                foreach (var neighbour in adjacency[node])
                {
                    if (!visited[neighbour])
                    {
                        visited[neighbour] = true;
                        stack.Push(neighbour);
                    }
                }
            }

            if (component.Any(node => adjacency[node].Count != 2))
            {
                openChains++;
                continue;
            }

            var ring = new List<(double X, double Z)>();
            var previous = -1;
            var current = start;

            do
            {
                ring.Add(nodePositions[current]);
                var next = adjacency[current][0] != previous ? adjacency[current][0] : adjacency[current][1];
                previous = current;
                current = next;
            }
            while (current != start && ring.Count <= component.Count);

            var ringTriangles = EarClip(ring);

            if (ringTriangles == null)
            {
                openChains++;
                continue;
            }

            foreach (var (ia, ib, ic) in ringTriangles)
            {
                var a = ring[ia];
                var b = ring[ib];
                var c = ring[ic];
                var pa = (a.X, yClip, a.Z);
                var pb = (b.X, yClip, b.Z);
                var pc = (c.X, yClip, c.Z);

                // The Y component of (b - a) x (c - a); a negative value means the winding faces -Y.
                var normalY = ((b.Z - a.Z) * (c.X - a.X)) - ((b.X - a.X) * (c.Z - a.Z));

                if (normalY < 0d)
                {
                    (pb, pc) = (pc, pb);
                }

                area += 0.5d * Math.Abs(((b.X - a.X) * (c.Z - a.Z)) - ((b.Z - a.Z) * (c.X - a.X)));
                triangles.Add(new GeneratedBridgeCapTriangle(loops, pa, pb, pc));
            }

            loops++;
        }

        return new GeneratedBridgeCaps
        {
            Loops = loops,
            OpenChains = openChains,
            NonManifoldNodes = nonManifoldNodes,
            Area = area,
            Triangles = triangles,
        };
    }

    private static List<(int, int, int)>? EarClip(IReadOnlyList<(double X, double Z)> ring)
    {
        var order = Enumerable.Range(0, ring.Count).ToList();
        var signed = 0d;

        for (var k = 0; k < ring.Count; k++)
        {
            var p = ring[k];
            var q = ring[(k + 1) % ring.Count];
            signed += (p.X * q.Z) - (q.X * p.Z);
        }

        if (signed < 0d)
        {
            order.Reverse();
        }

        var triangles = new List<(int, int, int)>();

        while (order.Count > 3)
        {
            var found = false;

            for (var k = 0; k < order.Count; k++)
            {
                var a = order[(k + order.Count - 1) % order.Count];
                var b = order[k];
                var c = order[(k + 1) % order.Count];
                var pa = ring[a];
                var pb = ring[b];
                var pc = ring[c];
                var cross = ((pb.X - pa.X) * (pc.Z - pa.Z)) - ((pb.Z - pa.Z) * (pc.X - pa.X));

                if (cross <= 1e-12d)
                {
                    continue;
                }

                var blocked = false;

                foreach (var other in order)
                {
                    if (other == a || other == b || other == c)
                    {
                        continue;
                    }

                    var p = ring[other];

                    if (PointInTriangle2D(p, pa, pb, pc))
                    {
                        blocked = true;
                        break;
                    }
                }

                if (blocked)
                {
                    continue;
                }

                triangles.Add((a, b, c));
                order.RemoveAt(k);
                found = true;
                break;
            }

            if (!found)
            {
                return null;
            }
        }

        triangles.Add((order[0], order[1], order[2]));
        return triangles;
    }

    private static bool PointInTriangle2D((double X, double Z) p, (double X, double Z) a, (double X, double Z) b, (double X, double Z) c)
    {
        double Side((double X, double Z) u, (double X, double Z) v) => ((v.X - u.X) * (p.Z - u.Z)) - ((v.Z - u.Z) * (p.X - u.X));
        var s1 = Side(a, b);
        var s2 = Side(b, c);
        var s3 = Side(c, a);
        return s1 >= 0d && s2 >= 0d && s3 >= 0d;
    }
}
