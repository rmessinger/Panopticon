using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage;
using OpenSage.Data.Map;
using OpenSage.Logic;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;
using Player = OpenSage.Logic.Player;

namespace OpenSage.Tests.Tools.ReplaySketch;

// WI-A2-04: the A2 statistical verification harness. One scenario runs N times; every run's
// correctness invariants are checked independently; aggregate metric distributions report
// mean, variance, confidence interval, and outliers. No assertion requires same-seed
// agreement or state-hash parity between runs.

public class A2StatisticalHarnessTests : MockedGameTest
{
    // ── Aggregation and statistics on crafted run outputs ──────────────────────

    [Fact, Trait("Category", "Roadmap")]
    public void Aggregate_ReportsMeanVarianceAndStudentTInterval()
    {
        // Two samples: mean 105, sample variance 50, t(1) = 12.706, half-width = 63.53.
        var report = StatisticalRunHarness.Aggregate(
        [
            Sample(0, frames: 100),
            Sample(1, frames: 110),
        ]);

        var frames = report.Metric("totalLogicFrames");
        Assert.NotNull(frames);
        Assert.Equal(2, frames.SampleCount);
        Assert.Equal(105d, frames.Mean, 6);
        Assert.Equal(50d, frames.SampleVariance, 6);
        Assert.Equal(105d - 12.706 * Math.Sqrt(50d / 2d), frames.ConfidenceLow, 4);
        Assert.Equal(105d + 12.706 * Math.Sqrt(50d / 2d), frames.ConfidenceHigh, 4);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Aggregate_FlagsTukeyOutliers_ByRunIndex()
    {
        var report = StatisticalRunHarness.Aggregate(
        [
            Sample(0, frames: 600),
            Sample(1, frames: 600),
            Sample(2, frames: 600),
            Sample(3, frames: 600),
            Sample(4, frames: 900),
        ]);

        var frames = report.Metric("totalLogicFrames");
        var outlier = Assert.Single(frames.Outliers);
        Assert.Equal(4, outlier.RunIndex);
        Assert.Equal(900d, outlier.Value, 6);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Aggregate_PerRunInvariantFailure_ReportsRunSeedAndAssertion()
    {
        var failing = new AssertionReport(
        [
            new AssertionResult
            {
                AssertionId = "p7-money-never-negative",
                Passed = false,
                FailFrame = 42,
                Message = "Invariant failed",
            },
        ]);

        var report = StatisticalRunHarness.Aggregate(
        [
            Sample(0, frames: 100),
            new StatisticalRunSample(1, 77, failing, Sample(1, frames: 100).Output),
        ]);

        Assert.False(report.AllInvariantsPassed);
        var failure = Assert.Single(report.InvariantFailures);
        Assert.Equal(1, failure.RunIndex);
        Assert.Equal(77u, failure.Seed);
        Assert.Equal("p7-money-never-negative", failure.AssertionId);
        Assert.Contains("Invariant failed", failure.Message);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Aggregate_NeverFailsBecauseRunsDiffer()
    {
        // Runs with entirely different metrics and seeds must not affect invariant
        // evaluation: the validation model has no cross-run comparison.
        var report = StatisticalRunHarness.Aggregate(
        [
            Sample(0, frames: 65, money: 5000, owned: 3),
            Sample(1, frames: 70, money: 5123, owned: 7),
            Sample(2, frames: 65, money: 4001, owned: 0),
        ]);

        Assert.True(report.AllInvariantsPassed);
        Assert.Empty(report.InvariantFailures);
        var frames = report.Metric("totalLogicFrames");
        Assert.Equal(3, frames.SampleCount);
    }

    // ── Default N-run execution on the mocked A1 host (non-interactive) ────────

    [Fact, Trait("Category", "Roadmap")]
    public void MockedRuns_DefaultN_InvariantsCheckedPerRun_MetricsAggregated()
    {
        const int runCount = 5;
        var samples = new List<StatisticalRunSample>();

        for (var i = 0; i < runCount; i++)
        {
            var seed = (uint)(1000 + i * 37);
            var game = new TestGame(SageGame.CncGenerals);
            ConfigureTwoPlayerSkirmish(game);

            // Per-run economy varies with the recorded seed, so the metric distributions
            // have real dispersion. Dispersion is reported, never asserted against.
            game.PlayerManager.Players[2].BankAccount.Deposit(5000 + seed % 500, playSound: false);
            game.PlayerManager.Players[3].BankAccount.Deposit(4000 + seed % 300, playSound: false);

            var host = new SimulationHost(game.GameEngine);
            using var recorder = new RunOutputRecorder(host, game, seed, "a2-mocked-two-player", sampleInterval: 10);
            host.Run(65);

            RegressionInvariants.EnsureRegistered();
            var invariantResults = RegressionInvariants.Assertions
                .Select(assertion => assertion.Evaluate(game, (uint)host.Frame))
                .ToList();

            samples.Add(new StatisticalRunSample(i, seed, new AssertionReport(invariantResults), recorder.Complete()));
        }

        var report = StatisticalRunHarness.Aggregate(samples);

        Assert.True(report.AllInvariantsPassed, report.ToJson());
        Assert.Equal(runCount, report.RunCount);

        var frames = report.Metric("totalLogicFrames");
        Assert.Equal(runCount, frames.SampleCount);
        Assert.True(frames.ConfidenceHigh >= frames.ConfidenceLow);

        var money = report.Metric("final.money.player2");
        Assert.NotNull(money);
        Assert.Equal(runCount, money.SampleCount);
        Assert.True(money.SampleVariance > 0, "seeded per-run variation should surface as dispersion");

        var owned = report.Metric("final.ownedObjects.player2");
        Assert.NotNull(owned);
        Assert.Equal(runCount, owned.SampleCount);

        Assert.Contains("\"totalLogicFrames\"", report.ToJson(), StringComparison.Ordinal);
        Assert.Contains("\"ci95\"", report.ToJson(), StringComparison.Ordinal);
    }

    // ── P0-harness-backed N runs (requires install; vacuous without) ───────────

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive")]
    public void P0Harness_DefaultN_EveryRunChecksAssertions_AndMetricsAggregate()
    {
        RunP0HarnessSamples(3);
    }

    [Fact, Trait("Category", "Roadmap"), Trait("Category", "Interactive"), Trait("Category", "StatisticalHighN")]
    public void P0Harness_HighN_EveryRunChecksAssertions_AndMetricsAggregate()
    {
        RunP0HarnessSamples(10);
    }

    private static void RunP0HarnessSamples(int runCount)
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
                    Actions =
                    {
                        new ActionEntry(
                            label: "Move",
                            command: CommandSpec.CreateOpen(
                                OrderType.MoveTo,
                                new PositionCommandArgument(new NormalizedPosition(0.3f, 0.3f))),
                            position: null,
                            timing: new FixedTiming(100),
                            id: "move"),
                    },
                },
            },
            Assertions =
            {
                // Per-run correctness invariant: money never goes negative in any run.
                new InvariantAssertion
                {
                    Id = "money-never-negative",
                    Path = "player[0].money",
                    Op = ">=",
                    Value = 0,
                },
            },
        };

        var installationService = new GeneralsInstallationService();
        installationService.TryGetInstallation(out var installation);

        var samples = new List<StatisticalRunSample>();
        for (var i = 0; i < runCount; i++)
        {
            if (installation is null)
            {
                samples.Add(new StatisticalRunSample(i, 0, AssertionReport.Empty, null));
                continue;
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), $"rs-a2-stats-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            try
            {
                RunOutput? output = null;
                var report = HarnessRunner.RunAndAssert(scenario, installation, tempDirectory, o => output = o);
                samples.Add(new StatisticalRunSample(i, output?.Seed ?? 0, report, output));
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        var aggregate = StatisticalRunHarness.Aggregate(samples);

        // Invariants hold independently in every run; the report names the run otherwise.
        Assert.True(aggregate.AllInvariantsPassed, aggregate.ToJson());

        // Without an install the samples are vacuous (CI); with one, all runs produced output.
        if (installation is null)
        {
            return;
        }

        var frames = aggregate.Metric("totalLogicFrames");
        Assert.Equal(runCount, frames.SampleCount);
        Assert.All(aggregate.Samples, s => Assert.NotNull(s.Output));
        // The seed is recorded with each run for investigation; it is not compared across runs.
        Assert.All(aggregate.Samples, s => Assert.Equal(s.Output!.Seed, s.Seed));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static StatisticalRunSample Sample(int runIndex, uint frames = 0, uint money = 5000, int owned = 3) =>
        new(
            runIndex,
            Seed: (uint)runIndex,
            Assertions: new AssertionReport([new AssertionResult { AssertionId = "always", Passed = true }]),
            Output: new RunOutput
            {
                Seed = (uint)runIndex,
                ScenarioId = "crafted",
                TotalLogicFrames = frames,
                WallClockDurationMilliseconds = 100 + runIndex,
                MetricSamples =
                {
                    new RunMetricSample
                    {
                        Frame = frames,
                        Players = { new RunPlayerMetrics { Player = "player2", Money = money, OwnedObjects = owned } },
                    },
                },
            });

    /// <summary>
    /// Two competitive players marked human (the AI-progress invariant is vacuous for human
    /// players, matching the P7 matrix base), plus the neutral and civilian slots.
    /// </summary>
    private static void ConfigureTwoPlayerSkirmish(TestGame game)
    {
        var mapPlayers = new List<OpenSage.Data.Map.Player>
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
            new()
            {
                Name = "player2",
                Faction = "FactionAmerica",
                DisplayName = "player2",
                IsHuman = true,
            },
            new()
            {
                Name = "player3",
                Faction = "FactionGLA",
                DisplayName = "player3",
                IsHuman = true,
            },
        };

        var settings = new List<PlayerSetting>
        {
            new(2, "FactionAmerica", new ColorRgb(255, 64, 64), 1, PlayerOwner.Player, "player2"),
            new(3, "FactionGLA", new ColorRgb(64, 64, 255), 2, PlayerOwner.Player, "player3"),
        };

        game.PlayerManager.OnNewGame(mapPlayers.ToArray(), GameType.Skirmish, settings.ToArray());
        game.VictorySystem.CurrentOutcome.Reset();
    }
}
