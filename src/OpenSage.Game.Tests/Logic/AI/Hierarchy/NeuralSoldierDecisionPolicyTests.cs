using System;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.AI.Hierarchy;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using Xunit;

namespace OpenSage.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-A4-25: the soldier network follows the frozen contract — hand-computed forward pass,
/// lowest-index ties on zero genomes, explicit invalid-length rejection, and action decoding
/// through the existing soldier order surface.
/// </summary>
public sealed class NeuralSoldierDecisionPolicyTests : MockedGameTest
{
    private static GameObject SpawnUnit(TestGame game, Vector3 position, float health = 1f, float maxHealth = 1f)
    {
        var definition = new ObjectDefinition("A4NeuralSoldierUnit");
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = maxHealth, InitialHealth = health },
                ModuleInheritanceMode.Default, inherited: false));
        var obj = game.GameLogic.CreateObject(definition, game.PlayerManager.Players[0]);
        obj.UpdateTransform(position, null);
        return obj;
    }

    [Fact]
    public void InvalidWeightLength_Throws()
    {
        Assert.Throws<System.ArgumentException>(
            () => new NeuralSoldierDecisionPolicy(new float[NeuralSoldierDecisionPolicy.WeightCount - 1]));
    }

    [Fact]
    public void ZeroGenome_TiesResolveToHold()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game, new Vector3(10, 10, 0));
        var policy = new NeuralSoldierDecisionPolicy(new float[NeuralSoldierDecisionPolicy.WeightCount]);

        Assert.Null(policy.DecideOrder(new SoldierDecisionContext(
            unit, 2, new SoldierGoal(TargetId: new ObjectId(5u)))));
    }

    [Fact]
    public void BiasDrivenAttackAction_AttacksGoalTarget()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game, new Vector3(10, 10, 0));
        var weights = new float[NeuralSoldierDecisionPolicy.WeightCount];
        weights[72 + 1] = 5f; // b2[1] = attack goal target (W1=48, b1=6, W2=18).

        var policy = new NeuralSoldierDecisionPolicy(weights);
        var order = policy.DecideOrder(new SoldierDecisionContext(
            unit, 3, new SoldierGoal(TargetId: new ObjectId(7u))));

        Assert.Equal(OrderType.ForceAttackObject, order!.OrderType);
        Assert.Equal(3, order.PlayerIndex);
    }

    [Fact]
    public void BiasDrivenMoveAction_MovesToGoalDestination()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game, new Vector3(10, 10, 0));
        var destination = new Vector3(400f, 250f, 0f);
        var weights = new float[NeuralSoldierDecisionPolicy.WeightCount];
        weights[72 + 2] = 5f; // b2[2] = move to destination.

        var policy = new NeuralSoldierDecisionPolicy(weights);
        var order = policy.DecideOrder(new SoldierDecisionContext(
            unit, 4, new SoldierGoal(Destination: destination)));

        Assert.Equal(OrderType.MoveTo, order!.OrderType);
    }

    [Fact]
    public void AttackActionWithoutValidTarget_FallsBackToHold()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game, new Vector3(10, 10, 0));
        var weights = new float[NeuralSoldierDecisionPolicy.WeightCount];
        weights[72 + 1] = 5f;

        var policy = new NeuralSoldierDecisionPolicy(weights);

        Assert.Null(policy.DecideOrder(new SoldierDecisionContext(unit, 2, new SoldierGoal())));
    }

    [Fact]
    public void HandComputedForwardPass_MatchesTheDocumentedMath()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game, new Vector3(0, 0, 0), health: 0.5f, maxHealth: 1f);
        var weights = new float[NeuralSoldierDecisionPolicy.WeightCount];

        // Every hidden unit reads only feature 0 (=0.5): h_j = tanh(0.8 * 0.5).
        for (var j = 0; j < NeuralSoldierDecisionPolicy.HiddenSize; j++)
        {
            weights[j * NeuralSoldierDecisionPolicy.InputSize] = 0.8f;
        }

        // Output 2 (move) reads every hidden unit at 0.25.
        var w2Offset = NeuralSoldierDecisionPolicy.InputSize * NeuralSoldierDecisionPolicy.HiddenSize
            + NeuralSoldierDecisionPolicy.HiddenSize;
        for (var j = 0; j < NeuralSoldierDecisionPolicy.HiddenSize; j++)
        {
            weights[w2Offset + 2 * NeuralSoldierDecisionPolicy.HiddenSize + j] = 0.25f;
        }

        var policy = new NeuralSoldierDecisionPolicy(weights);
        Span<float> features = stackalloc float[NeuralSoldierDecisionPolicy.InputSize];
        policy.Encode(new SoldierDecisionContext(unit, 2, null), features);

        Assert.Equal(0.5f, features[0]);

        Span<float> hidden = stackalloc float[NeuralSoldierDecisionPolicy.HiddenSize];
        Span<float> logits = stackalloc float[NeuralSoldierDecisionPolicy.OutputSize];
        var core = new NeuralRankNetwork(weights, NeuralSoldierDecisionPolicy.InputSize, NeuralSoldierDecisionPolicy.HiddenSize, NeuralSoldierDecisionPolicy.OutputSize);
        core.ForwardPass(features, hidden, logits);

        var expectedLogit2 = NeuralSoldierDecisionPolicy.HiddenSize * 0.25f * MathF.Tanh(0.4f);
        Assert.Equal(expectedLogit2, logits[2], 5);
        Assert.Equal(2, ArgMax(logits));
    }

    private static int ArgMax(ReadOnlySpan<float> logits)
    {
        var best = 0;
        for (var k = 1; k < logits.Length; k++)
        {
            if (logits[k] > logits[best])
            {
                best = k;
            }
        }

        return best;
    }
}
