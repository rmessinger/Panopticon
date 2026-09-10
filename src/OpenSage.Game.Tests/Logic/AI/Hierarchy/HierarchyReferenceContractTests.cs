using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.AI;
using OpenSage.Logic.AI.Hierarchy;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using Xunit;

namespace OpenSage.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-A4-23: the reference hierarchy flows directives -> goals -> orders at the documented
/// cadences, repairs commander loss deterministically, persists its identity, and actuates
/// only through the existing order-dispatch boundary.
/// </summary>
public sealed class HierarchyReferenceContractTests : MockedGameTest
{
    private sealed record SpyRanks : IGeneralDecisionPolicy, ICommanderDecisionPolicy, ISoldierDecisionPolicy
    {
        public int GeneralCalls;
        public int CommanderCalls;
        public int SoldierCalls;
        public List<uint> SquadIdsSeenByGeneral = [];
        public List<CommanderDirective?> DirectivesSeenByCommander = [];
        public List<SoldierGoal?> GoalsSeenBySoldier = [];

        public IReadOnlyDictionary<uint, CommanderDirective> DecideDirectives(in GeneralDecisionContext context)
        {
            GeneralCalls++;
            SquadIdsSeenByGeneral.AddRange(context.Squads.Select(s => s.SquadId));
            return context.Squads.ToDictionary(s => s.SquadId, _ => new CommanderDirective(DirectivePosture.Attack, TargetId: new ObjectId(99u)));
        }

        public IReadOnlyDictionary<uint, SoldierGoal> DecideGoals(in CommanderDecisionContext context)
        {
            CommanderCalls++;
            DirectivesSeenByCommander.Add(context.Directive);
            var targetId = context.Directive?.TargetId;
            return context.Squad.Members.ToDictionary(
                m => m.Id.Index,
                _ => new SoldierGoal(TargetId: targetId));
        }

        public Order? DecideOrder(in SoldierDecisionContext context)
        {
            SoldierCalls++;
            GoalsSeenBySoldier.Add(context.Goal);
            return null;
        }
    }

    private static (TestGame Game, Player Ai, Player Enemy) CreateTwoPlayerGame()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var mapPlayers = new[]
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
            new OpenSage.Data.Map.Player
            {
                Name = "ai",
                Faction = "FactionAmerica",
                DisplayName = "AI",
                IsHuman = false,
                Enemies = "enemy",
            },
            new OpenSage.Data.Map.Player
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

    private static GameObject SpawnUnit(TestGame game, Player owner, float buildCost)
    {
        var definition = new ObjectDefinition("A4ContractMember");
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildCost))!
            .SetValue(definition, buildCost);
        return game.GameLogic.CreateObject(definition, owner);
    }

    private static GameObject SpawnStructure(TestGame game, Player owner, string name)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Structure, true);
        return game.GameLogic.CreateObject(definition, owner);
    }

    [Fact]
    public void Cadences_DirectivesGoalsAndOrders_FlowThroughEveryRank()
    {
        var (game, ai, _) = CreateTwoPlayerGame();
        for (var i = 0; i < 3; i++)
        {
            SpawnUnit(game, ai, buildCost: 100f + i);
        }

        var spy = new SpyRanks();
        var policy = new HierarchicalAIDecisionPolicy(spy, spy, spy);

        policy.DecideNextOrders(new AIDecisionContext(ai, 2, 1));
        policy.DecideNextOrders(new AIDecisionContext(ai, 2, 2));

        // One squad of three members; general every 2nd tick, commander and soldier every tick.
        Assert.Equal(1, spy.GeneralCalls);
        Assert.Equal(2, spy.CommanderCalls);
        Assert.Equal(6, spy.SoldierCalls);
        Assert.All(spy.DirectivesSeenByCommander, d => Assert.Equal(new ObjectId(99u), d!.TargetId));
        Assert.All(spy.GoalsSeenBySoldier, g => Assert.NotNull(g!.TargetId));
        Assert.Single(spy.SquadIdsSeenByGeneral.Distinct());
    }

    [Fact]
    public void OrdersFlow_ThroughTheExistingDispatchBoundary_WithNoDirectMutation()
    {
        var (game, ai, enemy) = CreateTwoPlayerGame();
        SpawnUnit(game, ai, buildCost: 100f);
        SpawnUnit(game, ai, buildCost: 200f);
        SpawnStructure(game, enemy, "A4ContractEnemyStructure");

        var policy = AIDecisionPolicyFactory.Create(
            new AIDecisionPolicyPayload { Type = AIDecisionPolicyType.Hierarchical });
        var aiPlayer = new SkirmishAIPlayer(ai, policy);

        for (var i = 0; i < (int)AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            aiPlayer.Update();
        }

        // Every emitted order arrived through IGame.InjectOrders: the only actuation path.
        Assert.NotEmpty(game.InjectedOrders);
        Assert.All(game.InjectedOrders, o => Assert.Equal(2, o.PlayerIndex));
        Assert.Contains(game.InjectedOrders, o => o.OrderType == OrderType.ForceAttackObject);
    }

    [Fact]
    public void CommanderLoss_PromotesDeterministically_AndOrdersContinue()
    {
        var (game, ai, enemy) = CreateTwoPlayerGame();
        var commander = SpawnUnit(game, ai, buildCost: 2000f);
        var lieutenant = SpawnUnit(game, ai, buildCost: 500f);
        var private_ = SpawnUnit(game, ai, buildCost: 100f);
        SpawnStructure(game, enemy, "A4ContractEnemyStructure2");

        var policy = (HierarchicalAIDecisionPolicy)AIDecisionPolicyFactory.Create(
            new AIDecisionPolicyPayload { Type = AIDecisionPolicyType.Hierarchical });
        var context = new AIDecisionContext(ai, 2, 1);
        policy.DecideNextOrders(context);

        var squad = Assert.Single(policy.Hierarchy.Squads);
        Assert.Equal(commander.Id, squad.CommanderId);

        game.GameLogic.DestroyObject(commander);
        var orders = policy.DecideNextOrders(new AIDecisionContext(ai, 2, 2));

        Assert.Equal(lieutenant.Id, squad.CommanderId);
        Assert.Equal(2, squad.Members.Count);
        Assert.NotEmpty(orders);
        Assert.Contains(orders, o => o.OrderType == OrderType.ForceAttackObject);
        Assert.DoesNotContain(orders, o => o.PlayerIndex != 2);
        Assert.All(orders, _ => { });
        Assert.Equal(new List<ObjectId> { lieutenant.Id, private_.Id }.OrderBy(id => id.Index),
            squad.Members.Select(m => m.Id).OrderBy(id => id.Index));
    }

    [Fact]
    public void SaveReload_PreservesHierarchyIdentity_MidFlow()
    {
        var (game, ai, _) = CreateTwoPlayerGame();
        SpawnUnit(game, ai, buildCost: 300f);
        SpawnUnit(game, ai, buildCost: 100f);

        var policy = (HierarchicalAIDecisionPolicy)AIDecisionPolicyFactory.Create(
            new AIDecisionPolicyPayload { Type = AIDecisionPolicyType.Hierarchical });
        var aiPlayer = new SkirmishAIPlayer(ai, policy) { Hierarchy = policy.Hierarchy };
        policy.DecideNextOrders(new AIDecisionContext(ai, 2, 1));

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
            policy.Hierarchy.Squads.Select(s => s.Id),
            reloaded.Hierarchy.Squads.Select(s => s.Id));
        Assert.Equal(
            policy.Hierarchy.Squads.Select(s => s.CommanderId),
            reloaded.Hierarchy.Squads.Select(s => s.CommanderId));
        Assert.Equal(
            policy.Hierarchy.Squads.SelectMany(s => s.Members).Select(m => m.Id).OrderBy(id => id.Index),
            reloaded.Hierarchy.Squads.SelectMany(s => s.Members).Select(m => m.Id).OrderBy(id => id.Index));
    }
}
