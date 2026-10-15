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
/// </summary>
public static class GroupMovement
{
    /// <summary>
    /// Orders every member toward <paramref name="destination"/>, issuing exactly one
    /// <see cref="Navigation.CalculatePath"/> per squad plus one per blocked follower. Aircraft
    /// members fly directly (no path request, mirroring single-unit orders). Members without an
    /// AI update module are skipped.
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

        var path = navigation.CalculatePath(leader.Translation, destination, out var endIsPassable);
        if (path.Count == 0)
        {
            return;
        }

        foreach (var member in ground)
        {
            var ai = member.AIUpdate;
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

            if (navigation.IsPassable(offsetDestination))
            {
                AssignPath(member, path, offset, endIsPassable, destination);
            }
            else
            {
                // Blocked follower: retrace is impossible, so this member pays for its own path.
                var fallback = navigation.CalculatePath(member.Translation, destination, out var fallbackEndIsPassable);
                if (fallback.Count > 0)
                {
                    AssignPath(member, fallback, Vector2.Zero, fallbackEndIsPassable, destination);
                }
            }
        }
    }

    private static void AssignPath(GameObject member, IList<Vector3> path, Vector2 offset, bool endIsPassable, Vector3 destination)
    {
        var ai = member.AIUpdate!;
        ai.TargetPoints.Clear();

        // Mirror single-unit path following: skip the start point, walk the rest at this member's
        // offset, and land exactly on the (offset) destination when it is passable.
        for (var i = 1; i < path.Count; i++)
        {
            var point = path[i];
            ai.TargetPoints.Add(new Vector3(point.X + offset.X, point.Y + offset.Y, point.Z));
        }

        if (ai.TargetPoints.Count > 0 && endIsPassable)
        {
            ai.TargetPoints[^1] = destination + new Vector3(offset.X, offset.Y, 0);
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
