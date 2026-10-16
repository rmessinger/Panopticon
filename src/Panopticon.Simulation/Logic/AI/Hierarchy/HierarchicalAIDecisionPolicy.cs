#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>
/// The hierarchical family behind the flat <see cref="IAIDecisionPolicy"/> seam (A4-3d):
/// maintains the ground and air squad hierarchies, runs the general every second decision tick
/// (directives persist between general ticks), runs each squad's commander and each soldier
/// every tick, and returns the soldiers' orders through the host's single actuation path. The
/// air commander (WI-M4-05) assigns missions to air squads, which receive them only when
/// parked and rearmed. No rank mutates game state; only the soldier rank and the air mission
/// dispatch produce <see cref="Order"/>s.
/// </summary>
public sealed class HierarchicalAIDecisionPolicy : IAIDecisionPolicy
{
    private readonly IGeneralDecisionPolicy _general;
    private readonly ICommanderDecisionPolicy _commander;
    private readonly ISoldierDecisionPolicy _soldier;
    private readonly IAirCommanderDecisionPolicy _airCommander;

    private readonly Dictionary<uint, CommanderDirective?> _lastDirectives = new();
    private readonly SquadHierarchy _hierarchy = new();
    private readonly SquadHierarchy _airHierarchy = new(SquadDomain.Air);
    private readonly int[] _airMissionAssignments = new int[3];
    private readonly Dictionary<uint, int> _airMissionAssignmentsPerUnit = new();
    private int _decisionTicks;

    /// <summary>The hierarchy this policy maintains; wired onto the skirmish host for persistence.</summary>
    internal SquadHierarchy Hierarchy => _hierarchy;

    /// <summary>The air hierarchy this policy maintains (WI-M4-05). Not persisted: it rebuilds on load.</summary>
    internal SquadHierarchy AirHierarchy => _airHierarchy;

    public HierarchicalAIDecisionPolicy(
        IGeneralDecisionPolicy general,
        ICommanderDecisionPolicy commander,
        ISoldierDecisionPolicy soldier,
        IAirCommanderDecisionPolicy? airCommander = null)
    {
        _general = general;
        _commander = commander;
        _soldier = soldier;
        _airCommander = airCommander ?? new RuleBasedAirCommanderDecisionPolicy();
    }

    public IReadOnlyList<Order> DecideNextOrders(AIDecisionContext context)
    {
        _decisionTicks++;
        _hierarchy.Synchronize(context.Owner);

        if (_decisionTicks % 2 == 1)
        {
            _lastDirectives.Clear();
            foreach (var (squadId, directive) in _general.DecideDirectives(
                         new GeneralDecisionContext(context, SummarizeSquads())))
            {
                _lastDirectives[squadId] = directive;
            }
        }

        var orders = new List<Order>();
        foreach (var squad in _hierarchy.Squads)
        {
            var directive = _lastDirectives.TryGetValue(squad.Id, out var d) ? d : null;
            var goals = _commander.DecideGoals(
                new CommanderDecisionContext(directive, squad, context.OwnerPlayerIndex));

            // WI-M4-03: orders act on the player's current selection, so each decided group is
            // preceded by its own SetSelection. The reference commander emits squad-uniform
            // goals, which collapses to one selection and one order per squad; a soldier rank
            // that decides per member falls back to one selection per member.
            var decidedMembers = new List<GameObject>(squad.Members.Count);
            var decidedOrders = new List<Order>(squad.Members.Count);
            Order? uniformOrder = null;
            var uniform = true;
            foreach (var member in squad.Members)
            {
                var goal = goals.TryGetValue(member.Id.Index, out var g) ? g : null;
                if (_soldier.DecideOrder(
                        new SoldierDecisionContext(member, context.OwnerPlayerIndex, goal))
                    is { } order)
                {
                    decidedMembers.Add(member);
                    decidedOrders.Add(order);
                    if (uniformOrder is null)
                    {
                        uniformOrder = order;
                    }
                    else if (uniform && !OrdersEqual(uniformOrder, order))
                    {
                        uniform = false;
                    }
                }
            }

            if (decidedMembers.Count == 0)
            {
                continue;
            }

            if (uniform)
            {
                orders.Add(Order.CreateSetSelection(
                    context.OwnerPlayerIndex, decidedMembers.Select(member => member.Id)));
                orders.Add(uniformOrder!);
            }
            else
            {
                for (var i = 0; i < decidedMembers.Count; i++)
                {
                    orders.Add(Order.CreateSetSelection(context.OwnerPlayerIndex, decidedMembers[i].Id));
                    orders.Add(decidedOrders[i]);
                }
            }
        }

        DecideAirMissions(context, orders);

        return orders;
    }

    // WI-M4-05: the air half. Missions are assigned every decision tick, but orders are only
    // emitted for squads that are parked with a full clip; a mid-sortie squad keeps its state
    // until it lands, rearms, and accepts the mission current at that point.
    private void DecideAirMissions(AIDecisionContext context, List<Order> orders)
    {
        _airHierarchy.Synchronize(context.Owner);
        if (_airHierarchy.Squads.Count == 0)
        {
            return;
        }

        var directive = _lastDirectives.Values.FirstOrDefault();
        var missions = _airCommander.DecideMissions(new AirCommanderContext(
            context, _airHierarchy.Squads, SummarizeSquads(), directive));

        var squadById = _airHierarchy.Squads.ToDictionary(s => s.Id);
        foreach (var mission in missions)
        {
            if (!squadById.TryGetValue(mission.SquadId, out var squad) || squad.Members.Count == 0)
            {
                continue;
            }

            // The commander assigned this mission whatever the squad's readiness; the per-unit
            // counter below records only missions a member actually received (WI-M4-06).
            _airMissionAssignments[(int)mission.Mission]++;

            // Per-member readiness (full-scale finding): an all-or-nothing squad gate lets one
            // wingman that never lands ground every parked jet in its squad for the whole run.
            // Each ready member is re-tasked individually, matching the M1 scripted semantics.
            var readyMembers = squad.Members.Where(AirMemberReady).ToList();
            foreach (var member in readyMembers)
            {
                _airMissionAssignmentsPerUnit[member.Id.Index]
                    = _airMissionAssignmentsPerUnit.TryGetValue(member.Id.Index, out var count) ? count + 1 : 1;
            }

            if (readyMembers.Count == 0 || mission.Destination is not { } destination)
            {
                continue;
            }

            orders.Add(Order.CreateSetSelection(
                context.OwnerPlayerIndex, readyMembers.Select(member => member.Id)));
            orders.Add(mission.Mission == AirMission.Strike && mission.StrikeTargetId is { } targetId
                ? Order.CreateAttackObject(context.OwnerPlayerIndex, targetId, force: true)
                : Order.CreateMoveOrder(context.OwnerPlayerIndex, destination));
        }
    }

    // Ready to receive a mission. A jet is ready when parked (the airfield's park-heal-rearm
    // cycle: health refills while parked; clips refill through the weapon's own reload cycle
    // once it re-engages, matching the M1 scripted re-issue, which relaunched parked jets on
    // state alone). Aircraft without jet state (tests, future imported aircraft) are ready
    // when they hold no target points.
    private static bool AirMemberReady(GameObject member)
    {
        if (member.IsDestroyed)
        {
            return false;
        }

        if (member.AIUpdate is JetAIUpdate jet)
        {
            return jet.CurrentJetAIState == JetAIUpdate.JetAIState.Parked;
        }

        return member.AIUpdate?.TargetPoints.Count == 0;
    }

    /// <summary>Missions of the given type assigned so far (WI-M4-05 report).</summary>
    internal int AirMissionAssignmentCount(AirMission mission) => _airMissionAssignments[(int)mission];

    /// <summary>Missions assigned to the given aircraft so far (WI-M4-05 report).</summary>
    internal int AirMissionAssignmentCountForUnit(ObjectId unitId) =>
        _airMissionAssignmentsPerUnit.TryGetValue(unitId.Index, out var count) ? count : 0;

    private static bool OrdersEqual(Order left, Order right)
    {
        if (left.OrderType != right.OrderType || left.Arguments.Count != right.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Arguments.Count; i++)
        {
            var leftArgument = left.Arguments[i];
            var rightArgument = right.Arguments[i];
            if (leftArgument.ArgumentType != rightArgument.ArgumentType
                || !ArgumentsEqual(leftArgument.Value, rightArgument.Value, leftArgument.ArgumentType))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ArgumentsEqual(OrderArgumentValue left, OrderArgumentValue right, OrderArgumentType type) =>
        type switch
        {
            OrderArgumentType.Integer => left.Integer == right.Integer,
            OrderArgumentType.Float => left.Float == right.Float,
            OrderArgumentType.Boolean => left.Boolean == right.Boolean,
            OrderArgumentType.ObjectId => left.ObjectId == right.ObjectId,
            OrderArgumentType.Position => left.Position == right.Position,
            OrderArgumentType.ScreenPosition => left.ScreenPosition.Equals(right.ScreenPosition),
            OrderArgumentType.ScreenRectangle => left.ScreenRectangle.Equals(right.ScreenRectangle),
            _ => false,
        };

    private IReadOnlyList<SquadSummary> SummarizeSquads()
    {
        var summaries = new List<SquadSummary>(_hierarchy.Squads.Count);
        foreach (var squad in _hierarchy.Squads)
        {
            var healthSum = 0f;
            foreach (var member in squad.Members)
            {
                var max = member.BodyModule?.MaxHealth ?? 0f;
                healthSum += max > 0f ? (member.BodyModule?.Health ?? 0f) / max : 0f;
            }

            summaries.Add(new SquadSummary(
                squad.Id,
                squad.Members.Count,
                squad.Members.Count > 0 ? healthSum / squad.Members.Count : 0f,
                RuleBasedCommanderDecisionPolicy.Centroid(squad)));
        }

        return summaries;
    }
}
