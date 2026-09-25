using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-A4-21: the rule-based commander translates one directive into bounded per-soldier
/// goals with stable member ordering, rejecting oversized squads.
/// </summary>
public sealed class CommanderDecisionPolicyTests : MockedGameTest
{
    private static (TestGame Game, Player Player) CreateGame()
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
            },
        };
        game.PlayerManager.OnNewGame(mapPlayers, GameType.Skirmish);
        return (game, game.PlayerManager.Players[2]);
    }

    private static GameObject SpawnUnit(TestGame game, Player owner, Vector3 position)
    {
        var definition = new ObjectDefinition("A4CommanderPolicyUnit");
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        var obj = game.GameLogic.CreateObject(definition, owner);
        obj.UpdateTransform(position, null);
        return obj;
    }

    private static Squad BuildSquad(TestGame game, Player owner, int memberCount)
    {
        var squad = new Squad(1u);
        for (var i = 0; i < memberCount; i++)
        {
            squad.Members.Add(SpawnUnit(game, owner, new Vector3(i * 10f, 0, 0)));
        }

        squad.PromoteNextCommander();
        return squad;
    }

    [Fact]
    public void AttackDirectiveWithTarget_TargetsEveryMember()
    {
        var (game, player) = CreateGame();
        var squad = BuildSquad(game, player, memberCount: 3);
        var target = new ObjectId(42u);
        var policy = new RuleBasedCommanderDecisionPolicy();

        var goals = policy.DecideGoals(new CommanderDecisionContext(
            new CommanderDirective(DirectivePosture.Attack, TargetId: target),
            squad,
            2));

        Assert.Equal(3, goals.Count);
        Assert.All(goals.Values, g => Assert.Equal(target, g.TargetId));
    }

    [Fact]
    public void AttackDirectiveWithRegion_MovesEveryMember()
    {
        var (game, player) = CreateGame();
        var squad = BuildSquad(game, player, memberCount: 2);
        var region = new Vector3(500f, 500f, 0f);
        var policy = new RuleBasedCommanderDecisionPolicy();

        var goals = policy.DecideGoals(new CommanderDecisionContext(
            new CommanderDirective(DirectivePosture.Attack, TargetRegion: region),
            squad,
            2));

        Assert.All(goals.Values, g => Assert.Equal(region, g.Destination));
    }

    [Fact]
    public void DefendDirective_HoldsAtSquadCentroid()
    {
        var (game, player) = CreateGame();
        var squad = BuildSquad(game, player, memberCount: 2);
        var expectedCentroid = (squad.Members[0].Translation + squad.Members[1].Translation) / 2f;
        var policy = new RuleBasedCommanderDecisionPolicy();

        var goals = policy.DecideGoals(new CommanderDecisionContext(
            new CommanderDirective(DirectivePosture.Defend),
            squad,
            2));

        Assert.All(goals.Values, g =>
        {
            Assert.Equal(expectedCentroid, g.Destination);
            Assert.Equal(GoalStance.Hold, g.Stance);
        });
    }

    [Fact]
    public void NullDirective_HoldsAtCentroid()
    {
        var (game, player) = CreateGame();
        var squad = BuildSquad(game, player, memberCount: 1);
        var policy = new RuleBasedCommanderDecisionPolicy();

        var goals = policy.DecideGoals(new CommanderDecisionContext(null, squad, 2));

        Assert.Single(goals);
        Assert.Equal(GoalStance.Hold, goals.Values.Single().Stance);
    }

    [Fact]
    public void OversizedSquad_IsRejected()
    {
        var (game, player) = CreateGame();
        var squad = BuildSquad(game, player, memberCount: SquadHierarchy.MaxSquadSize + 1);
        var policy = new RuleBasedCommanderDecisionPolicy();

        Assert.Throws<System.InvalidOperationException>(
            () => policy.DecideGoals(new CommanderDecisionContext(null, squad, 2)));
    }

    [Fact]
    public void LiveMembersOnly_GoalKeysAreStableMemberIds()
    {
        var (game, player) = CreateGame();
        var squad = BuildSquad(game, player, memberCount: 3);
        var dead = squad.Members[1];
        game.GameLogic.DestroyObject(dead);
        squad.Members.Remove(dead);

        var policy = new RuleBasedCommanderDecisionPolicy();
        var goals = policy.DecideGoals(new CommanderDecisionContext(null, squad, 2));

        Assert.Equal(2, goals.Count);
        Assert.Equal(
            squad.Members.Select(m => m.Id.Index).OrderBy(id => id),
            goals.Keys.OrderBy(id => id));
    }
}
