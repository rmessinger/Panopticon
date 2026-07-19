// Track P scenario matrix stubs — one class per phase.
// Each class verifies that the phase's ScenarioMatrix (defined in the phase's deep dive matrix
// section) expands and runs correctly against the P0-5b runner. These are STUBS: each test throws
// NotImplementedException until the corresponding phase and P0-5b are implemented.
//
// Run with: dotnet test src/OpenSage.Game.Tests --filter "FullyQualifiedName~ScenarioMatrix"
//
// See: docs/roadmap/deep-dives/playable-baseline-harness.md §3.5b for the ScenarioMatrix model.

using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using OpenSage.Tests;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// ── P1: Skirmish setup ─────────────────────────────────────────────────────────
// Matrix: playerCount × teamConfig × factionMix
// Validates relationship and faction assignment across the full config space.
// See docs/roadmap/deep-dives/p1-skirmish-setup.md §7 (Scenario Matrix).

public class P1_ScenarioMatrixTests : MockedGameTest
{
    private const string Ffa = "FFA";
    private const string TwoVTwo = "2v2";
    private const string ThreeVOne = "3v1";
    private const string FourVFour = "4v4";

    private const string Mirror = "mirror";
    private const string Mixed = "mixed";
    private const string AllFactions = "all-factions";

    // Generals playable factions; all-factions cells are constrained to playerCount <= this length.
    private static readonly string[] FactionSides = ["FactionAmerica", "FactionGLA", "FactionChina"];

    [Fact, Trait("Category", "Roadmap")]
    public void Matrix_AllCells_BootWithCorrectRelationships()
    {
        var matrix = BuildP1Matrix();
        var cells = matrix.Expand();

        // 60-cell cartesian space pruned to 26 valid cells by the constraint filter.
        Assert.Equal(26, cells.Count);

        var results = new List<CellResult>(cells.Count);
        foreach (var cell in cells)
        {
            var game = new TestGame(SageGame.CncGenerals);
            var setup = ConfigureScenario(game, cell.Bindings);
            results.Add(new CellResult(cell.Bindings, new AssertionReport(BuildAssertions(cell.Bindings, setup))));
        }

        var report = new MatrixReport(results);
        Assert.True(report.AllPassed, report.ToJson());
        Assert.Contains("\"failed\":0", report.ToJson(), StringComparison.Ordinal);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Matrix_InvalidCombinations_PrunedByConstraintFilter()
    {
        var matrix = BuildP1Matrix();
        var all = ExpandAllBindings(matrix.Axes);

        // The unfiltered cartesian product includes combinations that violate the constraints.
        Assert.Equal(60, all.Count);
        Assert.Contains(all, b => b["teamConfig"] == TwoVTwo && b["playerCount"] == "2");
        Assert.Contains(all, b => b["teamConfig"] == FourVFour && b["playerCount"] == "4");
        Assert.Contains(all, b => b["factionMix"] == AllFactions && b["playerCount"] == "4");

        var cells = matrix.Expand();

        // Pruned: 2v2/3v1 need >= 4 players, 4v4 needs exactly 8, all-factions needs <= 3 factions.
        Assert.DoesNotContain(cells, c => c.Bindings["teamConfig"] == TwoVTwo && int.Parse(c.Bindings["playerCount"]) < 4);
        Assert.DoesNotContain(cells, c => c.Bindings["teamConfig"] == ThreeVOne && int.Parse(c.Bindings["playerCount"]) < 4);
        Assert.DoesNotContain(cells, c => c.Bindings["teamConfig"] == FourVFour && c.Bindings["playerCount"] != "8");
        Assert.DoesNotContain(cells, c => c.Bindings["factionMix"] == AllFactions && int.Parse(c.Bindings["playerCount"]) > FactionSides.Length);
    }

    private static ScenarioMatrix BuildP1Matrix()
    {
        return new ScenarioMatrix
        {
            Axes =
            [
                new SweepAxis { Name = "playerCount", Values = ["2", "3", "4", "6", "8"] },
                new SweepAxis { Name = "teamConfig", Values = [Ffa, TwoVTwo, ThreeVOne, FourVFour] },
                new SweepAxis { Name = "factionMix", Values = [Mirror, Mixed, AllFactions] },
            ],
            Filters =
            [
                new ConstraintFilter(
                    "teamConfig fits playerCount and factionMix fits the faction count",
                    IsCellAllowed),
            ],
        };
    }

    // 2v2/3v1 need at least 4 players, 4v4 needs exactly 8, and all-factions needs one distinct
    // faction per slot (so playerCount <= FactionSides.Length). FFA and mirror/mixed always fit.
    private static bool IsCellAllowed(IReadOnlyDictionary<string, string> bindings)
    {
        var playerCount = int.Parse(bindings["playerCount"]);
        var teamOk = bindings["teamConfig"] switch
        {
            TwoVTwo => playerCount >= 4,
            ThreeVOne => playerCount >= 4,
            FourVFour => playerCount == 8,
            _ => true,
        };
        var factionOk = bindings["factionMix"] != AllFactions || playerCount <= FactionSides.Length;
        return teamOk && factionOk;
    }

    private static ScenarioSetup ConfigureScenario(TestGame game, IReadOnlyDictionary<string, string> bindings)
    {
        var playerCount = int.Parse(bindings["playerCount"]);
        var teams = AssignTeams(playerCount, bindings["teamConfig"]);
        var factionMix = bindings["factionMix"];

        var mapPlayers = new List<OpenSage.Data.Map.Player>
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
        };

        var settings = new PlayerSetting[playerCount];
        for (var i = 0; i < playerCount; i++)
        {
            var faction = FactionFor(i, factionMix);
            mapPlayers.Add(new OpenSage.Data.Map.Player
            {
                Name = $"player{i}",
                Faction = faction,
                DisplayName = $"Player {i}",
                IsHuman = i == 0,
            });

            settings[i] = new PlayerSetting(
                startPosition: i + 1,
                sideName: faction,
                color: new ColorRgb((byte)(200 - i * 20), (byte)(100 + i * 10), (byte)(50 + i * 5)),
                team: teams[i],
                owner: i == 0 ? PlayerOwner.Player : PlayerOwner.EasyAi,
                name: $"player{i}",
                isLocalForMultiplayer: i == 0);
        }

        game.PlayerManager.OnNewGame(mapPlayers.ToArray(), GameType.Skirmish, settings);

        var all = game.PlayerManager.Players;
        var nonParticipants = all.Take(2).ToList();
        var participants = all.Skip(2).Take(playerCount).ToList();
        return new ScenarioSetup(participants, nonParticipants, teams, factionMix);
    }

    // FFA leaves every player on its own team. The paired configs seat the first team, then the
    // second, then any overflow onto solo teams so 2v2/3v1 stay valid for playerCount > 4.
    private static int[] AssignTeams(int playerCount, string teamConfig)
    {
        var teams = new int[playerCount];
        for (var i = 0; i < playerCount; i++)
        {
            teams[i] = i;
        }

        var firstTeamSize = teamConfig switch
        {
            TwoVTwo => 2,
            ThreeVOne => 3,
            FourVFour => 4,
            _ => 0,
        };

        if (firstTeamSize == 0)
        {
            return teams;
        }

        for (var i = 0; i < playerCount; i++)
        {
            teams[i] = i < firstTeamSize ? 0 : (i < 2 * firstTeamSize ? 1 : i);
        }

        return teams;
    }

    private static string FactionFor(int index, string factionMix)
    {
        return factionMix switch
        {
            Mirror => FactionSides[0],
            Mixed => FactionSides[index % 2],
            AllFactions => FactionSides[index],
            _ => throw new InvalidOperationException($"Unsupported factionMix '{factionMix}'."),
        };
    }

    private static IReadOnlyList<AssertionResult> BuildAssertions(
        IReadOnlyDictionary<string, string> bindings,
        ScenarioSetup setup)
    {
        var results = new List<AssertionResult>();
        var participants = setup.Participants;
        var cellKey = $"{bindings["playerCount"]}-{bindings["teamConfig"]}-{bindings["factionMix"]}";

        for (var i = 0; i < participants.Count; i++)
        {
            for (var j = 0; j < participants.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }
                results.Add(AssertRelationship(cellKey, i, j, participants, setup.Teams));
            }
        }

        for (var i = 0; i < participants.Count; i++)
        {
            var expected = FactionFor(i, setup.FactionMix);
            var actual = participants[i].Side;
            results.Add(new AssertionResult
            {
                AssertionId = $"p1-faction-{cellKey}-{i}",
                Passed = actual == expected,
                ActualValue = actual,
                FailFrame = 0,
                Message = actual == expected ? null : $"Player {i} faction '{actual}', expected '{expected}' for cell {cellKey}.",
            });
        }

        // Neutral and civilian slots must remain neutral to every participant (not wired as ally/enemy).
        for (var n = 0; n < setup.NonParticipants.Count; n++)
        {
            var nonParticipant = setup.NonParticipants[n];
            for (var i = 0; i < participants.Count; i++)
            {
                var participant = participants[i];
                var label = $"{RelationshipLabel(nonParticipant, participant)}/{RelationshipLabel(participant, nonParticipant)}";
                results.Add(new AssertionResult
                {
                    AssertionId = $"p1-neutral-{cellKey}-{n}-{i}",
                    Passed = label == "Neutral/Neutral",
                    ActualValue = label,
                    FailFrame = 0,
                    Message = label == "Neutral/Neutral" ? null : $"Non-participant '{nonParticipant.Name}' should be neutral to player {i} for cell {cellKey}, got {label}.",
                });
            }
        }

        return results;
    }

    private static AssertionResult AssertRelationship(
        string cellKey, int i, int j, IReadOnlyList<Player> participants, int[] teams)
    {
        var a = participants[i];
        var b = participants[j];
        var sameTeam = teams[i] == teams[j];
        var actual = $"{RelationshipLabel(a, b)}/{RelationshipLabel(b, a)}";
        var expected = sameTeam ? "Allies/Allies" : "Enemies/Enemies";

        bool passed;
        if (sameTeam)
        {
            passed = a.Allies.Contains(b) && b.Allies.Contains(a)
                     && !a.Enemies.Contains(b) && !b.Enemies.Contains(a);
        }
        else
        {
            passed = a.Enemies.Contains(b) && b.Enemies.Contains(a)
                     && !a.Allies.Contains(b) && !b.Allies.Contains(a);
        }

        return new AssertionResult
        {
            AssertionId = $"p1-rel-{cellKey}-{i}-{j}",
            Passed = passed,
            ActualValue = actual,
            FailFrame = 0,
            Message = passed ? null : $"Pair ({i},{j}) expected {expected}, got {actual} for cell {cellKey} (team {teams[i]} vs team {teams[j]}).",
        };
    }

    private static string RelationshipLabel(Player a, Player b)
    {
        if (a.Allies.Contains(b))
        {
            return "Allies";
        }
        if (a.Enemies.Contains(b))
        {
            return "Enemies";
        }
        return "Neutral";
    }

    // Replicates ScenarioMatrix.Expand() without applying filters, so the pruned-combinations
    // test can assert against the full cartesian product.
    private static List<Dictionary<string, string>> ExpandAllBindings(IReadOnlyList<SweepAxis> axes)
    {
        var result = new List<Dictionary<string, string>> { new() };
        foreach (var axis in axes)
        {
            var next = new List<Dictionary<string, string>>();
            foreach (var binding in result)
            {
                foreach (var value in axis.Values)
                {
                    next.Add(new Dictionary<string, string>(binding) { [axis.Name] = value });
                }
            }
            result = next;
        }
        return result;
    }

    private sealed record ScenarioSetup(
        IReadOnlyList<Player> Participants,
        IReadOnlyList<Player> NonParticipants,
        int[] Teams,
        string FactionMix);
}

// ── P2: Skirmish AI ───────────────────────────────────────────────────────────
// Matrix: humanFaction × aiFaction × mapSize
// Validates the AI plays a legal game for every faction and map combination.

public class P2_ScenarioMatrixTests
{
    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_AllCells_AIPlaysLegalGame()
    {
        // Arrange: ScenarioMatrix with axes humanFaction × aiFaction ∈ {USA, GLA, China},
        //          mapSize ∈ {small, medium} — 18 cells.
        // Act: expand and run all cells for a fixed frame count via P0-5b.
        // Assert: every cell asserts AI built >= 1 structure, produced >= 1 unit,
        //         issued >= 1 attack order, and kept BankAccount >= 0 throughout.

    }

    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_MirrorFactionCells_AIStillFunctional()
    {
        // Arrange: cells where humanFaction == aiFaction (mirror match).
        // Act: run those cells.
        // Assert: AI behavior assertions pass even in same-faction mirror matches.

    }
}

// ── P3: Victory & defeat ──────────────────────────────────────────────────────
// Matrix: endState × playerCount
// Validates the correct GameOutcome for every termination path and match size.

public class P3_ScenarioMatrixTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public void Matrix_AllCells_ProduceCorrectGameOutcome()
    {
        var matrix = BuildP3Matrix();
        var cells = matrix.Expand();

        Assert.Equal(17, cells.Count);
        Assert.DoesNotContain(cells, cell => cell.Bindings["endState"] == TeamWinEndState && cell.Bindings["playerCount"] == "2");

        var results = new List<CellResult>(cells.Count);
        foreach (var cell in cells)
        {
            var execution = ExecuteCell(cell.Bindings);
            results.Add(new CellResult(cell.Bindings, new AssertionReport(BuildFinalAssertions(cell.Bindings, execution))));
        }

        var report = new MatrixReport(results);
        Assert.True(report.AllPassed, report.ToJson());
        Assert.Contains("\"failed\":0", report.ToJson(), StringComparison.Ordinal);

    }

    [Fact, Trait("Category", "Roadmap")]
    public void Matrix_SuddenDeath_ContinuesUntilNextElimination()
    {
        var matrix = BuildP3Matrix();
        var cells = matrix.Expand().Where(cell => cell.Bindings["endState"] == MutualEliminationEndState).ToList();

        Assert.Equal(3, cells.Count);

        var results = new List<CellResult>(cells.Count);
        foreach (var cell in cells)
        {
            var execution = ExecuteCell(cell.Bindings);
            results.Add(new CellResult(cell.Bindings, new AssertionReport(BuildSuddenDeathAssertions(execution))));
        }

        var report = new MatrixReport(results);
        Assert.True(report.AllPassed, report.ToJson());
    }

    private ScenarioMatrix BuildP3Matrix()
    {
        return new ScenarioMatrix
        {
            Axes =
            [
                new SweepAxis
                {
                    Name = "endState",
                    Values =
                    [
                        PlayerEliminatedEndState,
                        PlayerSurrendersEndState,
                        AiSurrendersEndState,
                        TeamWinEndState,
                        MutualEliminationEndState,
                        ScoreDecisionEndState,
                    ],
                },
                new SweepAxis
                {
                    Name = "playerCount",
                    Values = ["2", "3", "4"],
                },
            ],
            Filters =
            [
                new ConstraintFilter(
                    "endState != TeamWin || playerCount != 2",
                    static bindings => bindings["endState"] != TeamWinEndState || bindings["playerCount"] != "2"),
            ],
        };
    }

    private CellExecution ExecuteCell(IReadOnlyDictionary<string, string> bindings)
    {
        var playerCount = int.Parse(bindings["playerCount"]);
        var endState = bindings["endState"];
        var game = new TestGame(SageGame.CncGenerals);
        var scenario = ConfigureScenario(game, endState, playerCount);
        var cadence = (int)VictorySystem.DefaultCheckCadence.Value;
        var suddenDeathObserved = false;

        switch (endState)
        {
            case PlayerEliminatedEndState:
                for (var i = 1; i < scenario.Players.Count; i++)
                {
                    scenario.CommandCenters[i].Destroy();
                    RunVictoryCadence(game);
                }
                break;

            case PlayerSurrendersEndState:
                new OrderProcessor(game).Process([new Order(2, OrderType.EndGame)]);
                break;

            case AiSurrendersEndState:
                new OrderProcessor(game).Process([new Order(3, OrderType.EndGame)]);
                break;

            case TeamWinEndState:
                for (var i = 2; i < scenario.Players.Count; i++)
                {
                    scenario.CommandCenters[i].Destroy();
                    RunVictoryCadence(game);
                }
                break;

            case MutualEliminationEndState:
                foreach (var commandCenter in scenario.CommandCenters)
                {
                    commandCenter.Destroy();
                }

                RunVictoryCadence(game);
                suddenDeathObserved = game.CurrentOutcome.Status == OutcomeStatus.SuddenDeath;

                for (var i = 1; i < scenario.Units.Count; i++)
                {
                    scenario.Units[i].Destroy();
                    RunVictoryCadence(game);
                }
                break;

            case ScoreDecisionEndState:
                game.VictorySystem.FrameLimit = cadence;
                RunVictoryCadence(game);
                break;

            default:
                throw new InvalidOperationException($"Unsupported end state '{endState}'.");
        }

        return new CellExecution(
            game,
            endState,
            playerCount,
            game.CurrentOutcome.Status,
            game.CurrentOutcome.Reason,
            game.CurrentOutcome.WinningPlayers.Select(game.PlayerManager.GetPlayerIndex).OrderBy(index => index).ToArray(),
            game.CurrentOutcome.LosingPlayer != null ? game.PlayerManager.GetPlayerIndex(game.CurrentOutcome.LosingPlayer) : null,
            game.CurrentOutcome.DecidedOnFrame,
            suddenDeathObserved,
            game.IsLogicRunning);
    }

    private ScenarioSetup ConfigureScenario(TestGame game, string endState, int playerCount)
    {
        var teamConfiguration = endState == TeamWinEndState
            ? GetTeamWinConfiguration(playerCount)
            : Enumerable.Repeat(-1, playerCount).ToArray();

        var mapPlayers = new List<OpenSage.Data.Map.Player>
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
        };
        var playerSettings = new PlayerSetting[playerCount];

        for (var i = 0; i < playerCount; i++)
        {
            var faction = i % 2 == 0 ? "FactionAmerica" : "FactionGLA";
            mapPlayers.Add(new OpenSage.Data.Map.Player
            {
                Name = $"player{i + 1}",
                Faction = faction,
                DisplayName = $"Player {i + 1}",
                IsHuman = i == 0,
            });

            playerSettings[i] = new PlayerSetting(
                i + 1,
                faction,
                new ColorRgb((byte)(255 - (i * 20)), (byte)(180 - (i * 20)), (byte)(120 - (i * 10))),
                teamConfiguration[i],
                i == 0 ? PlayerOwner.Player : PlayerOwner.EasyAi,
                $"player{i + 1}",
                i == 0);
        }

        game.PlayerManager.OnNewGame(mapPlayers.ToArray(), GameType.Skirmish, playerSettings);
        game.IsLogicRunning = true;

        var players = new List<Player>(playerCount);
        var commandCenters = new List<GameObject>(playerCount);
        var units = new List<GameObject>(playerCount);

        for (var i = 0; i < playerCount; i++)
        {
            var player = game.PlayerManager.Players[i + 2];
            players.Add(player);

            var commandCenter = CreateGameObject(game, CreateCommandCenterDefinition(), player);
            player.AddOwnedObject(commandCenter);
            commandCenters.Add(commandCenter);

            if (endState == MutualEliminationEndState)
            {
                var unit = CreateGameObject(game, CreateUnitDefinition(), player);
                player.AddOwnedObject(unit);
                units.Add(unit);
            }
        }

        if (endState == ScoreDecisionEndState)
        {
            var extraStructure = CreateGameObject(game, CreateStructureDefinition(), players[0]);
            players[0].AddOwnedObject(extraStructure);
        }

        return new ScenarioSetup(players, commandCenters, units);
    }

    private static IReadOnlyList<AssertionResult> BuildFinalAssertions(
        IReadOnlyDictionary<string, string> bindings,
        CellExecution execution)
    {
        var expected = GetExpectedOutcome(bindings["endState"], execution.PlayerCount);
        var outcomeAssertion = new OutcomeAssertion
        {
            Id = $"p3-outcome-{bindings["endState"]}-{bindings["playerCount"]}",
            ExpectedStatus = OutcomeStatus.Decided.ToString(),
            ExpectedReason = expected.Reason.ToString(),
            ExpectedLosingPlayerIndex = expected.LosingPlayerIndex,
            ExpectedDecidedFrame = expected.DecidedFrame,
        };

        if (expected.WinningPlayerIndices.Count == 1)
        {
            outcomeAssertion.ExpectedWinningPlayerIndex = expected.WinningPlayerIndices[0];
        }

        var results = new List<AssertionResult>
        {
            outcomeAssertion.Evaluate(execution.Game, execution.DecidedFrame.HasValue ? (uint) execution.DecidedFrame.Value : 0),
            CreateWinnerSetAssertion(bindings, execution.WinningPlayerIndices, expected.WinningPlayerIndices),
            CreateLogicStoppedAssertion(bindings, execution.IsLogicRunning),
        };

        if (!expected.LosingPlayerIndex.HasValue)
        {
            results.Add(new AssertionResult
            {
                AssertionId = $"p3-loser-none-{bindings["endState"]}-{bindings["playerCount"]}",
                Passed = execution.LosingPlayerIndex == null,
                ActualValue = execution.LosingPlayerIndex?.ToString(),
                FailFrame = execution.DecidedFrame.HasValue ? (uint)execution.DecidedFrame.Value : 0,
                Message = execution.LosingPlayerIndex == null
                    ? null
                    : $"Expected no losing player for cell {bindings["endState"]}/{bindings["playerCount"]}, actual '{execution.LosingPlayerIndex.Value}'.",
            });
        }

        return results;
    }

    private static IReadOnlyList<AssertionResult> BuildSuddenDeathAssertions(CellExecution execution)
    {
        return
        [
            new AssertionResult
            {
                AssertionId = $"p3-suddendeath-observed-{execution.PlayerCount}",
                Passed = execution.SuddenDeathObserved,
                ActualValue = execution.SuddenDeathObserved.ToString(),
                FailFrame = 0,
                Message = execution.SuddenDeathObserved ? null : $"Expected sudden death before resolution for playerCount={execution.PlayerCount}.",
            },
            new AssertionResult
            {
                AssertionId = $"p3-suddendeath-final-{execution.PlayerCount}",
                Passed = execution.FinalStatus == OutcomeStatus.Decided && execution.FinalReason == OutcomeReason.Eliminated,
                ActualValue = $"{execution.FinalStatus}/{execution.FinalReason}",
                FailFrame = execution.DecidedFrame.HasValue ? (uint) execution.DecidedFrame.Value : 0,
                Message = execution.FinalStatus == OutcomeStatus.Decided && execution.FinalReason == OutcomeReason.Eliminated
                    ? null
                    : $"Expected sudden death to resolve to Decided/Eliminated for playerCount={execution.PlayerCount}, actual {execution.FinalStatus}/{execution.FinalReason}.",
            },
        ];
    }

    private static AssertionResult CreateWinnerSetAssertion(
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlyList<int> actualWinners,
        IReadOnlyList<int> expectedWinners)
    {
        var actual = string.Join(",", actualWinners);
        var expected = string.Join(",", expectedWinners);
        return new AssertionResult
        {
            AssertionId = $"p3-winners-{bindings["endState"]}-{bindings["playerCount"]}",
            Passed = actual == expected,
            ActualValue = actual,
            FailFrame = 0,
            Message = actual == expected
                ? null
                : $"Expected winners [{expected}] for cell {bindings["endState"]}/{bindings["playerCount"]}, actual [{actual}].",
        };
    }

    private static AssertionResult CreateLogicStoppedAssertion(IReadOnlyDictionary<string, string> bindings, bool isLogicRunning)
    {
        return new AssertionResult
        {
            AssertionId = $"p3-logic-stopped-{bindings["endState"]}-{bindings["playerCount"]}",
            Passed = !isLogicRunning,
            ActualValue = isLogicRunning.ToString(),
            FailFrame = 0,
            Message = !isLogicRunning
                ? null
                : $"Expected decided cell {bindings["endState"]}/{bindings["playerCount"]} to stop logic.",
        };
    }

    private static OutcomeExpectation GetExpectedOutcome(string endState, int playerCount)
    {
        return endState switch
        {
            PlayerEliminatedEndState => new OutcomeExpectation(OutcomeReason.Eliminated, [2], playerCount + 1, (playerCount - 1) * (int)VictorySystem.DefaultCheckCadence.Value),
            PlayerSurrendersEndState => new OutcomeExpectation(OutcomeReason.Surrendered, Enumerable.Range(3, playerCount - 1).ToArray(), 2, 0),
            AiSurrendersEndState => new OutcomeExpectation(OutcomeReason.Surrendered, [2, .. Enumerable.Range(4, playerCount - 2)], 3, 0),
            TeamWinEndState => new OutcomeExpectation(OutcomeReason.Eliminated, [2, 3], playerCount + 1, (playerCount - 2) * (int)VictorySystem.DefaultCheckCadence.Value),
            MutualEliminationEndState => new OutcomeExpectation(OutcomeReason.Eliminated, [2], playerCount + 1, playerCount * (int)VictorySystem.DefaultCheckCadence.Value),
            ScoreDecisionEndState => new OutcomeExpectation(OutcomeReason.ScoreDecision, [2], null, (int)VictorySystem.DefaultCheckCadence.Value),
            _ => throw new InvalidOperationException($"Unsupported end state '{endState}'."),
        };
    }

    private static int[] GetTeamWinConfiguration(int playerCount)
    {
        return playerCount switch
        {
            3 => [1, 1, 2],
            4 => [1, 1, 2, 2],
            _ => throw new InvalidOperationException($"No team-win configuration for playerCount={playerCount}."),
        };
    }

    private static GameObject CreateGameObject(TestGame game, ObjectDefinition definition, Player owner)
    {
        return new GameObject(definition, game.GameEngine, owner);
    }

    private static ObjectDefinition CreateCommandCenterDefinition()
    {
        var definition = new ObjectDefinition("P3MatrixCommandCenter");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        definition.KindOf.Set(ObjectKinds.CommandCenter, true);
        typeof(ObjectDefinition).GetProperty(nameof(ObjectDefinition.BuildCost))!.SetValue(definition, 500f);
        return definition;
    }

    private static ObjectDefinition CreateStructureDefinition()
    {
        var definition = new ObjectDefinition("P3MatrixStructure");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        return definition;
    }

    private static ObjectDefinition CreateUnitDefinition()
    {
        return new ObjectDefinition("P3MatrixUnit");
    }

    private static void RunVictoryCadence(TestGame game)
    {
        for (var i = 0u; i < VictorySystem.DefaultCheckCadence.Value; i++)
        {
            game.GameLogic.Update();
        }
    }

    private const string PlayerEliminatedEndState = "PlayerEliminated";
    private const string PlayerSurrendersEndState = "PlayerSurrenders";
    private const string AiSurrendersEndState = "AISurrenders";
    private const string TeamWinEndState = "TeamWin";
    private const string MutualEliminationEndState = "MutualElimination";
    private const string ScoreDecisionEndState = "ScoreDecision";

    private sealed record ScenarioSetup(
        IReadOnlyList<Player> Players,
        IReadOnlyList<GameObject> CommandCenters,
        IReadOnlyList<GameObject> Units);

    private sealed record CellExecution(
        TestGame Game,
        string EndState,
        int PlayerCount,
        OutcomeStatus FinalStatus,
        OutcomeReason FinalReason,
        IReadOnlyList<int> WinningPlayerIndices,
        int? LosingPlayerIndex,
        int? DecidedFrame,
        bool SuddenDeathObserved,
        bool IsLogicRunning);

    private sealed record OutcomeExpectation(
        OutcomeReason Reason,
        IReadOnlyList<int> WinningPlayerIndices,
        int? LosingPlayerIndex,
        int DecidedFrame);
}

// ── P4: Economy ───────────────────────────────────────────────────────────────
// Matrix: faction × supplyDensity × startingMoney
// Validates the economy loop runs correctly across the configuration space.

public class P4_ScenarioMatrixTests
{
    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_AllCells_EconomyLoopRunsCorrectly()
    {
        // Arrange: ScenarioMatrix with axes
        //          faction ∈ {USA, GLA, China},
        //          supplyDensity ∈ {sparse, normal, rich},
        //          startingMoney ∈ {low (500), normal (10000)} — 18 cells.
        // Act: expand and run all cells for a fixed frame count via P0-5b.
        // Assert: every cell asserts BankAccount >= 0 at all ticks (per-tick invariant),
        //         income increased at least once, and >= 1 unit was produced.

    }

    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_LowStartingMoney_ProductionGatedUntilIncomeArrives()
    {
        // Arrange: startingMoney=low cells.
        // Act: run those cells.
        // Assert: production does not start until BankAccount reaches the unit cost threshold;
        //         BankAccount never goes negative.

    }
}

// ── P5: Construction & production ────────────────────────────────────────────
// Matrix: faction × buildTreeDepth × playerType
// Validates prerequisite gating and production completeness across the config space.

public class P5_ScenarioMatrixTests
{
    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_AllCells_PrerequisiteGatingEnforced()
    {
        // Arrange: ScenarioMatrix with axes
        //          faction ∈ {USA, GLA, China},
        //          buildTreeDepth ∈ {tier1, tier2, tier3},
        //          playerType ∈ {human, AI} — 18 cells.
        //          tier3 cells issue the full prerequisite chain within the scenario.
        // Act: expand and run all cells via P0-5b.
        // Assert: every cell asserts the gated unit/upgrade is unavailable before prereqs
        //         and available after; tier3 cells assert all three tiers construct correctly.

    }

    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_AIPlayerType_BuildsViaOrderProcessorNotDirectAPI()
    {
        // Arrange: playerType=AI cells.
        // Act: run those cells.
        // Assert: the AI's build/produce actions are issued via OrderProcessor (order stream
        //         contains the expected BuildObject/CreateUnit orders, not just state changes).

    }
}

// ── P6: Combat & command ──────────────────────────────────────────────────────
// Matrix: orderType × faction
// Validates each command type produces the expected OrderProcessor effect for every faction.

public class P6_ScenarioMatrixTests
{
    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_AllCells_OrderTypeProducesExpectedEffect()
    {
        // Arrange: ScenarioMatrix with axes
        //          orderType ∈ {MoveTo, AttackObject, BuildObject, CreateUnit, SpecialPower},
        //          faction ∈ {USA, GLA, China} — 15 cells.
        // Act: expand and run all cells via P0-5b; each cell issues the given order for
        //      the faction's reference unit.
        // Assert: every cell asserts the observable effect for that faction's reference unit
        //         (position delta / health decrease / building exists / unit spawned / ability effect).

    }

    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_SpecialPowerCells_UnimplementedHandlersSurfaceAsFailures()
    {
        // Arrange: SpecialPower cells across all three factions.
        // Act: run those cells.
        // Assert: any SpecialPower order whose handler is unimplemented produces an explicit
        //         assertion failure (not a silent no-op) in the per-cell report.

    }
}

// ── P6b: Unit-contract validation ────────────────────────────────────────────
// Matrix: archetype × faction
// Validates the full unit contract for every archetype-faction combination.

public class P6b_ScenarioMatrixTests
{
    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_AllValidCells_FullContractPasses()
    {
        // Arrange: ScenarioMatrix with axes
        //          archetype ∈ {infantry, vehicle, aircraft, builder, support, superweapon},
        //          faction ∈ {USA, GLA, China};
        //          cells without a valid reference unit pruned by ConstraintFilter.
        // Act: expand and run all valid cells via P0-5b; each cell runs the full archetype
        //      contract (produce/move/attack/all abilities/die).
        // Assert: every valid cell asserts all six contract points; unimplemented special-power
        //         handlers are reported as explicit gaps (not silent passes).

    }

    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_GapCells_ReportedInAggregateWithAxisKey()
    {
        // Arrange: introduce a known-unimplemented SpecialPower into one cell's archetype.
        // Act: run the matrix.
        // Assert: the aggregate JSON report identifies the failing cell by its
        //         archetype × faction key and includes the assertion detail.

    }
}

// ── P7: End-to-end regression ─────────────────────────────────────────────────
// Matrix: endState (with universal invariants attached to all cells)
// The canonical "is the game still playable?" regression gate.

public class P7_ScenarioMatrixTests
{
    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_AllEndStateCells_PassWithInvariants()
    {
        // Arrange: ScenarioMatrix with axis
        //          endState ∈ {PlayerEliminated, PlayerSurrenders, AISurrenders, TeamWin,
        //                       MutualElimination, ScoreDecision};
        //          universal invariant assertions attached to all cells:
        //            - BankAccount never negative (per-tick)
        //            - Unit count not duplicated (per-tick)
        //            - AI issued >= 1 order in matches where AI participates.
        // Act: expand and run all six cells via P0-5b.
        // Assert: all cells pass their end-state GameOutcome assertion and all invariants;
        //         this is the standing playability gate.

    }

    [Fact(Skip = "Stub — blocked on the named phase; implement when that phase lands"), Trait("Category", "Roadmap")]
    public void Matrix_InvariantViolation_AbortsRunAndReportsCell()
    {
        // Arrange: introduce a deliberate invariant violation in one cell's scenario
        //          (e.g. force a negative BankAccount).
        // Act: run the matrix.
        // Assert: the violating run is aborted; the aggregate report identifies the cell
        //         by its endState key and includes the offending frame.

    }
}
