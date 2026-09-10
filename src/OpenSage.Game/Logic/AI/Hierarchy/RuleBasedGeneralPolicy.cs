#nullable enable

using System.Collections.Generic;

namespace OpenSage.Logic.AI.Hierarchy;

/// <summary>
/// Reference general policy (A4-3d): splits today's flat military behavior across ranks.
/// Every squad receives an attack directive on the reference target (an enemy attacker
/// inside the player's base first, else the first enemy structure, matching
/// <see cref="RuleBasedAIDecisionPolicy"/>'s defensive-then-offensive priority), or a defend
/// directive when no target exists. Economy and production decisions stay the flat policy's
/// surface; the hierarchical reference covers the military facet.
/// </summary>
public sealed class RuleBasedGeneralPolicy : IGeneralDecisionPolicy
{
    public IReadOnlyDictionary<uint, CommanderDirective> DecideDirectives(in GeneralDecisionContext context)
    {
        var owner = context.Context.Owner;
        var target = RuleBasedAIDecisionPolicy.FindDefensiveTarget(owner)
            ?? RuleBasedAIDecisionPolicy.FindOffensiveTargetStructure(owner);

        var directives = new Dictionary<uint, CommanderDirective>();
        foreach (var squad in context.Squads)
        {
            directives[squad.SquadId] = target is not null
                ? new CommanderDirective(DirectivePosture.Attack, TargetId: target.Id)
                : new CommanderDirective(DirectivePosture.Defend);
        }

        return directives;
    }
}
