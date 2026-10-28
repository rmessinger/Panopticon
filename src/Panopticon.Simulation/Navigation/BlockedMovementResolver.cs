using System;
using System;
using System.Numerics;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-N8-01/02: resolves a ground unit blocked by a non-progressing unit past
/// <see cref="NavigationOptions.BlockedWaitFrames"/>. Steps: wait behind a progressing blocker,
/// give-way (idle or arrived friendly blockers sidestep, mutual blocks yield by the higher
/// object id, WI-N8-02), one bounded local repath per wait interval, and give up after
/// <see cref="NavigationOptions.MaxResolutionAttempts"/> attempts. Detection comes from
/// <see cref="MovementProgress"/>; nothing here changes movement unless the option is on.
/// </summary>
internal static class BlockedMovementResolver
{
    internal static void TryResolve(GameObject unit, AIUpdate aiUpdate, Navigation navigation)
    {
        var options = navigation.Options;

        var blocker = unit.GameEngine.GameLogic.GetObjectById(aiUpdate.Progress.BlockerId);
        if (blocker == null || blocker.IsDestroyed || LocalOccupancyWindow.IsAdvancing(blocker))
        {
            // A progressing blocker is queueing traffic; wait as today.
            return;
        }

        if (TryGiveWay(unit, aiUpdate, blocker, navigation))
        {
            // Give-way handled this step: a sidestep was accepted or one is already in flight.
            return;
        }

        if (!aiUpdate.TryBeginLocalRepathAttempt(
                aiUpdate.Progress.FramesWithoutProgress,
                options.BlockedWaitFrames,
                options.MaxResolutionAttempts,
                out var gaveUp))
        {
            if (gaveUp && WithinCrowdArrivalRadius(unit, aiUpdate, options.CrowdArrivalRadius))
            {
                aiUpdate.SettleGoalCrowded();
            }

            // Otherwise keep the route and stay reported.
            return;
        }

        if (!TryFindRejoin(navigation, aiUpdate, unit, options.LocalRepathWindowCells, out var rejoin, out var superseded))
        {
            // The attempt is consumed; the unit keeps its route and stays reported.
            return;
        }

        var occupancy = LocalOccupancyWindow.Collect(
            unit,
            Math.Min(unit.Translation.X, rejoin.X),
            Math.Min(unit.Translation.Y, rejoin.Y),
            Math.Max(unit.Translation.X, rejoin.X),
            Math.Max(unit.Translation.Y, rejoin.Y));
        var detour = navigation.CalculateLocalPath(unit.Translation, rejoin, options.LocalRepathWindowCells, occupancy);
        if (detour.Count > 0)
        {
            aiUpdate.SpliceLocalDetour(detour, superseded);
        }
    }

    private static bool WithinCrowdArrivalRadius(GameObject unit, AIUpdate aiUpdate, float crowdArrivalRadius)
    {
        if (aiUpdate.TargetPoints.Count == 0)
        {
            return false;
        }

        var toGoal = aiUpdate.TargetPoints[^1] - unit.Translation;
        var distance = toGoal.Vector2XY().Length();
        return distance <= crowdArrivalRadius;
    }

    /// <summary>
    /// Card WI-N8-02: the give-way step. True means the resolution step is handled (wait);
    /// false falls through to the local repath.
    /// </summary>
    private static bool TryGiveWay(GameObject unit, AIUpdate aiUpdate, GameObject blocker, Navigation navigation)
    {
        var blockerAi = blocker.AIUpdate;

        // Mutual block: each records the other. Exactly one unit yields: the higher object id
        // sidesteps off the lower unit's next segment; the lower waits for that to clear. A
        // yield that cannot be planned falls through to the local repath instead of stalling.
        // Mutual block: each records the other. Exactly one unit yields: the higher object id
        // sidesteps off the lower unit's next segment; the lower waits for that to clear. A
        // yield that cannot be planned, or a wait that outlives a full give-way cycle without
        // the path clearing, falls through to the local repath instead of stalling.
        if (blockerAi != null && blockerAi.Progress.BlockerId == unit.Id)
        {
            // The blocker's sidestep is not in flight and this wait has outlived a full
            // give-way cycle: stop waiting and detour instead.
            var waitExpired = aiUpdate.Progress.FramesWithoutProgress >= 2 * navigation.Options.BlockedWaitFrames
                && !blockerAi.RecentlyGaveWay(navigation.Options.BlockedWaitFrames);
            if (unit.Id.Index < blocker.Id.Index && !waitExpired)
            {
                return true;
            }

            if (aiUpdate.RecentlyGaveWay(navigation.Options.BlockedWaitFrames))
            {
                // A sidestep is already in flight; let it clear the path.
                return true;
            }

            if (unit.Id.Index > blocker.Id.Index
                && blockerAi.TargetPoints.Count > 0
                && aiUpdate.SidestepWhileKeepingRoute(
                    blocker,
                    navigation,
                    blocker.Translation.Vector2XY(),
                    blockerAi.TargetPoints[0].Vector2XY()))
            {
                return true;
            }
        }

        if (blockerAi == null || blocker.Owner != unit.Owner)
        {
            // Enemy blockers and blocker-less objects never give way; local repath instead.
            return false;
        }

        if (blockerAi.RecentlyGaveWay(navigation.Options.BlockedWaitFrames))
        {
            // A sidestep is already in flight; let it clear the path.
            return true;
        }

        if (aiUpdate.TargetPoints.Count == 0)
        {
            return false;
        }

        return blockerAi.RequestGiveWay(
            unit,
            navigation,
            unit.Translation.Vector2XY(),
            aiUpdate.TargetPoints[0].Vector2XY());
    }

    /// <summary>
    /// The rejoin is the first remaining waypoint outside the window, clamped to the window
    /// edge toward it. The detour supersedes every leading waypoint not strictly closer to
    /// the goal than the rejoin point (at least the final goal waypoint survives). A route
    /// that ends inside the window detours straight to its goal.
    /// </summary>
    private static bool TryFindRejoin(
        Navigation navigation,
        AIUpdate aiUpdate,
        GameObject unit,
        int windowCells,
        out Vector3 rejoin,
        out int superseded)
    {
        rejoin = default;
        superseded = 0;
        var unitNode = navigation.GetNodeAt(unit.Translation);
        if (unitNode == null)
        {
            return false;
        }

        var goal = aiUpdate.TargetPoints[^1];
        var maxX = Math.Min(navigation.Graph.Width - 1, unitNode.X + windowCells);
        var maxY = Math.Min(navigation.Graph.Height - 1, unitNode.Y + windowCells);
        for (var i = 0; i < aiUpdate.TargetPoints.Count; i++)
        {
            var node = navigation.GetNodeAt(aiUpdate.TargetPoints[i]);
            if (node == null)
            {
                continue;
            }

            var chebyshev = Math.Max(Math.Abs(node.X - unitNode.X), Math.Abs(node.Y - unitNode.Y));
            if (chebyshev <= windowCells)
            {
                continue;
            }

            var clamped = navigation.Graph.GetNode(
                Math.Clamp(node.X, Math.Max(0, unitNode.X - windowCells), maxX),
                Math.Clamp(node.Y, Math.Max(0, unitNode.Y - windowCells), maxY));
            rejoin = navigation.GetNodeWorldPosition(clamped);
            superseded = CountSupersededWaypoints(aiUpdate, goal, rejoin);
            return true;
        }

        rejoin = goal;
        superseded = aiUpdate.TargetPoints.Count - 1;
        return true;
    }

    private static int CountSupersededWaypoints(AIUpdate aiUpdate, Vector3 goal, Vector3 rejoin)
    {
        var rejoinDistance = (goal - rejoin).Vector2XY().LengthSquared();
        var superseded = 0;
        while (superseded < aiUpdate.TargetPoints.Count - 1
            && (goal - aiUpdate.TargetPoints[superseded]).Vector2XY().LengthSquared() >= rejoinDistance)
        {
            superseded++;
        }

        return superseded;
    }
}
