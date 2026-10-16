#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Panopticon.Navigation;
using NavigationGrid = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-M4-03: samples the squad invariant of both army-AI sides after every engagement frame, a
/// superset of the per-decision-tick requirement (membership only changes on decision ticks or
/// deaths, and both settle within the frame). Sampling starts after each side's first decision
/// tick, so the pre-AI unassigned state is not counted; the invariant is that from the first
/// Synchronize on, no eligible ground unit is unassigned and no non-empty squad lacks a live
/// commander.
/// </summary>
internal sealed class MassBattleSquadInvariantSampler
{
    private sealed class Side
    {
        public Side(string id, Player player, AIPlayer ai, SquadHierarchy hierarchy)
        {
            Id = id;
            Player = player;
            Ai = ai;
            Hierarchy = hierarchy;
        }

        public string Id { get; }

        public Player Player { get; }

        public AIPlayer Ai { get; }

        public SquadHierarchy Hierarchy { get; }

        public int GroundSquads { get; set; }

        public int GroundMembers { get; set; }

        public int CurrentUnassigned { get; set; }

        public int MaxUnassigned { get; set; }

        public int CurrentCommanderless { get; set; }

        public int MaxCommanderless { get; set; }

        public Dictionary<uint, (Vector3 LastPosition, int IdleFrames)> IdleTracking { get; } = new();

        public int MaxIdleFrames { get; set; }

        public int StalledUnitsAtEnd { get; set; }
    }

    private readonly List<Side> _sides;

    // WI-M4-04: path requests are counted over the frame after each decision tick, because the
    // policies dispatch through NetworkMessageBuffer.AddLocalOrder during the decision frame and
    // the buffer processes those orders at the start of the next frame. Blocked followers pay
    // their own request (WI-B4-04), so the per-tick bound is squads plus blocked followers.
    private long _lastPathRequestCount;
    private long _lastBlockedFollowerCount;
    private int _lastDecisionTicks;
    private bool _burstWindowOpen;
    private int _burstSquads;
    private int _burstMembers;

    private MassBattleSquadInvariantSampler(List<Side> sides)
    {
        _sides = sides;
    }

    public static MassBattleSquadInvariantSampler Create(MassBattleDeploymentProfile profile, Game game)
    {
        var sides = new List<Side>();
        for (var i = 0; i < profile.Sides.Count; i++)
        {
            var side = profile.Sides[i];
            var player = game.Scene3D.Players[2 + i];
            var ai = player.AIPlayer
                ?? throw new InvalidOperationException(
                    $"Army-AI mode: side player '{side.Id}' has no AI player.");
            var hierarchy = (ai.DecisionPolicy as HierarchicalAIDecisionPolicy)?.Hierarchy
                ?? throw new InvalidOperationException(
                    $"Army-AI mode: side player '{side.Id}' does not run the hierarchical policy.");
            sides.Add(new Side(side.Id, player, ai, hierarchy));
        }

        return new MassBattleSquadInvariantSampler(sides);
    }

    public int DecisionTicksObserved { get; private set; }

    public long MaxPathRequestsPerDecisionTick { get; private set; }

    public int SquadsAtWorstDecisionTick { get; private set; }

    public int MembersAtWorstDecisionTick { get; private set; }

    public long MaxBlockedFollowersPerDecisionTick { get; private set; }

    public void Sample()
    {
        foreach (var side in _sides)
        {
            if (side.Ai.DecisionTickCount == 0)
            {
                continue;
            }

            var unassigned = 0;
            foreach (var unit in SquadHierarchy.EligibleUnits(side.Player))
            {
                if (!unit.IsEffectivelyDead && side.Hierarchy.SquadOf(unit) is null)
                {
                    unassigned++;
                }
            }

            var members = 0;
            var commanderless = 0;
            foreach (var squad in side.Hierarchy.Squads)
            {
                members += squad.Members.Count;
                if (squad.Members.Count > 0
                    && (squad.CommanderId is null || squad.Members.All(member => member.Id != squad.CommanderId)))
                {
                    commanderless++;
                }
            }

            side.GroundSquads = side.Hierarchy.Squads.Count;
            side.GroundMembers = members;
            side.CurrentUnassigned = unassigned;
            side.CurrentCommanderless = commanderless;
            side.MaxUnassigned = Math.Max(side.MaxUnassigned, unassigned);
            side.MaxCommanderless = Math.Max(side.MaxCommanderless, commanderless);

            SampleStalls(side);
        }

        SamplePathRequests();
    }

    // WI-M4-06: a live combat unit is idle when it holds no target point, its weapon has no
    // valid target, and it has not moved since the last sample, while enemies remain. Parked
    // jets are excluded: parking between sorties is intentional idle time (WI-M4-05). Idleness
    // longer than the authored threshold is a stall.
    private const int StallThresholdFrames = 600;

    private static void SampleStalls(Side side)
    {
        if (!EnemiesRemain(side.Player))
        {
            side.IdleTracking.Clear();
            return;
        }

        foreach (var unit in side.Player.OwnedObjects)
        {
            if (unit.IsEffectivelyDead || unit.IsStructure || !unit.CanAttack)
            {
                continue;
            }

            if (unit.AIUpdate is JetAIUpdate jet && jet.CurrentJetAIState == JetAIUpdate.JetAIState.Parked)
            {
                continue;
            }

            var position = unit.Transform.Translation;
            var movedOrNew = !side.IdleTracking.TryGetValue(unit.Id.Index, out var tracked)
                || tracked.LastPosition != position;
            if (movedOrNew)
            {
                side.IdleTracking[unit.Id.Index] = (position, 0);
                continue;
            }

            var engaged = unit.AIUpdate?.TargetPoints.Count > 0 || unit.CurrentWeapon?.HasValidTarget == true;
            var idleFrames = engaged ? 0 : tracked.IdleFrames + 1;
            side.IdleTracking[unit.Id.Index] = (position, idleFrames);
            side.MaxIdleFrames = Math.Max(side.MaxIdleFrames, idleFrames);
        }
    }

    private static bool EnemiesRemain(Player player)
    {
        foreach (var enemy in player.Enemies)
        {
            foreach (var unit in enemy.OwnedObjects)
            {
                if (!unit.IsEffectivelyDead && !unit.IsStructure && unit.CanAttack)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void SamplePathRequests()
    {
        var pathRequestCount = NavigationGrid.PathRequestCount;
        var blockedFollowerCount = GroupMovement.BlockedFollowerCount;

        var decisionTicks = 0;
        foreach (var side in _sides)
        {
            decisionTicks = Math.Max(decisionTicks, side.Ai.DecisionTickCount);
        }

        if (decisionTicks > _lastDecisionTicks)
        {
            // A decision tick fired this frame; its orders reach the OrderProcessor next frame.
            _burstWindowOpen = true;
            _burstSquads = _sides.Sum(side => side.GroundSquads);
            _burstMembers = _sides.Sum(side => side.GroundMembers);
        }
        else if (_burstWindowOpen)
        {
            var burst = pathRequestCount - _lastPathRequestCount;
            DecisionTicksObserved++;
            if (burst > MaxPathRequestsPerDecisionTick)
            {
                MaxPathRequestsPerDecisionTick = burst;
                SquadsAtWorstDecisionTick = _burstSquads;
                MembersAtWorstDecisionTick = _burstMembers;
            }

            MaxBlockedFollowersPerDecisionTick = Math.Max(
                MaxBlockedFollowersPerDecisionTick,
                blockedFollowerCount - _lastBlockedFollowerCount);

            _burstWindowOpen = false;
        }

        _lastDecisionTicks = decisionTicks;
        _lastPathRequestCount = pathRequestCount;
        _lastBlockedFollowerCount = blockedFollowerCount;
    }

    public MassBattleSquadInvariantReport Build()
    {
        foreach (var side in _sides)
        {
            side.StalledUnitsAtEnd = side.IdleTracking.Values.Count(t => t.IdleFrames > StallThresholdFrames);
        }

        return new MassBattleSquadInvariantReport
        {
            Sides = _sides.Select(side => new SquadInvariantSideResult
            {
                SideId = side.Id,
                GroundSquads = side.GroundSquads,
                GroundMembers = side.GroundMembers,
                UnassignedEligibleGround = side.CurrentUnassigned,
                MaxUnassignedEligibleGround = side.MaxUnassigned,
                CommanderlessSquads = side.CurrentCommanderless,
                MaxCommanderlessSquads = side.MaxCommanderless,
                MaxIdleFrames = side.MaxIdleFrames,
                StalledUnitsAtEnd = side.StalledUnitsAtEnd,
            }).ToList(),
            DecisionTicksObserved = DecisionTicksObserved,
            MaxPathRequestsPerDecisionTick = MaxPathRequestsPerDecisionTick,
            SquadsAtWorstDecisionTick = SquadsAtWorstDecisionTick,
            MembersAtWorstDecisionTick = MembersAtWorstDecisionTick,
            MaxBlockedFollowersPerDecisionTick = MaxBlockedFollowersPerDecisionTick,
            StallThresholdFrames = StallThresholdFrames,
        };
    }
}
