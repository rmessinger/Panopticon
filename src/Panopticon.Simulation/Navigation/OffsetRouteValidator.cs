#nullable enable

using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-N7-02: detects formation-offset retrace routes that cross impassable cells even
/// though the offset destination itself is passable. Segment walks use the same Bresenham line
/// rule as <see cref="PathOptimizer"/> smoothing, which mirrors the search's diagonal
/// movement.
/// </summary>
internal static class OffsetRouteValidator
{
    /// <summary>
    /// Returns true when the leader route shifted to <paramref name="offset"/> leaves the
    /// graph or crosses an impassable cell.
    /// </summary>
    internal static bool Crosses(Navigation navigation, IList<Vector3> route, Vector2 offset)
    {
        Node? previous = null;
        for (var i = 0; i < route.Count; i++)
        {
            var point = route[i];
            var node = navigation.GetNodeAt(new Vector3(point.X + offset.X, point.Y + offset.Y, point.Z));
            // Off-graph or impassable waypoints both invalidate the retrace: the segment walk
            // below skips segment endpoints, so an offset waypoint landing on a wall cell is
            // only caught here.
            if (node == null || !node.IsPassable)
            {
                return true;
            }

            if (previous != null && !PathOptimizer.IsDirectLinePassable(previous, node, navigation.Graph))
            {
                return true;
            }

            previous = node;
        }

        return false;
    }
}
