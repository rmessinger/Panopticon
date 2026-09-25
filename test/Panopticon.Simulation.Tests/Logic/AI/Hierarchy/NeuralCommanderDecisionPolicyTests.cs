using System;
using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-A4-26: the commander network follows the frozen bounded-squad contract — uniform goal
/// templates over stable member ordering, padding and overflow behavior, hand-computed
/// decode, lowest-index ties, and explicit invalid-length rejection.
/// </summary>
public sealed class NeuralCommanderDecisionPolicyTests : MockedGameTest
{
    private static GameObject SpawnMember(TestGame game, Player owner, float health, float maxHealth)
    {
        var definition = new ObjectDefinition("A4NeuralCommanderUnit");
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = maxHealth, InitialHealth = health },
                ModuleInheritanceMode.Default, inherited: false));
        return game.GameLogic.CreateObject(definition, owner);
    }

    private static Squad BuildSquad(TestGame game, Player owner, int memberCount, float health = 1f)
    {
        var squad = new Squad(1u);
        for (var i = 0; i < memberCount; i++)
        {
            squad.Members.Add(SpawnMember(game, owner, health, 1f));
        }

        squad.PromoteNextCommander();
        return squad;
    }

    [Fact]
    public void InvalidWeightLength_Throws()
    {
        Assert.Throws<System.ArgumentException>(
            () => new NeuralCommanderDecisionPolicy(new float[NeuralCommanderDecisionPolicy.WeightCount + 1]));
    }

    [Fact]
    public void ZeroGenome_HoldsEveryMemberAtCentroid()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var squad = BuildSquad(game, game.PlayerManager.Players[0], memberCount: 3);
        var policy = new NeuralCommanderDecisionPolicy(new float[NeuralCommanderDecisionPolicy.WeightCount]);

        var goals = policy.DecideGoals(new CommanderDecisionContext(null, squad, 2));

        Assert.Equal(3, goals.Count);
        Assert.All(goals.Values, g =>
        {
            Assert.Equal(GoalStance.Hold, g.Stance);
            Assert.Null(g.TargetId);
        });
    }

    [Fact]
    public void BiasDrivenAttackAction_ForwardsDirectiveTargetToEveryMember()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var squad = BuildSquad(game, game.PlayerManager.Players[0], memberCount: 2);
        var weights = new float[NeuralCommanderDecisionPolicy.WeightCount];
        weights[152 + 1] = 5f; // b2[1] = attack directive target (W1=112, b1=8, W2=32).
        var target = new ObjectId(31u);

        var policy = new NeuralCommanderDecisionPolicy(weights);
        var goals = policy.DecideGoals(new CommanderDecisionContext(
            new CommanderDirective(DirectivePosture.Attack, TargetId: target),
            squad,
            2));

        Assert.Equal(2, goals.Count);
        Assert.All(goals.Values, g => Assert.Equal(target, g.TargetId));
    }

    [Fact]
    public void AttackActionWithoutTarget_FallsBackToRegion_ThenHold()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var squad = BuildSquad(game, game.PlayerManager.Players[0], memberCount: 1);
        var weights = new float[NeuralCommanderDecisionPolicy.WeightCount];
        weights[152 + 1] = 5f;
        var region = new Vector3(600f, 100f, 0f);

        var policy = new NeuralCommanderDecisionPolicy(weights);

        var regionGoals = policy.DecideGoals(new CommanderDecisionContext(
            new CommanderDirective(DirectivePosture.Attack, TargetRegion: region), squad, 2));
        Assert.All(regionGoals.Values, g => Assert.Equal(region, g.Destination));

        var holdGoals = policy.DecideGoals(new CommanderDecisionContext(
            new CommanderDirective(DirectivePosture.Attack), squad, 2));
        Assert.All(holdGoals.Values, g => Assert.Equal(GoalStance.Hold, g.Stance));
    }

    [Fact]
    public void RetreatAction_SendsEveryMemberToCentroid()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var squad = BuildSquad(game, game.PlayerManager.Players[0], memberCount: 2);
        squad.Members[0].UpdateTransform(new Vector3(0, 0, 0), null);
        squad.Members[1].UpdateTransform(new Vector3(100, 0, 0), null);
        var weights = new float[NeuralCommanderDecisionPolicy.WeightCount];
        weights[152 + 3] = 5f; // b2[3] = retreat.

        var policy = new NeuralCommanderDecisionPolicy(weights);
        var goals = policy.DecideGoals(new CommanderDecisionContext(null, squad, 2));

        Assert.All(goals.Values, g =>
        {
            Assert.Equal(GoalStance.Retreat, g.Stance);
            Assert.Equal(new Vector3(50f, 0, 0), g.Destination);
        });
    }

    [Fact]
    public void Encoding_PadsMemberSlotsAndAggregates()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var owner = game.PlayerManager.Players[0];
        var squad = new Squad(1u);
        squad.Members.Add(SpawnMember(game, owner, 0.5f, 1f));
        squad.Members.Add(SpawnMember(game, owner, 0f, 1f));
        squad.PromoteNextCommander();

        var policy = new NeuralCommanderDecisionPolicy(new float[NeuralCommanderDecisionPolicy.WeightCount]);
        Span<float> features = stackalloc float[NeuralCommanderDecisionPolicy.InputSize];
        policy.Encode(new CommanderDecisionContext(null, squad, 2), features);

        Assert.Equal(2f / SquadHierarchy.MaxSquadSize, features[2]);
        Assert.Equal(0.25f, features[3]); // average health (0.5 + 0) / 2
        Assert.Equal(0.5f, features[5]);  // first member slot
        Assert.Equal(0f, features[6]);    // second member slot
        for (var i = 7; i < features.Length; i++)
        {
            Assert.Equal(0f, features[i]); // padding
        }
    }

    [Fact]
    public void OversizedSquad_IsRejected()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var owner = game.PlayerManager.Players[0];
        var squad = new Squad(1u);
        for (var i = 0; i < SquadHierarchy.MaxSquadSize + 1; i++)
        {
            squad.Members.Add(SpawnMember(game, owner, 1f, 1f));
        }

        var policy = new NeuralCommanderDecisionPolicy(new float[NeuralCommanderDecisionPolicy.WeightCount]);

        Assert.Throws<System.InvalidOperationException>(
            () => policy.DecideGoals(new CommanderDecisionContext(null, squad, 2)));
    }

    [Fact]
    public void Goals_KeyedOnlyByLiveMembers_InStableOrder()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var squad = BuildSquad(game, game.PlayerManager.Players[0], memberCount: 3);
        var dead = squad.Members[1];
        game.GameLogic.DestroyObject(dead);
        squad.Members.Remove(dead);

        var policy = new NeuralCommanderDecisionPolicy(new float[NeuralCommanderDecisionPolicy.WeightCount]);
        var goals = policy.DecideGoals(new CommanderDecisionContext(null, squad, 2));

        Assert.Equal(squad.Members.Select(m => m.Id.Index).OrderBy(id => id), goals.Keys.OrderBy(id => id));
    }
}
