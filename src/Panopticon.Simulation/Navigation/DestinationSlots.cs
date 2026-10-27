using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-N9-01: spreads a selection's shared move order into per-unit goal slots packed
/// outward from the order point, so a crowd goal stops competing for one cell. Slots are
/// passable cells in the destination's region; candidate order is ring distance, then euclidean
/// distance, then row, and units are assigned in ObjectId order, both deterministic. Units
/// without a slot inside the bound, and aircraft, keep the shared destination.
/// </summary>
internal static class DestinationSlots
{
    /// <summary>Returns one goal per unit, aligned with the <paramref name="units"/> order.</summary>
    internal static Vector3[] Assign(
        Navigation navigation,
        Vector3 destination,
        IReadOnlyList<GameObject> units,
        int maxCells)
    {
        var slots = new Vector3[units.Count];
        Array.Fill(slots, destination);

        var destinationNode = navigation.GetNodeAt(destination);
        if (destinationNode == null || maxCells <= 0)
        {
            return slots;
        }

        var cellSize = CellSize(navigation);
        var maxRadius = units.Count > 0 ? units.Max(unit => unit.Geometry.BoundingCircleRadius) : 0.0f;
        // Units pack roughly at diameter spacing; one padding cell absorbs radius noise.
        var ringStep = Math.Max(1, (int)MathF.Ceiling((2.0f * maxRadius + 10.0f) / cellSize));

        var candidates = new List<(float DistanceSq, int X, int Y, Vector3 World)>();
        for (var ring = 0; ring <= maxCells; ring += ringStep)
        {
            var ringCandidates = new List<(float DistanceSq, int X, int Y, Vector3 World)>();
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    if (!navigation.Graph.TryGetNode(destinationNode.X + dx, destinationNode.Y + dy, out var node)
                        || !node.IsPassable
                        || !navigation.Graph.Regions.SameRegion(node, destinationNode))
                    {
                        continue;
                    }

                    var world = navigation.GetNodeWorldPosition(node);
                    var delta = world - destination;
                    ringCandidates.Add((delta.X * delta.X + delta.Y * delta.Y, node.X, node.Y, world));
                }
            }

            ringCandidates.Sort(static (a, b) => a.DistanceSq != b.DistanceSq
                ? a.DistanceSq.CompareTo(b.DistanceSq)
                : a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            candidates.AddRange(ringCandidates);
        }

        var next = 0;
        foreach (var entry in units
                     .Select((unit, index) => (Unit: unit, Index: index))
                     .Where(entry => entry.Unit.Definition.KindOf?.Get(ObjectKinds.Aircraft) != true)
                     .OrderBy(entry => entry.Unit.Id.Index))
        {
            if (next < candidates.Count)
            {
                slots[entry.Index] = candidates[next].World;
                next++;
            }
        }

        return slots;
    }

    private static float CellSize(Navigation navigation)
    {
        if (navigation.Graph.TryGetNode(0, 0, out var origin)
            && navigation.Graph.TryGetNode(1, 0, out var east))
        {
            var size = navigation.GetNodeWorldPosition(east).X - navigation.GetNodeWorldPosition(origin).X;
            if (size > 0.0f)
            {
                return size;
            }
        }

        return 10.0f;
    }
}
