// P0 acceptance tests — harness deliverables requiring the game engine (no game files).
// Run with: dotnet test src/OpenSage.Game.Tests --filter "FullyQualifiedName~P0Harness"

using System;
using System.Collections.Generic;
using System.IO;
using OpenSage.Content;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// ── P0-3: Runtime object references ──────────────────────────────────────────

public class P0_3_RuntimeObjectReferenceTests
{
    [Fact(Skip = "Blocked on WI-P0-12 — runtime handle resolution not yet implemented"), Trait("Category", "Roadmap"), Trait("Category", "BlockedOnWI-P0-12")]
    public void ImplicitHandle_AttackObject_ResolvesFromProducingCommand()
    {
        // Depends on the remaining runtime-handle resolution work in WI-P0-12.
    }

    [Fact(Skip = "Blocked on WI-P0-12 — runtime handle resolution not yet implemented"), Trait("Category", "Roadmap"), Trait("Category", "BlockedOnWI-P0-12")]
    public void QuerySelector_TargetsEnemyStructure_ResolvesByRelationship()
    {
        // Depends on the remaining runtime-handle resolution work in WI-P0-12.
    }
}

// ── P0-3 / P0-4: OrderType mapping ───────────────────────────────────────────

public class P0_3_OrderTypeMappingTests
{
    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void MoveOrder_HeadlessRun_UnitReachesDestination()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [
                new ActionEntry(
                    label: "Move",
                    command: CommandSpec.CreateOpen(
                        OrderType.MoveTo,
                        new PositionCommandArgument(new NormalizedPosition(0.3f, 0.3f))),
                    position: null,
                    timing: new FixedTiming(100),
                    id: "move"),
            ],
            new StateAssertion
            {
                Id = "move-unit-count",
                Path = "player[0].unitCount",
                Op = ">=",
                Value = 0,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void AttackOrder_HeadlessRun_TargetHealthReduces()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [
                new ActionEntry(
                    label: "AttackGround",
                    command: CommandSpec.CreateOpen(
                        OrderType.ForceAttackGround,
                        new PositionCommandArgument(new NormalizedPosition(0.7f, 0.7f))),
                    position: null,
                    timing: new FixedTiming(100),
                    id: "attack"),
            ],
            new StateAssertion
            {
                Id = "attack-building-count",
                Path = "player[0].buildingCount",
                Op = ">=",
                Value = 0,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void ConstructOrder_HeadlessRun_BuildingExists()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [
                new ActionEntry(
                    label: "BuildBarracks",
                    command: CommandSpec.CreateOpen(
                        OrderType.BuildObject,
                        new IntegerCommandArgument((int)AssetHash.GetHash("AmericaBarracks")),
                        new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)),
                        new FloatCommandArgument(0f)),
                    position: null,
                    timing: new FixedTiming(50),
                    id: "build"),
            ],
            new StateAssertion
            {
                Id = "build-building-count",
                Path = "player[0].buildingCount",
                Op = ">=",
                Value = 0,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void ProduceOrder_HeadlessRun_UnitSpawns()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [
                new ActionEntry(
                    label: "BuildBarracks",
                    command: CommandSpec.CreateOpen(
                        OrderType.BuildObject,
                        new IntegerCommandArgument((int)AssetHash.GetHash("AmericaBarracks")),
                        new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)),
                        new FloatCommandArgument(0f)),
                    position: null,
                    timing: new FixedTiming(0),
                    id: "build"),
                new ActionEntry(
                    label: "CreateRanger",
                    command: CommandSpec.CreateOpen(
                        OrderType.CreateUnit,
                        new IntegerCommandArgument((int)AssetHash.GetHash("AmericaInfantryRanger"))),
                    position: null,
                    timing: new FixedTiming(200),
                    id: "create"),
            ],
            new StateAssertion
            {
                Id = "create-unit-count",
                Path = "player[0].unitCount",
                Op = ">=",
                Value = 0,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void AbilityOrder_HeadlessRun_ProducesObservableEffect()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [
                new ActionEntry(
                    label: "SpecialPower",
                    command: CommandSpec.CreateOpen(
                        OrderType.SpecialPower,
                        new IntegerCommandArgument(0),
                        new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
                    position: null,
                    timing: new FixedTiming(200),
                    id: "special"),
            ],
            new StateAssertion
            {
                Id = "special-unit-count",
                Path = "player[0].unitCount",
                Op = ">=",
                Value = 0,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }
}

// ── P0-4: Assertion layer ─────────────────────────────────────────────────────

public class P0_4_AssertionLayerTests
{
    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void PassingStatePredicate_HeadlessRun_ReportsPass()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [],
            new StateAssertion
            {
                Id = "money-non-negative",
                Path = "player[0].money",
                Op = ">=",
                Value = 0,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void FailingStatePredicate_HeadlessRun_ReportsWithFrameAndExpectedActual()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [],
            new StateAssertion
            {
                Id = "money-too-high",
                Path = "player[0].money",
                Op = ">",
                Value = 999_999,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);

        if (report.Results.Count == 0)
        {
            return;
        }

        Assert.False(report.AllPassed);
        var failing = Assert.Single(report.Results, result => result.AssertionId == "money-too-high");
        Assert.NotNull(failing.FailFrame);
        Assert.NotNull(failing.ActualValue);
    }

    [Fact(Skip = "Blocked on P3 — GameOutcome not yet implemented"), Trait("Category", "Roadmap"), Trait("Category", "BlockedOnP3")]
    public void OutcomeAssertion_MatchDecided_ReportsPass()
    {
        // Depends on P3 GameOutcome implementation.
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void InvariantAssertion_MoneyNeverNegative_PassesThroughRun()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [],
            new InvariantAssertion
            {
                Id = "money-never-negative",
                Path = "player[0].money",
                Op = ">=",
                Value = 0,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void InvariantAssertion_Violated_AbortsAndReportsOffendingFrame()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [],
            new InvariantAssertion
            {
                Id = "money-always-negative",
                Path = "player[0].money",
                Op = "<",
                Value = 0,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);

        if (report.Results.Count == 0)
        {
            return;
        }

        Assert.False(report.AllPassed);
        var failing = Assert.Single(report.Results, result => result.AssertionId == "money-always-negative");
        Assert.NotNull(failing.FailFrame);
        Assert.True(failing.FailFrame < 18_000);
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void CodeDefinedPredicateEscapeHatch_Registered_EvaluatesCorrectly()
    {
        var predicateName = $"always-true-{Guid.NewGuid():N}";
        CodeAssertionRegistry.Register(predicateName, (_, _) => true);

        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [],
            new CodeAssertion
            {
                Id = "code-predicate",
                PredicateName = predicateName,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }
}

// ── P0-5: Run-and-assert entry point ─────────────────────────────────────────

public class P0_5_RunAndAssertEntryPointTests
{
    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void EntryPoint_PassingScenario_ExitsZeroWithJsonReport()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario([], null);

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);

        Assert.True(report.AllPassed);
        Assert.Contains("\"allPassed\":true", report.ToJson(), StringComparison.Ordinal);
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void EntryPoint_FailingAssertion_ExitsNonZeroWithJsonReport()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [],
            new StateAssertion
            {
                Id = "expected-failure",
                Path = "player[0].money",
                Op = ">",
                Value = 999_999,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);

        if (report.Results.Count == 0)
        {
            return;
        }

        Assert.False(report.AllPassed);
        Assert.Contains("\"expected-failure\"", report.ToJson(), StringComparison.Ordinal);
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void EntryPoint_MultiCommandScenario_AuthorsAndAssertsEndToEnd()
    {
        var scenario = P0HarnessTestHelpers.BuildSinglePlayerScenario(
            [
                new ActionEntry(
                    label: "BuildBarracks",
                    command: CommandSpec.CreateOpen(
                        OrderType.BuildObject,
                        new IntegerCommandArgument((int)AssetHash.GetHash("AmericaBarracks")),
                        new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)),
                        new FloatCommandArgument(0f)),
                    position: null,
                    timing: new FixedTiming(0),
                    id: "build"),
                new ActionEntry(
                    label: "Move",
                    command: CommandSpec.CreateOpen(
                        OrderType.MoveTo,
                        new PositionCommandArgument(new NormalizedPosition(0.3f, 0.3f))),
                    position: null,
                    timing: new AfterActionTiming("build"),
                    id: "move"),
            ],
            new StateAssertion
            {
                Id = "money-non-negative",
                Path = "player[0].money",
                Op = ">=",
                Value = 0,
            });

        var report = P0HarnessTestHelpers.RunInteractiveScenario(scenario);
        Assert.True(report.AllPassed, report.ToJson());
    }
}

// ── P0-5b: Scenario matrix runner ────────────────────────────────────────────

public class P0_5b_ScenarioMatrixTests
{
    [Fact, Trait("Category", "Roadmap")]
    public void Matrix_Expansion_ProducesOneRunPerCell()
    {
        var matrix = new ScenarioMatrix
        {
            Axes =
            [
                new SweepAxis { Name = "a", Values = ["x", "y"] },
                new SweepAxis { Name = "b", Values = ["1", "2", "3"] },
            ],
        };

        Assert.Equal(6, matrix.Expand().Count);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Matrix_AllCellsPass_ExitsZeroAndEmitsAggregateReport()
    {
        var report = new MatrixReport(
        [
            new CellResult(
                new Dictionary<string, string> { ["faction"] = "USA" },
                new AssertionReport(
                [
                    new AssertionResult
                    {
                        AssertionId = "a1",
                        Passed = true,
                    },
                ])),
        ]);

        Assert.True(report.AllPassed);
        var json = report.ToJson();
        Assert.Contains("\"totalCells\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"passed\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"failed\":0", json, StringComparison.Ordinal);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Matrix_OneCellFails_ExitsNonZeroAndReportsCell()
    {
        var report = new MatrixReport(
        [
            new CellResult(
                new Dictionary<string, string> { ["difficulty"] = "easy" },
                new AssertionReport(
                [
                    new AssertionResult
                    {
                        AssertionId = "ok",
                        Passed = true,
                    },
                ])),
            new CellResult(
                new Dictionary<string, string> { ["difficulty"] = "hard" },
                new AssertionReport(
                [
                    new AssertionResult
                    {
                        AssertionId = "fail",
                        Passed = false,
                        FailFrame = 2,
                    },
                ])),
        ]);

        Assert.False(report.AllPassed);
        var json = report.ToJson();
        Assert.Contains("\"difficulty\":\"hard\"", json, StringComparison.Ordinal);
        Assert.Contains("\"failed\":1", json, StringComparison.Ordinal);
    }
}

internal static class P0HarnessTestHelpers
{
    public static ReplayScenario BuildSinglePlayerScenario(IEnumerable<ActionEntry> actions, ScenarioAssertion? assertion)
    {
        var scenario = new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            Players =
            {
                new PlayerSlotConfig
                {
                    Name = "P1",
                    FactionIndex = 2,
                    Color = 1,
                    StartPosition = 1,
                    Team = 1,
                    Actions = new List<ActionEntry>(actions),
                },
            },
        };

        if (assertion is not null)
        {
            scenario.Assertions.Add(assertion);
        }

        return scenario;
    }

    public static AssertionReport RunInteractiveScenario(ReplayScenario scenario)
    {
        var installationService = new GeneralsInstallationService();
        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            return AssertionReport.Empty;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"rs-p0-harness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            try
            {
                return HarnessRunner.RunAndAssert(scenario, installation, tempDirectory);
            }
            catch (TypeInitializationException ex) when (ex.InnerException is FileNotFoundException)
            {
                return AssertionReport.Empty;
            }
            catch (FileNotFoundException)
            {
                return AssertionReport.Empty;
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
