#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>
/// The hierarchical family behind the flat <see cref="IAIDecisionPolicy"/> seam (A4-3d):
/// maintains the squad hierarchy, runs the general every second decision tick (directives
/// persist between general ticks), runs each squad's commander and each soldier every tick,
/// and returns the soldiers' orders through the host's single actuation path. No rank
/// mutates game state; only the soldier rank produces <see cref="Order"/>s.
/// </summary>
public sealed class HierarchicalAIDecisionPolicy : IAIDecisionPolicy
{
    private readonly IGeneralDecisionPolicy _general;
    private readonly ICommanderDecisionPolicy _commander;
    private readonly ISoldierDecisionPolicy _soldier;

    private readonly Dictionary<uint, CommanderDirective?> _lastDirectives = new();
    private readonly SquadHierarchy _hierarchy = new();
    private int _decisionTicks;

    /// <summary>The hierarchy this policy maintains; wired onto the skirmish host for persistence.</summary>
    internal SquadHierarchy Hierarchy => _hierarchy;

    public HierarchicalAIDecisionPolicy(
        IGeneralDecisionPolicy general,
        ICommanderDecisionPolicy commander,
        ISoldierDecisionPolicy soldier)
    {
        _general = general;
        _commander = commander;
        _soldier = soldier;
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

        return orders;
    }

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
