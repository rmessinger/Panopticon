#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Logic.Object;

namespace OpenSage.Logic.AI.Hierarchy;

/// <summary>
/// The general-rank network (A4-3e): 9-8-4 tanh/argmax over the documented player-level
/// feature vector with four squad-summary slots, decoding to a uniform directive template
/// for every squad. Genome length 116. Contract: hierarchical-neural-network-policies.md §3.
/// </summary>
public sealed class NeuralGeneralPolicy : IGeneralDecisionPolicy
{
    public const int InputSize = 9;
    public const int HiddenSize = 8;
    public const int OutputSize = 4;
    public const int WeightCount = InputSize * HiddenSize + HiddenSize + HiddenSize * OutputSize + OutputSize;

    private const int SquadSlotCount = 4;
    private const float TotalUnitsCap = 24f;
    private const float CashNormalizationScale = 20000f;

    private readonly NeuralRankNetwork _network;

    public NeuralGeneralPolicy(float[]? weights)
    {
        _network = new NeuralRankNetwork(weights, InputSize, HiddenSize, OutputSize);
    }

    public IReadOnlyDictionary<uint, CommanderDirective> DecideDirectives(in GeneralDecisionContext context)
    {
        Span<float> features = stackalloc float[InputSize];
        Encode(context, features);

        var directive = DirectiveTemplate(_network.Decide(features), context.Context.Owner);
        var directives = new Dictionary<uint, CommanderDirective>(context.Squads.Count);
        foreach (var squad in context.Squads)
        {
            directives[squad.SquadId] = directive;
        }

        return directives;
    }

    internal void Encode(in GeneralDecisionContext context, Span<float> features)
    {
        var owner = context.Context.Owner;
        features[0] = MathF.Min(
            MathF.Log(1f + owner.BankAccount.Money) / MathF.Log(1f + CashNormalizationScale),
            1f);
        features[1] = MathF.Min(context.Squads.Sum(s => s.MemberCount) / TotalUnitsCap, 1f);
        features[2] = context.Squads.Count > 0 ? context.Squads.Average(s => s.AverageHealthFraction) : 0f;
        features[3] = owner.Enemies.Any(
            enemy => enemy.OwnedObjects.Any(x => !x.IsStructure && x.CanAttack))
            ? 1f
            : 0f;
        features[4] = owner.Enemies.Any(
            enemy => enemy.OwnedObjects.Any(x => x.IsStructure))
            ? 1f
            : 0f;

        for (var i = 0; i < SquadSlotCount; i++)
        {
            features[5 + i] = i < context.Squads.Count
                ? MathF.Min(context.Squads[i].MemberCount / (float)SquadHierarchy.MaxSquadSize, 1f) * 0.5f
                    + context.Squads[i].AverageHealthFraction * 0.5f
                : 0f;
        }
    }

    private static CommanderDirective DirectiveTemplate(int action, Player owner)
    {
        return action switch
        {
            0 => AttackDirective(RuleBasedAIDecisionPolicy.FindDefensiveTarget(owner)
                ?? RuleBasedAIDecisionPolicy.FindOffensiveTargetStructure(owner)),
            1 => AttackDirective(RuleBasedAIDecisionPolicy.FindOffensiveTargetStructure(owner)),
            3 => new CommanderDirective(DirectivePosture.Regroup),
            _ => new CommanderDirective(DirectivePosture.Defend),
        };

        static CommanderDirective AttackDirective(GameObject? target) => target is not null
            ? new CommanderDirective(DirectivePosture.Attack, TargetId: target.Id)
            : new CommanderDirective(DirectivePosture.Defend);
    }
}
