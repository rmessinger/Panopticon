using System;
using System.Numerics;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-N8-02: finds the sidestep cell for a blocker asked to give way: the nearest free
/// passable cell off the requester's next segment, inside the local window, same region.
/// </summary>
internal static class GiveWayPlanner
{
    /// <summary>
    /// Returns the world position of the nearest free passable cell within
    /// <paramref name="windowCells"/> of <paramref name="blocker"/> that clears the requester's
    /// next segment (<paramref name="segmentStart"/> to <paramref name="segmentEnd"/>) by the
    /// combined footprint radii plus padding, or null when no such cell exists. Ring order is
    /// row-major like <see cref="FollowerLanding.TryLand"/> so the choice is deterministic.
    /// </summary>
    internal static Vector3? FindSidestep(
        GameObject blocker,
        GameObject requester,
        Navigation navigation,
        Vector2 segmentStart,
        Vector2 segmentEnd,
        int windowCells)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(windowCells);

        var blockerNode = navigation.GetNodeAt(blocker.Translation);
        if (blockerNode == null)
        {
            return null;
        }

        var occupancy = LocalOccupancyWindow.Collect(
            blocker,
            blocker.Translation.X - windowCells * 10,
            blocker.Translation.Y - windowCells * 10,
            blocker.Translation.X + windowCells * 10,
            blocker.Translation.Y + windowCells * 10);

        var clearance = requester.Geometry.BoundingCircleRadius + blocker.Geometry.BoundingCircleRadius
            + LocalOccupancyWindow.FootprintPadding;
        // One extra cell beyond the strict contact clearance: a yield that only just clears
        // lets the requester re-block on arrival, so the pair dances without separating.
        clearance += 10.0f;

        for (var ring = 1; ring <= windowCells; ring++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    var x = blockerNode.X + dx;
                    var y = blockerNode.Y + dy;
                    var node = navigation.Graph.GetNode(x, y);
                    if (node == null || !node.IsPassable
                        || !navigation.Graph.Regions.SameRegion(node, blockerNode))
                    {
                        continue;
                    }

                    var position = navigation.GetNodeWorldPosition(node);
                    if (DistanceToSegment(position.Vector2XY(), segmentStart, segmentEnd) < clearance)
                    {
                        continue;
                    }

                    if (!occupancy.IsFreeAt(position, blocker.Geometry.BoundingCircleRadius) || !ClearsRequester(position, requester, blocker))
                    {
                        continue;
                    }

                    return position;
                }
            }
        }

        return null;
    }

    private static bool ClearsRequester(Vector3 position, GameObject requester, GameObject blocker)
    {
        var clearance = requester.Geometry.BoundingCircleRadius + blocker.Geometry.BoundingCircleRadius
            + LocalOccupancyWindow.FootprintPadding + 10.0f;
        return Vector3.DistanceSquared(position, requester.Translation) > clearance * clearance;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared < 0.0001f)
        {
            return Vector2.Distance(point, start);
        }

        var t = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0.0f, 1.0f);
        return Vector2.Distance(point, start + segment * t);
    }
}
