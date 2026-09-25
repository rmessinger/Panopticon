using System;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-A4-27: the general network follows the frozen bounded squad-summary contract, decoding
/// uniform directives with the documented fallback chain, and the factory builds the neural
/// hierarchy from validated per-rank weights.
/// </summary>
public sealed class NeuralGeneralPolicyTests : MockedGameTest
{
    private static (TestGame Game, Player Ai, Player Enemy) CreateTwoPlayerGame()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var mapPlayers = new[]
        {
            Panopticon.Data.Map.Player.CreateNeutralPlayer(),
            Panopticon.Data.Map.Player.CreateCivilianPlayer(),
            new Panopticon.Data.Map.Player
            {
                Name = "ai",
                Faction = "FactionAmerica",
                DisplayName = "AI",
                IsHuman = false,
                Enemies = "enemy",
            },
            new Panopticon.Data.Map.Player
            {
                Name = "enemy",
                Faction = "FactionGLA",
                DisplayName = "Enemy",
                IsHuman = false,
                Enemies = "ai",
            },
        };
        game.PlayerManager.OnNewGame(mapPlayers, GameType.Skirmish);
        return (game, game.PlayerManager.Players[2], game.PlayerManager.Players[3]);
    }

    private static GameObject SpawnStructure(TestGame game, Player owner)
    {
        var definition = new ObjectDefinition("A4NeuralGeneralStructure");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        return game.GameLogic.CreateObject(definition, owner);
    }

    [Fact]
    public void InvalidWeightLength_Throws()
    {
        Assert.Throws<System.ArgumentException>(
            () => new NeuralGeneralPolicy(new float[NeuralGeneralPolicy.WeightCount - 1]));
    }

    [Fact]
    public void ZeroGenome_DefendsEverySquad()
    {
        var (game, ai, _) = CreateTwoPlayerGame();
        var summaries = new[]
        {
            new SquadSummary(1u, 3, 1f, new System.Numerics.Vector3(0, 0, 0)),
            new SquadSummary(2u, 1, 0.5f, new System.Numerics.Vector3(50, 0, 0)),
        };
        var policy = new NeuralGeneralPolicy(new float[NeuralGeneralPolicy.WeightCount]);

        var directives = policy.DecideDirectives(new GeneralDecisionContext(
            new AIDecisionContext(ai, 2, 1), summaries));

        Assert.Equal(2, directives.Count);
        Assert.All(directives.Values, d => Assert.Equal(DirectivePosture.Defend, d.Posture));
    }

    [Fact]
    public void BiasDrivenDefensiveAttack_TargetsEnemyAttacker()
    {
        var (game, ai, enemy) = CreateTwoPlayerGame();
        var attackerDefinition = new ObjectDefinition("A4NeuralGeneralAttacker");
        attackerDefinition.KindOf.Set(ObjectKinds.Infantry, true);
        attackerDefinition.KindOf.Set(ObjectKinds.CanAttack, true);
        var attacker = game.GameLogic.CreateObject(attackerDefinition, enemy);
        // Defensive targets require an owned structure to defend.
        SpawnStructure(game, ai);

        var weights = new float[NeuralGeneralPolicy.WeightCount];
        weights[112 + 0] = 5f; // b2[0] = attack defensive target (W1=72, b1=8, W2=32).

        var policy = new NeuralGeneralPolicy(weights);
        var directives = policy.DecideDirectives(new GeneralDecisionContext(
            new AIDecisionContext(ai, 2, 1),
            [new SquadSummary(1u, 2, 1f, new System.Numerics.Vector3(0, 0, 0))]));

        var directive = Assert.Single(directives.Values);
        Assert.Equal(DirectivePosture.Attack, directive.Posture);
        Assert.Equal(attacker.Id, directive.TargetId);
    }

    [Fact]
    public void OffensiveAction_FallsBackToDefend_WithoutTargets()
    {
        var (game, ai, _) = CreateTwoPlayerGame();
        var weights = new float[NeuralGeneralPolicy.WeightCount];
        weights[112 + 1] = 5f; // b2[1] = attack offensive structure.

        var policy = new NeuralGeneralPolicy(weights);
        var directives = policy.DecideDirectives(new GeneralDecisionContext(
            new AIDecisionContext(ai, 2, 1),
            [new SquadSummary(1u, 1, 1f, new System.Numerics.Vector3(0, 0, 0))]));

        Assert.Equal(DirectivePosture.Defend, Assert.Single(directives.Values).Posture);
    }

    [Fact]
    public void Encoding_PadsSquadSlots_AggregatesSummaries()
    {
        var (game, ai, _) = CreateTwoPlayerGame();
        ai.BankAccount.Money = 0u;
        var summaries = new[]
        {
            new SquadSummary(1u, 4, 1f, new System.Numerics.Vector3(0, 0, 0)),
            new SquadSummary(2u, 2, 0.5f, new System.Numerics.Vector3(0, 0, 0)),
        };

        var policy = new NeuralGeneralPolicy(new float[NeuralGeneralPolicy.WeightCount]);
        Span<float> features = stackalloc float[NeuralGeneralPolicy.InputSize];
        policy.Encode(new GeneralDecisionContext(new AIDecisionContext(ai, 2, 1), summaries), features);

        Assert.Equal(0f, features[0]);                      // zero cash normalizes to 0
        Assert.Equal(6f / 24f, features[1]);                // total units / 24
        Assert.Equal(0.75f, features[2]);                   // mean of squad healths
        Assert.Equal(0f, features[3]);                      // no enemy attacker seen
        // Slot 0: (4/8)*0.5 + 1*0.5 = 0.75; slot 1: (2/8)*0.5 + 0.5*0.5 = 0.375.
        Assert.Equal(0.75f, features[5]);
        Assert.Equal(0.375f, features[6]);
        Assert.Equal(0f, features[7]);
        Assert.Equal(0f, features[8]);
    }

    [Fact]
    public void Factory_NeuralHierarchy_ValidatesEveryRankLength()
    {
        Assert.Throws<System.ArgumentException>(() => AIDecisionPolicyFactory.Create(
            new AIDecisionPolicyPayload
            {
                Type = AIDecisionPolicyType.Hierarchical,
                HierarchicalRankWeights = new HierarchicalRankWeights(
                    Soldier: new float[NeuralSoldierDecisionPolicy.WeightCount - 1],
                    Commander: new float[NeuralCommanderDecisionPolicy.WeightCount],
                    General: new float[NeuralGeneralPolicy.WeightCount]),
            }));

        Assert.Throws<System.ArgumentNullException>(() => AIDecisionPolicyFactory.Create(
            new AIDecisionPolicyPayload
            {
                Type = AIDecisionPolicyType.Hierarchical,
                HierarchicalRankWeights = new HierarchicalRankWeights(
                    Soldier: new float[NeuralSoldierDecisionPolicy.WeightCount],
                    Commander: new float[NeuralCommanderDecisionPolicy.WeightCount],
                    General: null),
            }));
    }

    [Fact]
    public void Factory_NeuralHierarchy_ComposesTheThreeRankNetworks()
    {
        var policy = AIDecisionPolicyFactory.Create(new AIDecisionPolicyPayload
        {
            Type = AIDecisionPolicyType.Hierarchical,
            HierarchicalRankWeights = new HierarchicalRankWeights(
                Soldier: new float[NeuralSoldierDecisionPolicy.WeightCount],
                Commander: new float[NeuralCommanderDecisionPolicy.WeightCount],
                General: new float[NeuralGeneralPolicy.WeightCount]),
        });

        Assert.IsType<HierarchicalAIDecisionPolicy>(policy);
    }
}
