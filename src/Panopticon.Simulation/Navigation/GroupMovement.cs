#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Navigation;

/// <summary>
/// Card WI-B4-04: squad-level movement over <see cref="Navigation"/>. One leader path is computed
/// per squad and followers retrace it at their formation offsets (position relative to the leader
/// when the order was issued), so path requests scale with squad count rather than unit count.
/// A follower whose offset destination is impassable falls back to its own path request.
/// WI-N11-01: with <see cref="NavigationOptions.ValidateOffsetRoutes"/> the driven legs of every
/// assignment (offset retrace, landing tail, reroute first leg) must stay on passable cells;
/// unclear assignments reroute onto the leader route or pay for their own search.
/// </summary>
public static class GroupMovement
{
    /// <summary>WI-M4-04 diagnostics: total followers whose formation landing was impassable,
    /// the sum of <see cref="LandedFollowerCount"/> and <see cref="FallbackFollowerCount"/>.
    /// Read as a delta alongside <see cref="Navigation.PathRequestCount"/>.</summary>
    public static long BlockedFollowerCount => LandedFollowerCount + FallbackFollowerCount;

    /// <summary>WI-N2-01 diagnostics: blocked followers served by the bounded ring landing, no
    /// search issued.</summary>
    public static long LandedFollowerCount { get; private set; }

    /// <summary>WI-N2-01 diagnostics: blocked followers whose landing failed (other region or
    /// ring bound) and paid for their own path request.</summary>
    public static long FallbackFollowerCount { get; private set; }

    /// <summary>
    /// Orders every member toward <paramref name="destination"/>, issuing exactly one
    /// <see cref="Navigation.CalculatePath"/> per squad plus one per blocked follower. With
    /// <see cref="NavigationOptions.AsyncOrderPaths"/> the leader path (and blocked-follower
    /// fallbacks) run on the async worker queue and assignment happens in the drain callback.
    /// Aircraft members fly directly (no path request, mirroring single-unit orders). Members
    /// without an AI update module are skipped.
    /// </summary>
    public static void MoveSquad(Navigation navigation, IReadOnlyList<GameObject> members, Vector3 destination)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(members);

        if (members.Count == 0)
        {
            return;
        }

        // Partition members into aircraft (direct orders) and ground squad candidates.
        var ground = new List<GameObject>();
        foreach (var member in members)
        {
            if (member.AIUpdate == null)
            {
                continue;
            }

            if (member.Definition.KindOf?.Get(ObjectKinds.Aircraft) == true)
            {
                member.AIUpdate.AddTargetPoint(destination);
                continue;
            }

            ground.Add(member);
        }

        if (ground.Count == 0)
        {
            return;
        }

        // Deterministic leader: nearest to the destination in the ground plane, ties broken by
        // object id. The leader's path is the squad's single shared request.
        var leader = ground[0];
        var leaderDistance = DistanceSquared2D(leader, destination);
        foreach (var member in ground)
        {
            var distance = DistanceSquared2D(member, destination);
            if (distance < leaderDistance || (distance == leaderDistance && member.Id.Index < leader.Id.Index))
            {
                leader = member;
                leaderDistance = distance;
            }
        }

        if (navigation.Options.AsyncOrderPaths)
        {
            // WI-N4-02: the leader search runs on the worker queue; leader and followers are
            // assigned in the drain callback on the logic thread. Members hold position until
            // then. A destroyed leader cancels the request in the drain without a callback.
            navigation.SubmitPathRequest(
                leader.Translation,
                destination,
                leader,
                handle =>
                {
                    if (handle.TryGetResult(out var asyncPath, out var asyncEndIsPassable)
                        && asyncPath.Count > 0)
                    {
                        AssignSquadMembers(
                            navigation, ground, leader, asyncPath, asyncEndIsPassable, destination, asyncFallbacks: true);
                    }
                    else
                    {
                        // WI-N7-01: a failed or empty leader completion settles every member.
                        SettleSquadNoRoute(ground);
                    }
                });
            return;
        }

        var path = navigation.CalculatePath(leader.Translation, destination, out var endIsPassable, PathRequestOrigin.SquadLeader);
        if (path.Count == 0)
        {
            // WI-N7-01: the leader search produced no route; nobody holds a stale route.
            SettleSquadNoRoute(ground);
            return;
        }

        AssignSquadMembers(navigation, ground, leader, path, endIsPassable, destination, asyncFallbacks: false);
    }

    private static void AssignSquadMembers(
        Navigation navigation,
        List<GameObject> ground,
        GameObject leader,
        IList<Vector3> path,
        bool endIsPassable,
        Vector3 destination,
        bool asyncFallbacks)
    {
        foreach (var member in ground)
        {
            if (member.IsDestroyed)
            {
                continue;
            }

            if (member == leader)
            {
                AssignPath(member, path, Vector2.Zero, endIsPassable, destination);
                continue;
            }

            // Formation offset: where this member sits relative to the leader today, flattened to
            // the ground plane.
            var delta = member.Translation - leader.Translation;
            var offset = new Vector2(delta.X, delta.Y);
            var offsetDestination = destination + new Vector3(offset.X, offset.Y, 0);
            var offsetDestinationPassable = navigation.IsPassable(offsetDestination);

            // WI-N7-02 + WI-N11-01: the offset retrace is validated whether or not the slot is
            // passable. The blocked-slot landing below keeps the offset waypoints, so an offset
            // waypoint on an impassable cell must take the same reroute; Crosses also flags the
            // offset destination itself (route[^1] + offset). The driven legs are checked
            // separately because the landing tail replacement and the reroute's first leg from
            // the member's position are not part of the walked route.
            var validationOn = navigation.Options.ValidateOffsetRoutes;
            var crossesOffsetRoute = validationOn
                && OffsetRouteValidator.Crosses(navigation, path, offset);
            var offsetRouteUsable = !crossesOffsetRoute
                && (!validationOn
                    || AssignedLegsAreClear(navigation, member.Translation, path, offset, null, endIsPassable, destination));

            if (offsetRouteUsable && offsetDestinationPassable)
            {
                AssignPath(member, path, offset, endIsPassable, destination);
                continue;
            }

            if (offsetRouteUsable)
            {
                // Blocked follower (WI-B4-04): the formation slot is impassable. WI-N2-01: a
                // follower starting in the leader's region first tries a bounded ring landing
                // in the destination's region; only failed landings and other-region followers
                // pay for their own full search. WI-N11-01: this branch only runs with
                // validation off; an impassable slot always fails Crosses (route[^1] + offset
                // is the slot itself), so validation on reroutes below instead.
                var destinationNode = navigation.GetNodeAt(path[^1]);
                var leaderStartNode = navigation.GetNodeAt(path[0]);
                var followerStartNode = navigation.GetNodeAt(member.Translation);
                if (leaderStartNode != null
                    && followerStartNode != null
                    && destinationNode != null
                    && navigation.Graph.Regions.SameRegion(followerStartNode, leaderStartNode)
                    && FollowerLanding.TryLand(
                        navigation,
                        offsetDestination,
                        destinationNode,
                        navigation.Options.MaxLandingRadiusCells,
                        out var blockedLandingPosition,
                        out var blockedRingDistance))
                {
                    LandedFollowerCount++;
                    navigation.AddLandedFollower();
                    FollowerLanding.RecordLanding(blockedRingDistance);
                    AssignPath(member, path, offset, endIsPassable, destination, blockedLandingPosition);
                    continue;
                }

                FallbackFollowerCount++;
                navigation.AddFallbackFollower();
                IssueFallbackSearch(navigation, member, destination, asyncFallbacks);
                continue;
            }

            // Crossing retrace (WI-N7-02) or unclear driven legs (WI-N11-01): land on the
            // leader route near the destination, or fall back to an own search when no
            // landing exists or its first leg from this member's position is blocked.
            navigation.AddReroutedFollower();
            var crossingDestinationNode = navigation.GetNodeAt(path[^1]);

            // A zero landing radius disables landing here too (WI-N2-01 semantics), even
            // though the destination itself is passable at ring 0.
            if (navigation.Options.MaxLandingRadiusCells > 0
                && crossingDestinationNode != null
                && FollowerLanding.TryLand(
                    navigation,
                    destination,
                    crossingDestinationNode,
                    navigation.Options.MaxLandingRadiusCells,
                    out var landingPosition,
                    out var ringDistance)
                && AssignedLegsAreClear(
                    navigation, member.Translation, path, Vector2.Zero, landingPosition, false, destination))
            {
                LandedFollowerCount++;
                navigation.AddLandedFollower();
                FollowerLanding.RecordLanding(ringDistance);
                AssignPath(member, path, Vector2.Zero, endIsPassable, destination, landingPosition);
                continue;
            }

            // Failed landing: pay for an own search like a blocked follower.
            FallbackFollowerCount++;
            navigation.AddFallbackFollower();
            IssueFallbackSearch(navigation, member, destination, asyncFallbacks);
        }
    }

    /// <summary>
    /// WI-N11-01: walks the legs the member will actually drive for an assignment built like
    /// <see cref="AssignPath"/> (offset waypoints, optional landing or destination tail
    /// replacement). Returns false when any leg leaves the graph, crosses an impassable cell,
    /// or ends on one. The member's own cell is exempt: the start node resolves from its
    /// position, which may sit on an impassable cell.
    /// </summary>
    private static bool AssignedLegsAreClear(
        Navigation navigation,
        Vector3 memberPosition,
        IList<Vector3> path,
        Vector2 offset,
        Vector3? landing,
        bool endIsPassable,
        Vector3 destination)
    {
        if (path.Count < 2)
        {
            return true;
        }

        var from = new Vector2(memberPosition.X, memberPosition.Y);
        for (var i = 1; i < path.Count; i++)
        {
            Vector3 to;
            if (i == path.Count - 1 && (landing.HasValue || endIsPassable))
            {
                to = landing ?? destination + new Vector3(offset.X, offset.Y, 0);
            }
            else
            {
                var point = path[i];
                to = new Vector3(point.X + offset.X, point.Y + offset.Y, point.Z);
            }

            if (!PathOptimizer.IsDirectLinePassable(navigation, from, new Vector2(to.X, to.Y)))
            {
                return false;
            }

            from = new Vector2(to.X, to.Y);
        }

        return true;
    }

    private static void IssueFallbackSearch(
        Navigation navigation,
        GameObject member,
        Vector3 destination,
        bool asyncFallbacks)
    {
        // WI-N4-02: with async order paths the fallback search also runs on the worker queue.
        // WI-N7-01: a failed or empty fallback settles the follower.
        if (asyncFallbacks)
        {
            navigation.SubmitPathRequest(
                member.Translation,
                destination,
                member,
                fallbackHandle =>
                {
                    if (fallbackHandle.TryGetResult(out var fallbackPath, out var fallbackEndIsPassable)
                        && fallbackPath.Count > 0)
                    {
                        AssignPath(member, fallbackPath, Vector2.Zero, fallbackEndIsPassable, destination);
                    }
                    else if (!member.IsDestroyed)
                    {
                        member.AIUpdate!.SettleNoRoute();
                    }
                });
            return;
        }

        var fallback = navigation.CalculatePath(member.Translation, destination, out var syncFallbackEndIsPassable, PathRequestOrigin.BlockedFollower);
        if (fallback.Count > 0)
        {
            AssignPath(member, fallback, Vector2.Zero, syncFallbackEndIsPassable, destination);
        }
        else
        {
            member.AIUpdate!.SettleNoRoute();
        }
    }

    private static void SettleSquadNoRoute(List<GameObject> ground)
    {
        // WI-N7-01: a failed or empty leader result settles every member as NoRoute.
        foreach (var member in ground)
        {
            if (!member.IsDestroyed && member.AIUpdate != null)
            {
                member.AIUpdate.SettleNoRoute();
            }
        }
    }

    private static void AssignPath(GameObject member, IList<Vector3> path, Vector2 offset, bool endIsPassable, Vector3 destination, Vector3? landing = null)
    {
        var ai = member.AIUpdate!;
        ai.TargetPoints.Clear();

        // Mirror single-unit path following: skip the start point, walk the rest at this member's
        // offset, and land exactly on the (offset) destination when it is passable. Landed
        // followers (WI-N2-01) always override the tail: their landing cell is passable by
        // construction.
        for (var i = 1; i < path.Count; i++)
        {
            var point = path[i];
            ai.TargetPoints.Add(new Vector3(point.X + offset.X, point.Y + offset.Y, point.Z));
        }

        if (ai.TargetPoints.Count > 0 && (landing.HasValue || endIsPassable))
        {
            ai.TargetPoints[^1] = landing ?? destination + new Vector3(offset.X, offset.Y, 0);
        }

        if (ai.TargetPoints.Count > 0)
        {
            member.SetModelConditionState(ModelConditionFlag.Moving);
        }
    }

    private static float DistanceSquared2D(GameObject member, Vector3 destination)
    {
        var delta = member.Translation - destination;
        return delta.X * delta.X + delta.Y * delta.Y;
    }
}
