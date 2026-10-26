using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Data;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Navigation;
using Panopticon.Network;

using NavigationGrid = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-N0-02 pathfinding-load scenario: spawns one side's ground units on the capacity-probe map
/// and measures the pathfinding cost of a single multi-unit move order per case. Cases cover
/// squad-less selections (per-unit searches), the same sizes under army-AI squads (leader
/// searches through GroupMovement), and one unreachable destination (failed searches). Only
/// side A thinks; with no enemy targets its decisions issue only selection-and-defend orders on
/// decision ticks, and case windows avoid decision-tick phases, so measured path traffic is the
/// scripted order's alone. The per-case metrics land in the report JSON and a B1 record beside
/// it; WI-N0-01 counters attribute requests by origin.
/// </summary>
public static class PathfindingLoadRunner
{
    private const string DefaultReportFileName = "pathfinding-load-report.json";

    // B1 record identity for the scenario (WI-N0-02).
    private const string BenchmarkScenarioId = "n0.pathfinding-load";

    // Exit codes follow the capacity-probe convention: 1 setup error, 5 unmet acceptance check.
    private const int ExitSetupError = 1;
    private const int ExitCheckFailed = 5;

    // Ticks sampled after each order: enough for the request burst plus follow-up ticks.
    private const int WindowTicks = 30;

    // Squad-less orders land well before the next AI decision tick (squads would form there);
    // squad orders land two ticks after the decision tick that synchronizes squads.
    private const int SquadlessSettleFrames = 5;

    // Impassable-pocket scan: placement bounds inflated by this margin, stepped at this stride.
    private const float ImpassableScanInflation = 200f;
    private const float ImpassableScanStride = 40f;

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var profilePath = ResolveProfilePath(config, reporter);
        if (profilePath == null)
        {
            return ExitSetupError;
        }

        MassBattleDeploymentProfile profile;
        try
        {
            profile = MassBattleDeploymentProfile.Load(profilePath);
        }
        catch (Exception ex)
        {
            reporter.Error($"Could not load pathfinding load profile '{profilePath}': {ex.Message}");
            return ExitSetupError;
        }

        if (profile.Sides.Count != 2)
        {
            reporter.Error($"Pathfinding load expects exactly 2 sides, found {profile.Sides.Count}.");
            return ExitSetupError;
        }

        if (profile.Capacity.Blocks.Count == 0)
        {
            reporter.Error("Pathfinding load expects at least one capacity block to spawn from.");
            return ExitSetupError;
        }

        var sizes = config.PathfindingLoadSelectionSizes ?? [1, 10, 100, 1000];
        if (sizes.Any(size => size < 1))
        {
            reporter.Error($"Pathfinding load sizes must be positive integers, got [{string.Join(", ", sizes)}].");
            return ExitSetupError;
        }

        var seed = config.PathfindingLoadSeed;
        if (seed < 1)
        {
            reporter.Error($"Pathfinding load seed must be positive, got {seed}.");
            return ExitSetupError;
        }

        // Same log flood as the capacity probe: spawning armies drowns stdout in Info lines.
        var loggingConfig = NLog.LogManager.Configuration;
        if (loggingConfig != null)
        {
            foreach (var rule in loggingConfig.LoggingRules)
            {
                rule.DisableLoggingForLevel(NLog.LogLevel.Info);
            }

            NLog.LogManager.ReconfigExistingLoggers();
        }

        Game? game = null;
        try
        {
            game = new Game(
                installation,
                config.Renderer,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud },
                null);
            var (sideA, navigation) = BootProfileGame(game, profile, seed);
            // WI-N2-02 knobs: cluster-based group moves for squad-less selections.
            navigation.Options.GroupSelectionMoves = config.PathfindingLoadGroupMoves;
            navigation.Options.SelectionClusterCells = config.PathfindingLoadClusterCells;
            // WI-N4-02 knob: order-driven paths submit to the async worker queue.
            navigation.Options.AsyncOrderPaths = config.PathfindingLoadAsyncOrderPaths;
            var zoneB = profile.Sides[1].Zone;
            var crossMapDestination = MassBattlePlacement.GroundPosition(game, zoneB.CenterX, zoneB.CenterY);

            var reasons = new List<string>();
            var cases = new List<PathfindingLoadCaseResult>();

            // B1 measurement stack (WI-M1-02 pattern): every stepped frame is a sample.
            var totalStopwatch = Stopwatch.StartNew();
            uint frameIndex = 0;
            var tickPathSpanMs = 0.0;
            var cadence = (int)AIPlayer.DefaultDecisionCadenceFrames;
            var hierarchy = sideA.Policy.Hierarchy;

            void OnSystemSpan(string system, TimeSpan span)
            {
                if (system == "path")
                {
                    tickPathSpanMs += span.TotalMilliseconds;
                }
            }

            using var recorder = new BenchmarkMetricRecorder();
            game.LogicStepper.SystemSpan += recorder.RecordSystemSpan;
            game.LogicStepper.SystemSpan += OnSystemSpan;
            try
            {
                void Step()
                {
                    frameIndex++;
                    tickPathSpanMs = 0;
                    recorder.BeginTick(frameIndex);
                    game.Update(Array.Empty<InputMessage>());
                    recorder.EndTick(frameIndex);
                }

                uint StepUntilPhase(int targetPhase)
                {
                    var stepped = 0u;
                    while (MassBattlePlacement.CurrentLogicFrame(game) % (uint)cadence != (uint)targetPhase && stepped < (uint)cadence + 5)
                    {
                        Step();
                        stepped++;
                    }

                    return stepped;
                }

                // Calibrate where the decision ticks land: the frame the scout joins a squad is
                // a decision tick, so its phase fixes the boundary for every later case.
                var scoutDefinition = MassBattlePlacement.ResolveDefinition(game, profile.Capacity.Blocks[0].Definition);
                if (scoutDefinition == null)
                {
                    reporter.Error($"Could not resolve scout definition '{profile.Capacity.Blocks[0].Definition}'.");
                    return ExitSetupError;
                }

                var scout = MassBattlePlacement.SpawnAt(
                    game,
                    scoutDefinition,
                    sideA.Player,
                    profile.Sides[0].Zone.CenterX,
                    profile.Sides[0].Zone.CenterY,
                    MathF.PI * profile.Sides[0].FacingDegrees / 180f);
                var boundaryPhase = -1;
                for (var i = 0; i < cadence * 3; i++)
                {
                    Step();
                    if (hierarchy.SquadOf(scout) != null)
                    {
                        boundaryPhase = (int)(MassBattlePlacement.CurrentLogicFrame(game) % (uint)cadence);
                        break;
                    }
                }

                if (boundaryPhase < 0)
                {
                    reporter.Error("Could not calibrate the AI decision-tick boundary; scout never joined a squad.");
                    return ExitSetupError;
                }

                // The decision that inducted the scout queued orders that execute on the next
                // tick; let them drain against the live scout first. Destroying it earlier
                // makes the queued SetSelection resolve a null object, which poisons the
                // player's selection and crashes the paired move order. Case teardown destroys
                // units mid-cycle, far from any in-flight decision, for the same reason.
                StepUntilPhase((boundaryPhase + 10) % cadence);
                game.Scene3D.GameObjects.DestroyObject(scout);
                game.Scene3D.GameObjects.DeleteDestroyed();

                var (impassableDestination, impassableKind) = FindImpassableDestination(game, profile, navigation, profile.Sides[0].Zone);
                // Planning probes accumulate span; zero it so the first case window starts clean.
                // Counter windows are per-case deltas, so planning counters need no reset.
                _ = navigation.ConsumePathSpan();
                reporter.Info($"Impassable destination: {impassableKind} at {impassableDestination.X:0.#}/{impassableDestination.Y:0.#}.");

                foreach (var plan in PathfindingLoadCasePlanner.BuildCases(sizes))
                {
                    var result = RunCase(
                        game, plan, profile, sideA, hierarchy, navigation, reporter, reasons,
                        Step, () => StepUntilPhase((boundaryPhase + 1) % cadence), cadence,
                        crossMapDestination, impassableDestination, impassableKind,
                        () => tickPathSpanMs);
                    cases.Add(result);
                    reporter.Info(
                        $"{result.Name}: span {result.OrderTickPathSpanMs:0.000} ms, unit {result.Requests.Unit}, squadLeader {result.Requests.SquadLeader}, " +
                        $"blocked {result.Requests.BlockedFollower}, failed {result.FailedSearches}, mean {result.MeanSearchMs:0.000} ms, p99 {result.P99SearchMs:0.000} ms.");
                }
            }
            finally
            {
                game.LogicStepper.SystemSpan -= recorder.RecordSystemSpan;
                game.LogicStepper.SystemSpan -= OnSystemSpan;
            }

            totalStopwatch.Stop();

            var allPassed = reasons.Count == 0;

            // WI-N4-02: whole-run async order path stats; wait-frame percentiles over applied
            // requests only.
            var waitSamples = navigation.CopyAsyncPathWaitFrameSamples();
            double waitMedian = 0;
            double waitP99 = 0;
            var waitMax = 0;
            if (waitSamples.Length > 0)
            {
                var waitDoubles = waitSamples.Select(sample => (double)sample).ToArray();
                Array.Sort(waitDoubles);
                waitMedian = BootstrapStatistics.Percentile(waitDoubles, 0.5);
                waitP99 = BootstrapStatistics.Percentile(waitDoubles, 0.99);
                waitMax = waitSamples.Max();
            }

            var report = new PathfindingLoadReport
            {
                Map = profile.Map.Path,
                ProfilePath = profilePath,
                Seed = seed,
                GroupMoves = config.PathfindingLoadGroupMoves,
                ClusterCells = config.PathfindingLoadClusterCells,
                AsyncOrderPaths = config.PathfindingLoadAsyncOrderPaths,
                WorkerSearchMs = navigation.Statistics.WorkerSearchMs,
                AsyncSubmitted = navigation.AsyncPathSubmitted,
                AsyncApplied = navigation.AsyncPathApplied,
                AsyncCancelled = navigation.AsyncPathCancelled,
                AsyncDeadlineMisses = navigation.AsyncPathDeadlineMisses,
                AsyncFrameBound = navigation.AsyncPathFrameBound,
                AsyncWaitFramesMedian = waitMedian,
                AsyncWaitFramesP99 = waitP99,
                AsyncWaitFramesMax = waitMax,
                GridWidth = navigation.GridWidth,
                GridHeight = navigation.GridHeight,
                Derivation = "meanSearchMs = window path span sum / window sync request sum; " +
                    "p99SearchMs = BootstrapStatistics.Percentile(0.99) over per-tick estimates (tick path span ms / tick sync requests, ticks with requests only).",
                Cases = cases,
                FailureReasons = reasons,
                AllPassed = allPassed,
            };
            reporter.Info(PathfindingLoadReport.WriteText(report));

            var reportPath = string.IsNullOrEmpty(config.PathfindingLoadReportPath)
                ? Path.Combine(AppContext.BaseDirectory, DefaultReportFileName)
                : config.PathfindingLoadReportPath!;
            PathfindingLoadReport.WriteJson(report, reportPath);
            reporter.Info($"Report JSON saved to: {reportPath}");

            // B1 record beside the report (WI-M1-02 pattern): system.path.spanMs comes free from
            // the recorded spans, per-case scalars ride along as case.* statistics.
            var environment = EnvironmentRecord.Capture();
            environment.InstallId = game.Definition.Identifier;
            if (Environment.GetEnvironmentVariable("CNC_GENERALS_PATH") is { } installPath)
            {
                environment.Env["CNC_GENERALS_PATH"] = installPath;
            }

            var framesRun = (int)frameIndex;
            var benchmarkRecord = new BenchmarkRunRecord(
                BenchmarkScenarioId, (uint)seed, 0, framesRun, framesRun, false, totalStopwatch.Elapsed.TotalMilliseconds)
            {
                TickSamples = [.. recorder.TickSamples],
                SystemSamples = [.. recorder.SystemSamples],
                Environment = environment,
            };
            benchmarkRecord.Statistics.AddRange(SimulationBenchmarkRunner.BuildStatistics(benchmarkRecord, (uint)seed, baseline: null));
            foreach (var c in cases)
            {
                AppendCaseStatistics(benchmarkRecord, (uint)seed, c);
            }

            var reportDirectory = Path.GetDirectoryName(reportPath) ?? AppContext.BaseDirectory;
            var benchmarkJsonPath = Path.Combine(reportDirectory, Path.GetFileNameWithoutExtension(reportPath) + "-benchmark.json");
            var benchmarkCsvPath = Path.ChangeExtension(benchmarkJsonPath, ".csv");
            BenchmarkReportWriter.WriteJson(benchmarkRecord, benchmarkJsonPath);
            BenchmarkReportWriter.WriteCsv(benchmarkRecord, benchmarkCsvPath);
            reporter.Info($"B1 benchmark JSON saved to: {benchmarkJsonPath}");
            reporter.Info($"B1 benchmark CSV saved to: {benchmarkCsvPath}");

            return allPassed ? 0 : ExitCheckFailed;
        }
        finally
        {
            game?.Dispose();
        }
    }

    private static (ArmyAiSide SideA, NavigationGrid Navigation) BootProfileGame(Game game, MassBattleDeploymentProfile profile, int seed)
    {
        var playerSettings = profile.Sides.Select((side, index) => new PlayerSetting(
            index,
            side.Side,
            MassBattlePlacement.ParseColor(side.Color, index),
            index,
            PlayerOwner.EasyAi,
            side.Id)
        ).ToArray();

        // Both sides run the hierarchical policy so the squad machinery exists; only side A
        // keeps its AI player, so side B generates no decision traffic.
        ArmyAiSides.ApplyPolicies(playerSettings);
        game.StartSkirmishOrMultiPlayerGame(profile.Map.Path, new EchoConnection(), playerSettings, seed, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        game.EnableFixedLogicTimestep();
        var sides = ArmyAiSides.Resolve(game.Scene3D.Players, profile.Sides.Select(side => side.Id).ToArray());
        sides[1].Player.DisableAI();
        return (sides[0], game.Scene3D.Navigation);
    }

    private static PathfindingLoadCaseResult RunCase(
        Game game,
        PathfindingLoadCasePlan plan,
        MassBattleDeploymentProfile profile,
        ArmyAiSide sideA,
        SquadHierarchy hierarchy,
        NavigationGrid navigation,
        ITestHarnessReporter reporter,
        List<string> reasons,
        Action step,
        Action stepToSpawnPhase,
        int cadence,
        Vector3 crossMapDestination,
        Vector3 impassableDestination,
        string impassableKind,
        Func<double> currentTickPathSpanMs)
    {
        // Spawn just after a decision tick: squad-less cases order before the next one, squad
        // cases order two ticks after the synchronizing decision tick.
        stepToSpawnPhase();
        var spawn = MassBattlePlacement.SpawnGroundArmy(
            game,
            profile,
            0,
            sideA.Player,
            [new MassBattleUnitBlock { Definition = profile.Capacity.Blocks[0].Definition, Count = plan.Size }],
            navigation,
            reporter,
            expectedGround: plan.Size);
        var units = spawn.Units;
        if (spawn.Spawned != plan.Size)
        {
            reasons.Add($"{plan.Name}: spawned {spawn.Spawned} of {plan.Size} requested units.");
        }

        var settleFrames = plan.Mode == PathfindingLoadCasePlanner.ArmyAiSquadsMode ? cadence + 1 : SquadlessSettleFrames;
        for (var i = 0; i < settleFrames; i++)
        {
            step();
        }

        var squadsAtOrder = units.Select(unit => hierarchy.SquadOf(unit))
            .Where(squad => squad != null)
            .Select(squad => squad!.Id)
            .Distinct()
            .Count();
        if (plan.Mode == PathfindingLoadCasePlanner.SquadlessMode && squadsAtOrder != 0)
        {
            reasons.Add($"{plan.Name}: expected squad-less selection, {squadsAtOrder} squads at order.");
        }

        if (plan.Mode == PathfindingLoadCasePlanner.ArmyAiSquadsMode && squadsAtOrder == 0)
        {
            reasons.Add($"{plan.Name}: expected squad assignment, selection is squad-less at order.");
        }

        var impassable = plan.DestinationKind == PathfindingLoadCasePlanner.ImpassablePocketDestination;
        var destination = impassable ? impassableDestination : crossMapDestination;

        var statsBefore = navigation.Statistics;
        var asyncSubmittedBefore = navigation.AsyncPathSubmitted;
        _ = navigation.ConsumePathSpan();

        // WI-N2-01: attribute this order's landings and their ring distances cleanly.
        FollowerLanding.ResetLandingRings();
        // WI-N3-01: goal histogram window covers the order itself plus the first window tick,
        // matching how the order-tick path span sample attributes the inline searches.
        navigation.ResetGoalHistogram();
        OrderProcessor.MoveUnits(navigation, sideA.Player, units, destination);
        var landingRings = (long[])FollowerLanding.LandingRingHistogram.Clone();

        // WI-N2-01: offset-shifted retrace waypoints on impassable cells, counted at order time
        // (existing retrace behavior, reported, not fixed here).
        var impassableRetracePoints = 0;
        foreach (var unit in units)
        {
            var ai = unit.AIUpdate;
            if (ai == null)
            {
                continue;
            }

            foreach (var point in ai.TargetPoints)
            {
                if (!navigation.IsPassable(point))
                {
                    impassableRetracePoints++;
                }
            }
        }

        // First window tick emits the span of the inline order searches; the stats delta over
        // the same tick attributes the requests by origin. The order runs between ticks, so
        // the tick-0 request baseline is the pre-order snapshot, not a post-order reading.
        PathStatistics? statsAfterOrderTick = null;
        var orderTickPathSpanMs = 0.0;
        var windowSpanMs = 0.0;
        var windowSyncRequests = 0L;
        var maxGoalNeighbourhoodRequests = 0L;
        var orderTickFullSearches = 0L;
        var perTickEstimates = new List<double>();
        var syncRequestsBeforeTick = statsBefore.SynchronousRequests;
        for (var t = 0; t < WindowTicks; t++)
        {
            step();
            var statsAfterTick = navigation.Statistics;
            var syncDelta = statsAfterTick.SynchronousRequests - syncRequestsBeforeTick;
            syncRequestsBeforeTick = statsAfterTick.SynchronousRequests;
            var spanMs = currentTickPathSpanMs();
            windowSpanMs += spanMs;
            windowSyncRequests += syncDelta;
            if (syncDelta > 0)
            {
                perTickEstimates.Add(spanMs / syncDelta);
            }

            // WI-N3-01: per-tick gauge; reset keeps each tick's bucket maximums separate.
            maxGoalNeighbourhoodRequests = Math.Max(maxGoalNeighbourhoodRequests, statsAfterTick.GoalHistogramMaxBucket);
            if (t == 0)
            {
                orderTickFullSearches = statsAfterTick.GoalHistogramSearches;
            }

            navigation.ResetGoalHistogram();

            if (t == 0)
            {
                statsAfterOrderTick = statsAfterTick;
                orderTickPathSpanMs = spanMs;
            }
        }

        var after = statsAfterOrderTick ?? statsBefore;
        var asyncSubmitted = navigation.AsyncPathSubmitted - asyncSubmittedBefore;
        var result = new PathfindingLoadCaseResult
        {
            Name = plan.Name,
            Mode = plan.Mode,
            DestinationKind = impassable ? impassableKind : plan.DestinationKind,
            RequestedSize = plan.Size,
            SpawnedSize = spawn.Spawned,
            SquadsAtOrder = squadsAtOrder,
            SettleFrames = settleFrames,
            WindowTicks = WindowTicks,
            DestinationX = destination.X,
            DestinationY = destination.Y,
            DestinationZ = destination.Z,
            OrderTickPathSpanMs = orderTickPathSpanMs,
            Requests = new PathfindingLoadRequestCounts
            {
                Unit = after.UnitRequests - statsBefore.UnitRequests,
                SquadLeader = after.SquadLeaderRequests - statsBefore.SquadLeaderRequests,
                BlockedFollower = after.BlockedFollowerRequests - statsBefore.BlockedFollowerRequests,
                Horde = after.HordeRequests - statsBefore.HordeRequests,
                Async = after.AsyncRequests - statsBefore.AsyncRequests,
            },
            SyncRequests = after.SynchronousRequests - statsBefore.SynchronousRequests,
            NodesExpanded = after.NodesExpanded - statsBefore.NodesExpanded,
            FailedSearches = after.FailedSearches - statsBefore.FailedSearches,
            EndpointAdjustments = after.EndpointAdjustments - statsBefore.EndpointAdjustments,
            LandedFollowers = after.LandedFollowers - statsBefore.LandedFollowers,
            FallbackFollowers = after.FallbackFollowers - statsBefore.FallbackFollowers,
            ImpassableRetracePoints = impassableRetracePoints,
            LandingRings = landingRings,
            MaxGoalNeighbourhoodRequests = maxGoalNeighbourhoodRequests,
            OrderTickFullSearches = orderTickFullSearches,
            MeanSearchMs = windowSyncRequests > 0 ? windowSpanMs / windowSyncRequests : 0,
            P99SearchMs = perTickEstimates.Count > 0
                ? BootstrapStatistics.Percentile([.. perTickEstimates.Order()], 0.99)
                : 0,
        };

        if (impassable && plan.Mode == PathfindingLoadCasePlanner.SquadlessMode && result.FailedSearches == 0)
        {
            reasons.Add($"{plan.Name}: unreachable destination produced no failed searches.");
        }

        if (result.SyncRequests == 0 && asyncSubmitted == 0)
        {
            reasons.Add($"{plan.Name}: move order issued no path requests.");
        }

        // Clear the board: destroyed units leave stale squads until the next synchronize, which
        // the next case's explicit selection ignores.
        foreach (var unit in units)
        {
            game.Scene3D.GameObjects.DestroyObject(unit);
        }

        game.Scene3D.GameObjects.DeleteDestroyed();
        return result;
    }

    /// <summary>
    /// Finds a map point inside an unreachable impassable pocket: impassable itself and yielding
    /// empty paths from the spawn zone center and all four zone corners. Falls back to an
    /// off-map point, which fails identically from every start.
    /// </summary>
    private static (Vector3 Destination, string Kind) FindImpassableDestination(
        Game game,
        MassBattleDeploymentProfile profile,
        NavigationGrid navigation,
        MassBattleRect zoneA)
    {
        var bounds = MassBattlePlacement.PlacementBounds(profile);
        var probes = new[]
        {
            MassBattlePlacement.GroundPosition(game, zoneA.CenterX, zoneA.CenterY),
            MassBattlePlacement.GroundPosition(game, zoneA.X, zoneA.Y),
            MassBattlePlacement.GroundPosition(game, zoneA.X + zoneA.Width, zoneA.Y),
            MassBattlePlacement.GroundPosition(game, zoneA.X, zoneA.Y + zoneA.Height),
            MassBattlePlacement.GroundPosition(game, zoneA.X + zoneA.Width, zoneA.Y + zoneA.Height),
        };
        for (var x = bounds.X - ImpassableScanInflation; x <= bounds.X + bounds.Width + ImpassableScanInflation; x += ImpassableScanStride)
        {
            for (var y = bounds.Y - ImpassableScanInflation; y <= bounds.Y + bounds.Height + ImpassableScanInflation; y += ImpassableScanStride)
            {
                var candidate = MassBattlePlacement.GroundPosition(game, x, y);
                if (navigation.IsPassable(candidate))
                {
                    continue;
                }

                if (probes.Any(probe => navigation.CalculatePath(probe, candidate, out _).Count != 0))
                {
                    continue;
                }

                return (candidate, PathfindingLoadCasePlanner.ImpassablePocketDestination);
            }
        }

        return (probes[0] + new Vector3(1_000_000f, 0f, 0f), PathfindingLoadCasePlanner.OffMapFallbackDestination);
    }

    private static void AppendCaseStatistics(BenchmarkRunRecord record, uint seed, PathfindingLoadCaseResult c)
    {
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.orderTickPathSpanMs", [c.OrderTickPathSpanMs], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.syncRequests", [c.SyncRequests], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.nodesExpanded", [c.NodesExpanded], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.failedSearches", [c.FailedSearches], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.meanSearchMs", [c.MeanSearchMs], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.p99SearchMs", [c.P99SearchMs], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.landedFollowers", [c.LandedFollowers], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.fallbackFollowers", [c.FallbackFollowers], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.impassableRetracePoints", [c.ImpassableRetracePoints], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.maxGoalNeighbourhoodRequests", [c.MaxGoalNeighbourhoodRequests], seed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples($"case.{c.Name}.orderTickFullSearches", [c.OrderTickFullSearches], seed));
    }

    private static string? ResolveProfilePath(TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        if (!string.IsNullOrEmpty(config.PathfindingLoadProfilePath))
        {
            if (!File.Exists(config.PathfindingLoadProfilePath))
            {
                reporter.Error($"Pathfinding load profile not found: {config.PathfindingLoadProfilePath}");
                return null;
            }

            return config.PathfindingLoadProfilePath;
        }

        var defaultPath = Path.Combine(AppContext.BaseDirectory, MassBattleCapacityProbeRunner.DefaultProfileFileName);
        if (!File.Exists(defaultPath))
        {
            reporter.Error($"Default capacity probe profile not found: {defaultPath}");
            return null;
        }

        return defaultPath;
    }
}
