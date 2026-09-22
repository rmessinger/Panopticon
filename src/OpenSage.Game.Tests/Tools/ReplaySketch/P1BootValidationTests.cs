// P1-4 boot validation — a human + AI skirmish boots headlessly with correct
// relationships, spawns, and LocalPlayer identification. Primary P1 regression gate.
// See WI-P1-06 and docs/roadmap/deep-dives/p1-skirmish-setup.md §5 (P1-4).
//
// Run with: dotnet test src/OpenSage.Game.Tests --filter "FullyQualifiedName~P1BootValidation"

using System;
using System.Collections.Generic;
using OpenSage;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class P1BootValidationTests
{
    public const string HumanPlayerIsLocalPredicate = "P1.HumanPlayerIsLocal";
    public const string RelationshipsAreSymmetricPredicate = "P1.RelationshipsAreSymmetric";
    public const string NoPlayerUnspawnedPredicate = "P1.NoPlayerUnspawned";

    static P1BootValidationTests()
    {
        CodeAssertionRegistry.Register(HumanPlayerIsLocalPredicate, HumanPlayerIsLocal);
        CodeAssertionRegistry.Register(RelationshipsAreSymmetricPredicate, RelationshipsAreSymmetric);
        CodeAssertionRegistry.Register(NoPlayerUnspawnedPredicate, NoPlayerUnspawned);
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void SkirmishBoot_HumanPlusOneAI_HasCorrectRelationshipsAndSpawns()
    {
        var scenario = BuildHumanPlusOneAIScenario(
        [
            new CodeAssertion { Id = "p1-local-player", PredicateName = HumanPlayerIsLocalPredicate },
            new CodeAssertion { Id = "p1-relationships", PredicateName = RelationshipsAreSymmetricPredicate },
            new CodeAssertion { Id = "p1-spawn", PredicateName = NoPlayerUnspawnedPredicate },
        ]);

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);

        if (report.Results.Count == 0)
        {
            return; // No game installation available on this machine/CI — skip.
        }

        Assert.True(report.AllPassed, report.ToJson());
    }

    /// <summary>
    /// Two slots on opposing teams (USA team 1 vs GLA team 2) so OnNewGame wires them
    /// as mutual enemies, exercising the reciprocal ally/enemy wiring from P1-2/P1-3.
    /// </summary>
    internal static ReplayScenario BuildHumanPlusOneAIScenario(
        IReadOnlyList<ScenarioAssertion> assertions)
    {
        var scenario = new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
        };

        scenario.Players.Add(new PlayerSlotConfig
        {
            Name = "P1",
            FactionIndex = 2, // USA
            Color = 1,
            StartPosition = 1,
            Team = 1,
        });
        scenario.Players.Add(new PlayerSlotConfig
        {
            Name = "P2",
            FactionIndex = 4, // GLA
            Color = 2,
            StartPosition = 2,
            Team = 2,
        });

        foreach (var assertion in assertions)
        {
            scenario.Assertions.Add(assertion);
        }

        return scenario;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Predicates registered with CodeAssertionRegistry. Internal so the predicate
    // logic is unit-tested headlessly (the full-harness run needs a game install).
    // ──────────────────────────────────────────────────────────────────────────

    internal static bool HumanPlayerIsLocal(IGame game, uint frame)
    {
        var local = game.PlayerManager.LocalPlayer;
        if (local is null)
        {
            return false;
        }

        var isParticipant = false;
        foreach (var player in Participants(game))
        {
            if (ReferenceEquals(player, local))
            {
                isParticipant = true;
                break;
            }
        }

        // A human local player reports IsHuman; neutral/civilian slots are excluded by
        // the participant check. This is the regression surface for LocalPlayer landing on
        // the wrong slot (neutral, civilian, or an AI player).
        return isParticipant && local.IsHuman;
    }

    internal static bool RelationshipsAreSymmetric(IGame game, uint frame)
    {
        var participants = Participants(game);
        for (var i = 0; i < participants.Count; i++)
        {
            for (var j = 0; j < participants.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var a = participants[i];
                var b = participants[j];
                if (RelationshipBetween(a, b) != RelationshipBetween(b, a))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static bool NoPlayerUnspawned(IGame game, uint frame)
    {
        foreach (var player in Participants(game))
        {
            if (player.OwnedObjects.Count == 0)
            {
                return false;
            }
        }

        return true;
    }

    // Skirmish participants are every player except the neutral and civilian slots
    // (indices 0 and 1 in the engine's player list).
    private static List<Player> Participants(IGame game)
    {
        var neutral = game.PlayerManager.NeutralPlayer;
        var civilian = game.PlayerManager.GetCivilianPlayer();
        var result = new List<Player>();
        foreach (var player in game.PlayerManager.Players)
        {
            if (ReferenceEquals(player, neutral) || ReferenceEquals(player, civilian))
            {
                continue;
            }

            result.Add(player);
        }

        return result;
    }

    private static RelationshipType RelationshipBetween(Player a, Player b)
    {
        if (a.Allies.Contains(b))
        {
            return RelationshipType.Allies;
        }

        if (a.Enemies.Contains(b))
        {
            return RelationshipType.Enemies;
        }

        return RelationshipType.Neutral;
    }
}

// CI-runnable checks of the predicate logic against a mocked game. These prove the
// predicates detect correct and broken skirmish setups without a game installation.
public class P1BootValidationPredicateTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public void Predicates_CorrectOneVOneSetup_AllPass()
    {
        var (human, ai) = BootTwoPlayers(humanTeam: 1, aiTeam: 2);
        GiveStartingObject(human);
        GiveStartingObject(ai);

        Assert.True(P1BootValidationTests.HumanPlayerIsLocal(Generals, 0),
            "LocalPlayer should be the human participant.");
        Assert.True(P1BootValidationTests.RelationshipsAreSymmetric(Generals, 0),
            "Reciprocal wiring should produce symmetric relationships.");
        Assert.True(P1BootValidationTests.NoPlayerUnspawned(Generals, 0),
            "Every participant should own a starting object.");
    }

    [Fact, Trait("Category", "Roadmap")]
    public void HumanPlayerIsLocal_FailsWhenLocalIsAnAiPlayer()
    {
        // Boot with only AI slots: LocalPlayer falls back to the first skirmish
        // player, which is AI-controlled, so the predicate must reject it.
        BootPlayers(
            human: false,
            settings: [new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), 1, PlayerOwner.EasyAi, "ai1")]);

        Assert.False(P1BootValidationTests.HumanPlayerIsLocal(Generals, 0));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void RelationshipsAreSymmetric_FailsWhenWiringIsNotReciprocal()
    {
        var (human, ai) = BootTwoPlayers(humanTeam: 1, aiTeam: 2);
        // Break reciprocity: human considers ai an enemy, ai does not reciprocate.
        human.Enemies.Add(ai);
        ai.Enemies.Remove(human);

        Assert.False(P1BootValidationTests.RelationshipsAreSymmetric(Generals, 0));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void NoPlayerUnspawned_FailsWhenAPlayerHasNoObjects()
    {
        var (human, ai) = BootTwoPlayers(humanTeam: 1, aiTeam: 2);
        GiveStartingObject(human);
        // ai never received a starting object.

        Assert.False(P1BootValidationTests.NoPlayerUnspawned(Generals, 0));
    }

    private (Player Human, Player Ai) BootTwoPlayers(int humanTeam, int aiTeam)
    {
        var mapPlayers = TwoPlayerMapPlayers();
        var settings = new PlayerSetting[]
        {
            new(1, "FactionAmerica", new ColorRgb(255, 255, 255), humanTeam, PlayerOwner.Player, "human", isLocalForMultiplayer: true),
            new(2, "FactionGLA", new ColorRgb(255, 128, 64), aiTeam, PlayerOwner.EasyAi, "ai"),
        };
        Generals.PlayerManager.OnNewGame(mapPlayers, GameType.Skirmish, settings);
        return (Generals.PlayerManager.Players[2], Generals.PlayerManager.Players[3]);
    }

    private void BootPlayers(bool human, PlayerSetting[] settings)
    {
        var mapPlayers = new List<OpenSage.Data.Map.Player>
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
        };
        foreach (var setting in settings)
        {
            mapPlayers.Add(new OpenSage.Data.Map.Player
            {
                Name = setting.Name,
                Faction = setting.SideName,
                DisplayName = setting.Name,
                IsHuman = human,
            });
        }

        Generals.PlayerManager.OnNewGame(mapPlayers.ToArray(), GameType.Skirmish, settings);
    }

    private static OpenSage.Data.Map.Player[] TwoPlayerMapPlayers() =>
    [
        OpenSage.Data.Map.Player.CreateNeutralPlayer(),
        OpenSage.Data.Map.Player.CreateCivilianPlayer(),
        new OpenSage.Data.Map.Player
        {
            Name = "human",
            Faction = "FactionAmerica",
            DisplayName = "Human",
            IsHuman = true,
        },
        new OpenSage.Data.Map.Player
        {
            Name = "ai",
            Faction = "FactionGLA",
            DisplayName = "AI",
            IsHuman = false,
        },
    ];

    private static GameObject CreateStartingBuilding(MockedGameTest.TestGame game, Player owner)
    {
        var definition = new ObjectDefinition("P1BootValidationStart");
        return new GameObject(definition, game.GameEngine, owner);
    }

    private void GiveStartingObject(Player owner)
    {
        owner.AddOwnedObject(CreateStartingBuilding(Generals, owner));
    }
}
