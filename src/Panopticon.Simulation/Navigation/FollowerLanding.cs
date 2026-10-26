using System;
using System.Numerics;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-N2-01: bounded ring search that lands a blocked follower on the nearest passable
/// cell to its offset destination inside the destination's connected region, so the follower
/// retraces the leader's path instead of paying for a full search. Ties inside a ring resolve
/// by fixed row-major ring order, keeping landings deterministic.
/// </summary>
public static class FollowerLanding
{
    // Landing-ring distribution (WI-N2-01): the histogram behind the MaxLandingRadiusCells
    // default. Logic-thread writes only; read by reporting scenarios between runs.
    private const int MaxTrackedRing = 31;
    private static readonly long[] RingHistogram = new long[MaxTrackedRing + 1];

    /// <summary>Tries to land a blocked follower near <paramref name="offsetDestination"/>.
    /// Returns the first passable cell at or within <paramref name="maxRadiusCells"/> Chebyshev
    /// rings that shares <paramref name="destinationNode"/>'s connected region, scanning rings
    /// outward in row-major order.</summary>
    public static bool TryLand(
        Navigation navigation,
        Vector3 offsetDestination,
        Node destinationNode,
        int maxRadiusCells,
        out Vector3 landingPosition,
        out int ringDistance)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(destinationNode);

        landingPosition = default;
        ringDistance = 0;

        ArgumentOutOfRangeException.ThrowIfNegative(maxRadiusCells);

        var graph = navigation.Graph;
        var center = navigation.GetNodeAt(offsetDestination);
        if (center == null)
        {
            // Off-graph offsets (formation slots past the map edge) have no cell to ring from.
            return false;
        }

        for (var ring = 0; ring <= maxRadiusCells; ring++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    if (!graph.TryGetNode(center.X + dx, center.Y + dy, out var candidate))
                    {
                        continue;
                    }

                    if (!graph.Regions.SameRegion(candidate, destinationNode))
                    {
                        continue;
                    }

                    landingPosition = navigation.GetNodeWorldPosition(candidate);
                    ringDistance = ring;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Records one successful landing at <paramref name="ring"/> rings from the blocked
    /// offset destination (WI-N2-01 distribution measurement; rings above the tracked maximum
    /// clamp to it).</summary>
    public static void RecordLanding(int ring)
    {
        RingHistogram[Math.Clamp(ring, 0, MaxTrackedRing)]++;
    }

    /// <summary>Cumulative landing counts per ring distance since the last reset.</summary>
    public static long[] LandingRingHistogram => RingHistogram;

    /// <summary>Zeroes the landing-ring histogram; used by scenarios that attribute the
    /// histogram to a specific case window.</summary>
    public static void ResetLandingRings() => Array.Clear(RingHistogram);
}
