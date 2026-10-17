using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-A4-28: the designed fixture genomes run through the complete hierarchy — general
/// directives, commander goals, and soldier orders — with cadence gating, per-rank genome
/// use, a single dispatch of the final orders, and save round-trip behavior.
/// </summary>
public sealed class HierarchicalNeuralInferenceTests : MockedGameTest
{
    private sealed class SpyGeneral : IGeneralDecisionPolicy
    {
        public int Calls;
        private readonly IGeneralDecisionPolicy _inner;

        public SpyGeneral(IGeneralDecisionPolicy inner) => _inner = inner;

        public IReadOnlyDictionary<uint, CommanderDirective> DecideDirectives(in GeneralDecisionContext context)
        {
            Calls++;
            return _inner.DecideDirectives(in context);
        }
    }

    private sealed class SpyCommander : ICommanderDecisionPolicy
    {
        public int Calls;
        private readonly ICommanderDecisionPolicy _inner;

        public SpyCommander(ICommanderDecisionPolicy inner) => _inner = inner;

        public IReadOnlyDictionary<uint, SoldierGoal> DecideGoals(in CommanderDecisionContext context)
        {
            Calls++;
            return _inner.DecideGoals(in context);
        }
    }

    private sealed class SpySoldier : ISoldierDecisionPolicy
    {
        public int Calls;
        public List<SoldierGoal?> GoalsSeen = [];
        private readonly ISoldierDecisionPolicy _inner;

        public SpySoldier(ISoldierDecisionPolicy inner) => _inner = inner;

        public Order? DecideOrder(in SoldierDecisionContext context)
        {
            Calls++;
            GoalsSeen.Add(context.Goal);
            return _inner.DecideOrder(in context);
        }
    }

    private static (TestGame Game, Player Ai, GameObject EnemyAttacker) CreateFixture()
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
        var ai = game.PlayerManager.Players[2];
        var enemy = game.PlayerManager.Players[3];

        // One squad: a commander and two soldiers (all three eligible members).
        SpawnUnit(game, ai, "A4FixtureCommander", buildCost: 2000f);
        SpawnUnit(game, ai, "A4FixtureSoldier1", buildCost: 100f);
        SpawnUnit(game, ai, "A4FixtureSoldier2", buildCost: 100f);

        // A friendly structure (enables the defensive-target priority) and the enemy
        // attacker the fixture's general genome targets.
        var structure = new ObjectDefinition("A4FixtureOwnStructure");
        structure.KindOf.Set(ObjectKinds.Structure, true);
        game.GameLogic.CreateObject(structure, ai);

        var attackerDefinition = new ObjectDefinition("A4FixtureEnemyAttacker");
        attackerDefinition.KindOf.Set(ObjectKinds.Infantry, true);
        attackerDefinition.KindOf.Set(ObjectKinds.CanAttack, true);
        var attacker = game.GameLogic.CreateObject(attackerDefinition, enemy);

        return (game, ai, attacker);
    }

    private static GameObject SpawnUnit(TestGame game, Player owner, string name, float buildCost)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildCost))!
            .SetValue(definition, buildCost);
        return game.GameLogic.CreateObject(definition, owner);
    }

    private static float[] SoldierGenome()
    {
        var weights = new float[NeuralSoldierDecisionPolicy.WeightCount];
        weights[73] = 5f; // b2[1]: attack the goal target.
        return weights;
    }

    private static float[] CommanderGenome()
    {
        var weights = new float[NeuralCommanderDecisionPolicy.WeightCount];
        weights[153] = 5f; // b2[1]: attack the directive target.
        return weights;
    }

    private static float[] GeneralGenome()
    {
        var weights = new float[NeuralGeneralPolicy.WeightCount];
        weights[112] = 5f; // b2[0]: defensive-first attack directive.
        return weights;
    }

    private static (HierarchicalAIDecisionPolicy Policy, SpyGeneral General, SpyCommander Commander, SpySoldier Soldier) CreateSpiedHierarchy()
    {
        var general = new SpyGeneral(new NeuralGeneralPolicy(GeneralGenome()));
        var commander = new SpyCommander(new NeuralCommanderDecisionPolicy(CommanderGenome()));
        var soldier = new SpySoldier(new NeuralSoldierDecisionPolicy(SoldierGenome()));
        return (new HierarchicalAIDecisionPolicy(general, commander, soldier), general, commander, soldier);
    }

    [Fact]
    public void FixtureGenomes_ProduceTheExpectedFullFlowAndOrders()
    {
        var (game, ai, attacker) = CreateFixture();
        var (policy, generalSpy, commanderSpy, soldierSpy) = CreateSpiedHierarchy();

        var orders = policy.DecideNextOrders(new AIDecisionContext(ai, 2, 1));

        // Every rank ran once; the squad's three members each decided on the enemy attacker
        // the general's genome selected, through the commander's goal forwarding. WI-M4-03:
        // the squad-uniform decisions collapse into one SetSelection of the three members
        // followed by the single order.
        Assert.Equal(1, generalSpy.Calls);
        Assert.Equal(1, commanderSpy.Calls);
        Assert.Equal(3, soldierSpy.Calls);
        Assert.All(soldierSpy.GoalsSeen, g => Assert.Equal(attacker.Id, g!.TargetId));
        Assert.Equal(2, orders.Count);
        Assert.Equal(OrderType.SetSelection, orders[0].OrderType);
        Assert.Equal(OrderType.ForceAttackObject, orders[1].OrderType);
        Assert.Equal(2, orders[1].PlayerIndex);
    }

    [Fact]
    public void Cadence_GeneralRunsEverySecondTick_CommandersAndSoldiersEveryTick()
    {
        var (game, ai, _) = CreateFixture();
        var (policy, generalSpy, commanderSpy, soldierSpy) = CreateSpiedHierarchy();

        policy.DecideNextOrders(new AIDecisionContext(ai, 2, 1));
        var orders = policy.DecideNextOrders(new AIDecisionContext(ai, 2, 2));

        // The general was not due on the second tick, but its directives persisted: the
        // commanders still forwarded goals and the soldiers still decided. WI-M4-03: each
        // tick's orders are the squad selection followed by the uniform attack order.
        Assert.Equal(1, generalSpy.Calls);
        Assert.Equal(2, commanderSpy.Calls);
        Assert.Equal(6, soldierSpy.Calls);
        Assert.Equal(2, orders.Count);
        Assert.Equal(OrderType.SetSelection, orders[0].OrderType);
        Assert.Equal(OrderType.ForceAttackObject, orders[1].OrderType);
    }

    [Fact]
    public void FixtureGenomes_ReachTheRanksThroughTheFactory()
    {
        var (game, ai, _) = CreateFixture();
        var policy = (HierarchicalAIDecisionPolicy)AIDecisionPolicyFactory.Create(
            new AIDecisionPolicyPayload
            {
                Type = AIDecisionPolicyType.Hierarchical,
                HierarchicalRankWeights = new HierarchicalRankWeights(
                    SoldierGenome(), CommanderGenome(), GeneralGenome()),
            });

        var orders = policy.DecideNextOrders(new AIDecisionContext(ai, 2, 1));

        // WI-M4-03: one selection plus the squad's uniform order reaches the boundary.
        Assert.Equal(2, orders.Count);
        Assert.Equal(OrderType.SetSelection, orders[0].OrderType);
        Assert.Equal(OrderType.ForceAttackObject, orders[1].OrderType);
    }

    [Fact]
    public void OrdersDispatchOnce_ThroughThePlayerBoundary_AndSaveRoundTrips()
    {
        var (game, ai, _) = CreateFixture();
        var policy = (HierarchicalAIDecisionPolicy)AIDecisionPolicyFactory.Create(
            new AIDecisionPolicyPayload
            {
                Type = AIDecisionPolicyType.Hierarchical,
                HierarchicalRankWeights = new HierarchicalRankWeights(
                    SoldierGenome(), CommanderGenome(), GeneralGenome()),
            });
        var aiPlayer = new SkirmishAIPlayer(ai, policy) { Hierarchy = policy.Hierarchy };

        // One decision tick: the orders leave through the single dispatch boundary.
        for (var i = 0; i < (int)AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            aiPlayer.Update();
        }

        // WI-M4-03: one decision tick leaves through the single dispatch boundary as the squad
        // selection plus the collapsed uniform order.
        Assert.Equal(2, game.InjectedOrders.Count);
        Assert.All(game.InjectedOrders, o => Assert.Equal(2, o.PlayerIndex));

        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            using (var writer = new StateWriter(stream, game))
            {
                aiPlayer.Persist(writer);
            }

            bytes = stream.ToArray();
        }

        var reloaded = new SkirmishAIPlayer(ai, policy);
        using (var reader = new StateReader(new MemoryStream(bytes), game))
        {
            reloaded.Persist(reader);
        }

        Assert.NotNull(reloaded.Hierarchy);
        reloaded.Hierarchy.Synchronize(ai);
        Assert.Equal(
            policy.Hierarchy.Squads.Select(s => s.CommanderId),
            reloaded.Hierarchy.Squads.Select(s => s.CommanderId));
    }
}
