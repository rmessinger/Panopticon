// WI-M4-05: the air commander rank. Pins that aircraft form their own air squads separate
// from the ground hierarchy, that the reference rotation assigns all three missions
// deterministically, that missions reach only parked-and-rearmed squads (mid-sortie squads
// hold their state), and that per-mission and per-unit assignment counts track every
// assignment for the probe report.
//
// Run with: dotnet test src --filter "FullyQualifiedName~AirCommanderTests"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Panopticon.Content;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Tests.Logic.AI.Hierarchy;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

public class AirCommanderTests : MockedGameTest
{
    [Fact]
    public void Aircraft_FormAirSquads_SeparateFromGround()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        _ = HierarchyTestPlayers.SpawnUnit(game, player, "Ground1", 100f);
        _ = HierarchyTestPlayers.SpawnUnit(game, player, "Ground2", 100f);
        _ = HierarchyTestPlayers.SpawnAircraft(game, player, "Jet1");
        _ = HierarchyTestPlayers.SpawnAircraft(game, player, "Jet2");
        var policy = NewPolicy();

        _ = policy.DecideNextOrders(new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: 1));

        var groundSquad = Assert.Single(policy.Hierarchy.Squads);
        Assert.Equal(2, groundSquad.Members.Count);
        var airSquad = Assert.Single(policy.AirHierarchy.Squads);
        Assert.Equal(2, airSquad.Members.Count);
        Assert.All(airSquad.Members, member => Assert.True(member.Definition.KindOf.Get(ObjectKinds.Aircraft)));
        Assert.Null(policy.AirHierarchy.SquadOf(groundSquad.Members[0]));
        Assert.Null(policy.Hierarchy.SquadOf(airSquad.Members[0]));
    }

    [Fact]
    public void Rotation_AssignsAllThreeMissionsDeterministically()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        _ = HierarchyTestPlayers.SpawnAircraft(game, player, "Jet1");
        var policy = NewPolicy();

        // First tick forms the air squad; the commander then sees it.
        _ = policy.DecideNextOrders(new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: 1));
        var commander = new RuleBasedAirCommanderDecisionPolicy();
        var expected = new[]
        {
            AirMission.Strike, AirMission.Strike, AirMission.Strike,
            AirMission.Patrol, AirMission.Patrol, AirMission.Patrol,
            AirMission.Escort, AirMission.Escort, AirMission.Escort,
        };
        for (var tick = 1; tick <= expected.Length; tick++)
        {
            var missions = commander.DecideMissions(new AirCommanderContext(
                new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: tick),
                policy.AirHierarchy.Squads,
                [],
                Directive: null));
            var mission = Assert.Single(missions);
            Assert.Equal(expected[tick - 1], mission.Mission);
        }
    }

    [Fact]
    public void ParkedSquad_ReceivesMissionOrder_MidSortieSquadSkipped()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var jet = SpawnJet(game, player, "Jet1");
        var destination = new Vector3(50f, 0f, 50f);
        var policy = new HierarchicalAIDecisionPolicy(
            new RuleBasedGeneralPolicy(),
            new RuleBasedCommanderDecisionPolicy(),
            new RuleBasedSoldierDecisionPolicy(),
            new FixedMissionAirCommander(AirMission.Patrol, destination));

        var jetAi = (JetAIUpdate)jet.AIUpdate!;
        jetAi.CurrentJetAIState = JetAIUpdate.JetAIState.Parked;
        var orders = policy.DecideNextOrders(new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: 1))
            .ToList();

        Assert.Equal(2, orders.Count);
        Assert.Equal(OrderType.SetSelection, orders[0].OrderType);
        Assert.Equal(OrderType.MoveTo, orders[1].OrderType);

        // Mid-sortie: the assignment still counts, but no order interrupts the jet.
        jetAi.CurrentJetAIState = JetAIUpdate.JetAIState.MovingTowardsTarget;
        orders = policy.DecideNextOrders(new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: 2))
            .ToList();
        Assert.Empty(orders);
        Assert.Equal(2, policy.AirMissionAssignmentCount(AirMission.Patrol));
        Assert.Equal(0, policy.AirMissionAssignmentCount(AirMission.Strike));
        Assert.Equal(0, policy.AirMissionAssignmentCount(AirMission.Escort));
        Assert.Equal(2, policy.AirMissionAssignmentCountForUnit(jet.Id));
    }

    [Fact]
    public void ParkedSquadWithSpentClip_IsStillRetasked_MatchingM1Semantics()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var jet = SpawnJet(game, player, "Jet1", clipSize: 2);
        var destination = new Vector3(50f, 0f, 50f);
        var policy = new HierarchicalAIDecisionPolicy(
            new RuleBasedGeneralPolicy(),
            new RuleBasedCommanderDecisionPolicy(),
            new RuleBasedSoldierDecisionPolicy(),
            new FixedMissionAirCommander(AirMission.Patrol, destination));

        // The engine refills clips only through the weapon's reload cycle (ReloadingWeaponState),
        // never on park; the M1 scripted re-issue relaunched parked jets on state alone. The
        // air commander matches that: Parked gates the re-task, not the clip.
        var jetAi = (JetAIUpdate)jet.AIUpdate!;
        var weapon = jet.CurrentWeapon!;
        Assert.True(weapon.UsesClip);
        jetAi.CurrentJetAIState = JetAIUpdate.JetAIState.Parked;
        weapon.CurrentRounds = 0;
        var orders = policy.DecideNextOrders(new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: 1))
            .ToList();

        Assert.Equal(2, orders.Count);
        Assert.Equal(OrderType.SetSelection, orders[0].OrderType);
        Assert.Equal(OrderType.MoveTo, orders[1].OrderType);
    }

    private static HierarchicalAIDecisionPolicy NewPolicy() => new(
        new RuleBasedGeneralPolicy(),
        new RuleBasedCommanderDecisionPolicy(),
        new RuleBasedSoldierDecisionPolicy());

    private static GameObject SpawnJet(MockedGameTest.TestGame game, Player owner, string name, int clipSize = 0)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Aircraft, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        SetProperty(definition, nameof(ObjectDefinition.BuildCost), 100f);
        definition.Behaviors.Add(
            "ModuleTag_Body",
            new ModuleDataContainer(
                "ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default,
                inherited: false));
        definition.Behaviors.Add(
            "ModuleTag_AI",
            new ModuleDataContainer(
                "ModuleTag_AI",
                new JetAIUpdateModuleData(),
                ModuleInheritanceMode.Default,
                inherited: false));
        if (clipSize > 0)
        {
            // A clip weapon through the real weapon-set path, so CurrentWeapon materializes
            // exactly as it does for INI-authored units (CreateObject builds weapons from
            // the definition's weapon sets).
            var weaponSet = new WeaponTemplateSet();
            weaponSet.Slots[(int)WeaponSlot.Primary] = new WeaponSetSlot
            {
                Weapon = new LazyAssetReference<WeaponTemplate>(new WeaponTemplate { ClipSize = clipSize }),
            };
            definition.WeaponSets.Add(new BitArray<WeaponSetConditions>(), weaponSet);
        }

        return game.GameLogic.CreateObject(definition, owner);
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private sealed class FixedMissionAirCommander(AirMission mission, Vector3 destination)
        : IAirCommanderDecisionPolicy
    {
        public IReadOnlyList<AirSquadMission> DecideMissions(in AirCommanderContext context) =>
        [
            .. context.AirSquads.Select(squad => new AirSquadMission(
                squad.Id, mission, StrikeTargetId: null, Destination: destination)),
        ];
    }
}
