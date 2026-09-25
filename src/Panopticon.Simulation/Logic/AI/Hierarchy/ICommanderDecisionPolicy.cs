#nullable enable

using System.Collections.Generic;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>
/// The commander rank's input (A4-3c): the general's directive for this squad (null before
/// the first general tick), the squad's live members in stable object-id order, and the
/// owner's player index for goals that carry orders.
/// </summary>
public readonly record struct CommanderDecisionContext(
    CommanderDirective? Directive,
    Squad Squad,
    int OwnerPlayerIndex);

/// <summary>
/// The middle rank of the command hierarchy: decides one goal per soldier in its squad.
/// Output is bounded by <see cref="SquadHierarchy.MaxSquadSize"/>; never mutates state.
/// </summary>
public interface ICommanderDecisionPolicy
{
    IReadOnlyDictionary<uint, SoldierGoal> DecideGoals(in CommanderDecisionContext context);
}
