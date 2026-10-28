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
        public Side(ArmyAiSide side)
        {
            Id = side.SideId;
            Player = side.Player;
            Ai = side.Ai;
            Policy = side.Policy;
        }

        public string Id { get; }

        public Player Player { get; }

        public AIPlayer Ai { get; }

        public HierarchicalAIDecisionPolicy Policy { get; }

        public int GroundSquads { get; set; }

        public int GroundMembers { get; set; }

        public int CurrentUnassigned { get; set; }

        public int MaxUnassigned { get; set; }

        public int CurrentCommanderless { get; set; }

        public int MaxCommanderless { get; set; }

        public Dictionary<uint, (Vector3 LastPosition, int IdleFrames)> IdleTracking { get; } = new();

        public int MaxIdleFrames { get; set; }

        public int StalledUnits { get; set; }
    }

    // WI-N6-01: game-wide stall records and wait-for cycles; blocker edges cross sides.
    private readonly NavigationStallSampler _stallSampler = new();
    private uint _sampleFrame;

    private readonly List<Side> _sides;

    // WI-M4-04: path requests are counted over the frame after each decision tick, because the
    // policies dispatch through NetworkMessageBuffer.AddLocalOrder during the decision frame and
    // the buffer processes those orders at the start of the next frame. Blocked followers pay
    // their own request (WI-B4-04), so the per-tick bound is squads plus blocked followers.
    private long _lastPathRequestCount;
    private long _lastBlockedFollowerCount;
    private long _lastLandedFollowerCount;
    private long _lastFallbackFollowerCount;
    private int _lastDecisionTicks;
    private bool _burstWindowOpen;
    private int _burstSquads;
    private int _burstMembers;

    private MassBattleSquadInvariantSampler(List<Side> sides)
    {
        _sides = sides;
    }

    /// <summary>Creates the sampler over resolved army-AI sides (WI-M5-01).</summary>
    public static MassBattleSquadInvariantSampler Create(IReadOnlyList<ArmyAiSide> sides)
    {
        return new MassBattleSquadInvariantSampler(sides.Select(side => new Side(side)).ToList());
    }

    public int DecisionTicksObserved { get; private set; }

    public long MaxPathRequestsPerDecisionTick { get; private set; }

    public int SquadsAtWorstDecisionTick { get; private set; }

    public int MembersAtWorstDecisionTick { get; private set; }

    public long MaxBlockedFollowersPerDecisionTick { get; private set; }

    // WI-N2-01: the blocked-follower split. Landed followers issued no search; fallback
    // followers paid for their own request.
    public long MaxLandedFollowersPerDecisionTick { get; private set; }

    public long MaxFallbackFollowersPerDecisionTick { get; private set; }

    public void Sample()
    {
        foreach (var side in _sides)
        {
            if (side.Ai.DecisionTickCount == 0)
            {
                continue;
            }

            var stats = side.Policy.GetStats(side.Player);
            side.GroundSquads = stats.GroundSquadCount;
            side.GroundMembers = stats.GroundMemberCount;
            side.CurrentUnassigned = stats.UnassignedEligibleGroundCount;
            side.CurrentCommanderless = stats.CommanderlessSquadCount;
            side.MaxUnassigned = Math.Max(side.MaxUnassigned, stats.UnassignedEligibleGroundCount);
            side.MaxCommanderless = Math.Max(side.MaxCommanderless, stats.CommanderlessSquadCount);

            SampleStalls(side);
        }

        _sampleFrame++;
        _stallSampler.Sample(
            _sampleFrame,
            _sides.SelectMany(side => side.Player.OwnedObjects));

        SamplePathRequests();
    }

    // WI-N6-01: a live combat unit is stalled when it holds target points but has made no
    // progress toward the current waypoint beyond the threshold, or its last order produced
    // no route, while enemies remain. The WI-M4-06 position/route rule counted any unit
    // holding target points as engaged, hiding jams; counts are expected to rise and N6
    // records the new baseline. Parked jets are excluded: parking between sorties is
    // intentional idle time (WI-M4-05).
    private const int StallThresholdFrames = 600;

    private static void SampleStalls(Side side)
    {
        if (!EnemiesRemain(side.Player))
        {
            side.IdleTracking.Clear();
            return;
        }

        var stalledUnits = 0;
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

            var progress = unit.AIUpdate?.Progress;
            if (progress == null)
            {
                stalledUnits += TrackPositionIdle(side, unit);
                continue;
            }

            side.MaxIdleFrames = Math.Max(side.MaxIdleFrames, progress.FramesWithoutProgress);
            var holdingOrder = unit.AIUpdate.TargetPoints.Count > 0;
            if ((holdingOrder && progress.FramesWithoutProgress > StallThresholdFrames)
                || progress.Reason == NavigationStallReason.NoRoute)
            {
                stalledUnits++;
            }
        }

        side.StalledUnits = stalledUnits;
    }

    // Fallback for units without AIUpdate-driven progress (none in the M4 roster): the
    // WI-M4-06 position rule.
    private static int TrackPositionIdle(Side side, GameObject unit)
    {
        var position = unit.Transform.Translation;
        var movedOrNew = !side.IdleTracking.TryGetValue(unit.Id.Index, out var tracked)
            || tracked.LastPosition != position;
        if (movedOrNew)
        {
            side.IdleTracking[unit.Id.Index] = (position, 0);
            return 0;
        }

        var engaged = unit.CurrentWeapon?.HasValidTarget == true;
        var idleFrames = engaged ? 0 : tracked.IdleFrames + 1;
        side.IdleTracking[unit.Id.Index] = (position, idleFrames);
        side.MaxIdleFrames = Math.Max(side.MaxIdleFrames, idleFrames);
        return idleFrames > StallThresholdFrames ? 1 : 0;
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
        var landedFollowerCount = GroupMovement.LandedFollowerCount;
        var fallbackFollowerCount = GroupMovement.FallbackFollowerCount;

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

            MaxLandedFollowersPerDecisionTick = Math.Max(
                MaxLandedFollowersPerDecisionTick,
                landedFollowerCount - _lastLandedFollowerCount);

            MaxFallbackFollowersPerDecisionTick = Math.Max(
                MaxFallbackFollowersPerDecisionTick,
                fallbackFollowerCount - _lastFallbackFollowerCount);

            _burstWindowOpen = false;
        }

        _lastDecisionTicks = decisionTicks;
        _lastPathRequestCount = pathRequestCount;
        _lastBlockedFollowerCount = blockedFollowerCount;
        _lastLandedFollowerCount = landedFollowerCount;
        _lastFallbackFollowerCount = fallbackFollowerCount;
    }

    public MassBattleSquadInvariantReport Build()
    {
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
                StalledUnitsAtEnd = side.StalledUnits,
            }).ToList(),
            DecisionTicksObserved = DecisionTicksObserved,
            MaxPathRequestsPerDecisionTick = MaxPathRequestsPerDecisionTick,
            SquadsAtWorstDecisionTick = SquadsAtWorstDecisionTick,
            MembersAtWorstDecisionTick = MembersAtWorstDecisionTick,
            MaxBlockedFollowersPerDecisionTick = MaxBlockedFollowersPerDecisionTick,
            MaxLandedFollowersPerDecisionTick = MaxLandedFollowersPerDecisionTick,
            MaxFallbackFollowersPerDecisionTick = MaxFallbackFollowersPerDecisionTick,
            LandingRingHistogram = [.. FollowerLanding.LandingRingHistogram],
            StallThresholdFrames = StallThresholdFrames,
            StallRecords = _stallSampler.Records.ToList(),
            WaitForCycles = _stallSampler.WaitForCycles.ToList(),
        };
    }
}
