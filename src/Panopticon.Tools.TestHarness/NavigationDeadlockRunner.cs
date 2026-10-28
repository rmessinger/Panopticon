using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Data;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Navigation;
using Panopticon.Network;
using Panopticon.Simulation;
using NavigationGrid = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-N6-02: the navigation-deadlock scenario. Each case reproduces one deadlock mechanism
/// from the deep-dive case table on the alpine assault map, runs until every ordered unit
/// settles (route exhausted or inside the arrival radius) or the per-case frame budget ends,
/// samples per-unit stall state with <see cref="NavigationStallSampler"/>, and records the
/// baseline. Deadlocks are findings, not failures: a case that never settles still exits 0.
/// </summary>
public static class NavigationDeadlockRunner
{
    public const string ScenarioId = "NavigationDeadlock";

    private const int ExitSetupError = 1;
    private const string DefaultMapPath = "maps/alpine assault/alpine assault.map";
    private const string DefaultReportPath = "artifacts/navigation-deadlock/navigation-deadlock.json";
    private const string UnitDefinitionName = "AmericaTankCrusader";
    private const string WallDefinitionName = "AmericaPowerPlant";

    // Wall geometry mirrors RoutingChokeRunner: the wall runs along X at WallY with a gap at
    // CorridorCenterX; the corridor cases reuse it.
    private const float CorridorCenterX = 1000f;
    private const float WallY = 1000f;
    private const float WallSpanFrom = 550f;
    private const float WallSpanTo = 1450f;
    private const float WallSpacing = 80f;
    private const float UnitSpacing = 60f;
    private const float ArrivalRadius = 90f;
    private const int SettleFrames = 30;
    private const int RetraceSampleStride = 10;

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(reporter);

        var frameBudget = config.NavigationDeadlockFrameBudget;
        var seed = config.NavigationDeadlockSeed;
        var mapPath = string.IsNullOrWhiteSpace(config.NavigationDeadlockMapPath) ? DefaultMapPath : config.NavigationDeadlockMapPath!;
        var reportPath = config.NavigationDeadlockReportPath ?? DefaultReportPath;
        if (frameBudget < 1 || seed < 1)
        {
            reporter.Error("NavigationDeadlock knobs must satisfy: frame budget >= 1, seed >= 1.");
            return ExitSetupError;
        }

        var plans = NavigationDeadlockCasePlanner.FilterCases(
            NavigationDeadlockCasePlanner.BuildCases(), config.NavigationDeadlockCaseFilter);
        if (plans.Count == 0)
        {
            reporter.Error($"NavigationDeadlock case filter '{config.NavigationDeadlockCaseFilter}' matched no cases.");
            return ExitSetupError;
        }

        reporter.Info($"NavigationDeadlock: {plans.Count} case(s), budget {frameBudget} frames each, seed {seed}, map {mapPath}.");

        var results = new List<NavigationDeadlockCaseResult>(plans.Count);
        foreach (var plan in plans)
        {
            var result = RunCase(installation, config, plan, mapPath, frameBudget, seed, reporter);
            if (result == null)
            {
                return ExitSetupError;
            }

            results.Add(result);
        }

        var report = new NavigationDeadlockReport(
            NavigationDeadlockReport.CurrentSchemaVersion,
            mapPath,
            seed,
            frameBudget,
            config.NavigationDeadlockValidateOffsetRoutes,
            results);
        report.WriteJson(reportPath);
        report.WriteText(Path.ChangeExtension(reportPath, ".txt"));
        reporter.Info($"NavigationDeadlock report saved to: {reportPath}");

        var unsettled = results.Where(result => result.SettledUnits < result.UnitsSpawned).Select(result => result.Name).ToArray();
        reporter.Info(
            unsettled.Length == 0
                ? "NavigationDeadlock: every case settled within budget."
                : $"NavigationDeadlock: {unsettled.Length} case(s) did not settle: {string.Join(", ", unsettled)}.");
        return 0;
    }

    private sealed class CaseSetup
    {
        public required List<GameObject> OrderedUnits { get; init; }

        public required Dictionary<uint, Vector3> Goals { get; init; }

        public required List<GameObject> IdleUnits { get; init; }

        public bool AsyncOrderPaths { get; init; }

        public required string SetupNote { get; set; }

        public required Action IssueOrders { get; init; }

        public Action? AfterSettle { get; set; }

        // WI-N6-02: async-no-route injects its worker failure only after the pocket rejects
        // have applied, keeping the pocket requests' completions attributable (pre-N7 a
        // failed request at the queue head also blocked every request behind it; failures
        // now complete as Failed and advance the queue). Frame counts from IssueOrders.
        public int? LateInjectFrame { get; set; }

        public Action? LateInject { get; set; }
    }

    private static NavigationDeadlockCaseResult? RunCase(
        GameInstallation installation,
        TestHarnessConfig config,
        NavigationDeadlockCasePlan plan,
        string mapPath,
        int frameBudget,
        int seed,
        ITestHarnessReporter reporter)
    {
        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true, SkipStartingBuildings = true },
            null);

        var playerSettings = new[]
        {
            new PlayerSetting(0, "FactionAmerica", new ColorRgb(64, 159, 255), 0, PlayerOwner.EasyAi, "NavDeadlock"),
        };
        game.StartSkirmishOrMultiPlayerGame(mapPath, new EchoConnection(), playerSettings, seed, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();

        var unitDefinition = game.AssetStore.ObjectDefinitions.GetByName(UnitDefinitionName);
        var wallDefinition = game.AssetStore.ObjectDefinitions.GetByName(WallDefinitionName);
        if (unitDefinition == null || wallDefinition == null)
        {
            reporter.Error($"Unknown object definition '{(unitDefinition == null ? UnitDefinitionName : WallDefinitionName)}'.");
            return null;
        }

        var owner = game.Scene3D.Players[2];
        var neutral = game.Scene3D.Players[0];
        var navigation = game.Scene3D.Navigation;
        var orderProcessor = new OrderProcessor(game);

        var setup = plan.Name switch
        {
            NavigationDeadlockCasePlanner.HeadOnPair => SetupHeadOnPair(game, unitDefinition, owner, orderProcessor),
            NavigationDeadlockCasePlanner.CorridorOpposed20 => SetupCorridorOpposed(
                game, unitDefinition, wallDefinition, owner, neutral, orderProcessor),
            NavigationDeadlockCasePlanner.CrowdGoal100 => SetupCrowdGoal(game, unitDefinition, owner, orderProcessor),
            NavigationDeadlockCasePlanner.QueueBehindIdle => SetupQueueBehindIdle(
                game, unitDefinition, wallDefinition, owner, neutral, orderProcessor),
            NavigationDeadlockCasePlanner.AsyncNoRoute => SetupAsyncNoRoute(
                game, unitDefinition, wallDefinition, owner, neutral, navigation, orderProcessor, reporter),
            NavigationDeadlockCasePlanner.SquadOffsetRetrace => SetupSquadOffsetRetrace(
                game, unitDefinition, wallDefinition, owner, neutral, navigation),
            _ => null,
        };
        if (setup == null)
        {
            reporter.Error($"NavigationDeadlock case '{plan.Name}' is unknown.");
            return null;
        }

        navigation.Options.AsyncOrderPaths = setup.AsyncOrderPaths;
        navigation.Options.ValidateOffsetRoutes = config.NavigationDeadlockValidateOffsetRoutes;

        // Structures mark their grid cells over the first ticks; orders are deferred until the
        // furniture settles so paths and region labels see the walls.
        for (var frame = 0; frame < SettleFrames; frame++)
        {
            game.Update(Array.Empty<Input.InputMessage>());
        }

        setup.AfterSettle?.Invoke();

        List<GameObject> allUnits = [.. setup.OrderedUnits, .. setup.IdleUnits];
        var syncRequestsBefore = NavigationGrid.PathRequestCount;
        var asyncSubmittedBefore = navigation.AsyncPathSubmitted;
        var asyncAppliedBefore = navigation.AsyncPathApplied;
        var asyncCancelledBefore = navigation.AsyncPathCancelled;
        var statsBefore = navigation.Statistics;

        setup.IssueOrders();

        var sampler = new NavigationStallSampler();
        var settled = new HashSet<uint>();
        var framesToSettle = (int?)null;
        var totalPathSpanMs = 0.0;
        var maxTickPathSpanMs = 0.0;
        var currentTickPathSpanMs = 0.0;
        var maxImpassableRetracePoints = 0;
        var framesRun = 0;
        var terminationReason = "BudgetEnded";

        // The logic stepper discards unconsumed path spans on ticks without a span sink, so the
        // case reads them through the span event instead of ConsumePathSpan.
        void OnSystemSpan(string system, TimeSpan span)
        {
            if (system == "path")
            {
                currentTickPathSpanMs += span.TotalMilliseconds;
            }
        }

        game.LogicStepper.SystemSpan += OnSystemSpan;
        try
        {
            for (; framesRun < frameBudget; framesRun++)
            {
                currentTickPathSpanMs = 0.0;
                game.Update(Array.Empty<Input.InputMessage>());
                totalPathSpanMs += currentTickPathSpanMs;
                maxTickPathSpanMs = Math.Max(maxTickPathSpanMs, currentTickPathSpanMs);

                sampler.Sample((uint)framesRun, allUnits);

                if (setup.LateInjectFrame == framesRun + 1)
                {
                    setup.LateInject?.Invoke();
                }

                if (framesRun % RetraceSampleStride == 0)
                {
                    maxImpassableRetracePoints = Math.Max(
                        maxImpassableRetracePoints, CountImpassableRetracePoints(navigation, setup.OrderedUnits));
                }

                var allSettled = true;
                foreach (var unit in setup.OrderedUnits)
                {
                    if (settled.Contains(unit.Id.Index))
                    {
                        continue;
                    }

                    var aiUpdate = unit.AIUpdate;
                    // Only units that have actually been ordered can settle; the async-no-route
                    // victim stays un-ordered until its late injection.
                    var activated = aiUpdate != null
                        && (aiUpdate.TargetPoints.Count > 0
                            || aiUpdate.Progress.Reason != NavigationStallReason.None);
                    var exhausted = activated
                        && aiUpdate.TargetPoints.Count == 0
                        && aiUpdate.Progress.Reason != NavigationStallReason.AwaitingPath;
                    var goal = setup.Goals[unit.Id.Index];
                    var delta = unit.Translation - goal;
                    if (exhausted || delta.Vector2XY().LengthSquared() < ArrivalRadius * ArrivalRadius)
                    {
                        settled.Add(unit.Id.Index);
                    }

                    allSettled &= settled.Contains(unit.Id.Index);
                }

                if (allSettled)
                {
                    framesToSettle = framesRun + 1;
                    framesRun++;
                    terminationReason = "AllSettled";
                    break;
                }
            }
        }
        finally
        {
            game.LogicStepper.SystemSpan -= OnSystemSpan;
        }

        var records = sampler.Records;
        var cycles = sampler.WaitForCycles;
        var reroutedFollowers = (int)(navigation.Statistics.ReroutedFollowers - statsBefore.ReroutedFollowers);
        var (confirmed, note) = EvaluateMechanism(plan.Name, setup, records, cycles, settled.Count, maxImpassableRetracePoints, sampler.CurrentStalls, reroutedFollowers, config.NavigationDeadlockValidateOffsetRoutes);

        var result = new NavigationDeadlockCaseResult(
            plan.Name,
            plan.Mechanism,
            setup.OrderedUnits.Count,
            settled.Count,
            framesRun,
            terminationReason,
            framesToSettle,
            totalPathSpanMs,
            maxTickPathSpanMs,
            NavigationGrid.PathRequestCount - syncRequestsBefore,
            navigation.AsyncPathSubmitted - asyncSubmittedBefore,
            navigation.AsyncPathApplied - asyncAppliedBefore,
            navigation.AsyncPathCancelled - asyncCancelledBefore,
            maxImpassableRetracePoints,
            reroutedFollowers,
            confirmed,
            note,
            [.. records],
            [.. cycles]);

        reporter.Info(
            $"NavigationDeadlock {plan.Name}: {settled.Count}/{setup.OrderedUnits.Count} settled"
            + (framesToSettle.HasValue ? $" at frame {framesToSettle}" : $" after {framesRun} frames ({terminationReason})")
            + $", {records.Count} stall records, {cycles.Count} wait-for cycle(s), mechanism {(confirmed ? "confirmed" : "NOT reproduced")}.");

        return result;
    }

    private static CaseSetup SetupHeadOnPair(
        Game game, ObjectDefinition unitDefinition, Player owner, OrderProcessor orderProcessor)
    {
        var first = Spawn(game, unitDefinition, owner, CorridorCenterX - 120, 800);
        var second = Spawn(game, unitDefinition, owner, CorridorCenterX + 120, 800);

        void IssueOrders()
        {
            // Each tank is ordered to the other's start; the straight lane runs through both.
            orderProcessor.Process(
            [
                Order.CreateSetSelection(2, [first.Id]),
                Order.CreateMoveOrder(2, second.Translation),
            ]);
            orderProcessor.Process(
            [
                Order.CreateSetSelection(2, [second.Id]),
                Order.CreateMoveOrder(2, first.Translation),
            ]);
        }

        return new CaseSetup
        {
            OrderedUnits = [first, second],
            Goals = new Dictionary<uint, Vector3>
            {
                [first.Id.Index] = second.Translation,
                [second.Id.Index] = first.Translation,
            },
            IdleUnits = [],
            AsyncOrderPaths = false,
            SetupNote = "Two single-unit orders through one lane.",
            IssueOrders = IssueOrders,
        };
    }

    private static CaseSetup SetupCorridorOpposed(
        Game game, ObjectDefinition unitDefinition, ObjectDefinition wallDefinition, Player owner, Player neutral, OrderProcessor orderProcessor)
    {
        BuildWallWithGap(game, wallDefinition, neutral, gapWidth: 120);

        var south = SpawnBlock(game, unitDefinition, owner, CorridorCenterX, 700, columns: 5, rows: 4);
        var north = SpawnBlock(game, unitDefinition, owner, CorridorCenterX, 1400, columns: 5, rows: 4);

        var southGoal = new Vector3(CorridorCenterX, 1500, 0);
        var northGoal = new Vector3(CorridorCenterX, 640, 0);

        void IssueOrders()
        {
            orderProcessor.Process(
            [
                Order.CreateSetSelection(2, south.Select(unit => unit.Id)),
                Order.CreateMoveOrder(2, southGoal),
            ]);
            orderProcessor.Process(
            [
                Order.CreateSetSelection(2, north.Select(unit => unit.Id)),
                Order.CreateMoveOrder(2, northGoal),
            ]);
        }

        return new CaseSetup
        {
            OrderedUnits = [.. south, .. north],
            Goals = south.ToDictionary(unit => unit.Id.Index, _ => southGoal)
                .Concat(north.ToDictionary(unit => unit.Id.Index, _ => northGoal))
                .ToDictionary(),
            IdleUnits = [],
            AsyncOrderPaths = false,
            SetupNote = "Two 20-unit selections ordered through the same gap in opposite directions.",
            IssueOrders = IssueOrders,
        };
    }

    private static CaseSetup SetupCrowdGoal(Game game, ObjectDefinition unitDefinition, Player owner, OrderProcessor orderProcessor)
    {
        var units = SpawnBlock(game, unitDefinition, owner, CorridorCenterX, 700, columns: 10, rows: 10);
        var goal = new Vector3(CorridorCenterX, 1100, 0);

        void IssueOrders()
        {
            orderProcessor.Process(
            [
                Order.CreateSetSelection(2, units.Select(unit => unit.Id)),
                Order.CreateMoveOrder(2, goal),
            ]);
        }

        return new CaseSetup
        {
            OrderedUnits = units,
            Goals = units.ToDictionary(unit => unit.Id.Index, _ => goal),
            IdleUnits = [],
            AsyncOrderPaths = false,
            SetupNote = "One 100-unit selection ordered to a single shared point.",
            IssueOrders = IssueOrders,
        };
    }

    private static CaseSetup SetupQueueBehindIdle(
        Game game, ObjectDefinition unitDefinition, ObjectDefinition wallDefinition, Player owner, Player neutral, OrderProcessor orderProcessor)
    {
        BuildWallWithGap(game, wallDefinition, neutral, gapWidth: 120);

        // Two idle tanks straddle the gap center so no tank-wide slot remains at the choke;
        // the single-file column queues behind them instead of jamming among itself.
        var idleA = Spawn(game, unitDefinition, owner, CorridorCenterX - 30, WallY);
        var idleB = Spawn(game, unitDefinition, owner, CorridorCenterX + 30, WallY);

        var column = SpawnBlock(game, unitDefinition, owner, CorridorCenterX, 700, columns: 1, rows: 10);
        var goal = new Vector3(CorridorCenterX, 1280, 0);

        void IssueOrders()
        {
            orderProcessor.Process(
            [
                Order.CreateSetSelection(2, column.Select(unit => unit.Id)),
                Order.CreateMoveOrder(2, goal),
            ]);
        }

        return new CaseSetup
        {
            OrderedUnits = column,
            Goals = column.ToDictionary(unit => unit.Id.Index, _ => goal),
            IdleUnits = [idleA, idleB],
            AsyncOrderPaths = false,
            SetupNote = "Column through a gap choked by two idle friendly tanks.",
            IssueOrders = IssueOrders,
        };
    }

    private static CaseSetup SetupAsyncNoRoute(
        Game game,
        ObjectDefinition unitDefinition,
        ObjectDefinition wallDefinition,
        Player owner,
        Player neutral,
        NavigationGrid navigation,
        OrderProcessor orderProcessor,
        ITestHarnessReporter reporter)
    {
        var pocketCenter = new Vector3(CorridorCenterX, 1350, 0);
        BuildSealedPocket(game, wallDefinition, neutral, pocketCenter, innerRadius: 90, outerRadius: 200, pitch: 30);

        var units = SpawnBlock(game, unitDefinition, owner, CorridorCenterX, 700, columns: 5, rows: 1);
        var victim = Spawn(game, unitDefinition, owner, 700, 700);
        var victimGoal = new Vector3(700, 1400, 0);

        void IssueOrders()
        {
            orderProcessor.Process(
            [
                Order.CreateSetSelection(2, units.Select(unit => unit.Id)),
                Order.CreateMoveOrder(2, pocketCenter),
            ]);
        }

        CaseSetup? setup = null;
        setup = new CaseSetup
        {
            OrderedUnits = [.. units, victim],
            Goals = units.ToDictionary(unit => unit.Id.Index, _ => pocketCenter),
            IdleUnits = [],
            AsyncOrderPaths = true,
            SetupNote = "Async order paths on.",
            IssueOrders = IssueOrders,
            LateInjectFrame = 90,
            LateInject = () =>
            {
                // Phase 2, after the pocket rejects applied: one request dies the way an
                // unexpected worker exception does, cancelled without a callback, so the
                // victim awaits forever. Injecting earlier would also strand every request
                // queued behind the failed head.
                navigation.ForceNextWorkerFailure();
                orderProcessor.Process(
                [
                    Order.CreateSetSelection(2, [victim.Id]),
                    Order.CreateMoveOrder(2, victimGoal),
                ]);
            },
        };
        setup.Goals[victim.Id.Index] = victimGoal;
        setup.AfterSettle = () =>
        {
            // Probe the pocket after the structures mark their cells, but before the counters
            // snapshot: the probe's request stays out of the case totals. A sealed pocket
            // keeps its interior passable but region-isolated, so the probe returns empty.
            var probe = navigation.CalculatePath(units[0].Translation, pocketCenter, out _);
            var sealedPocket = navigation.IsPassable(pocketCenter) && probe.Count == 0;
            setup.SetupNote = $"Pocket sealed: {sealedPocket}. Async order paths on, worker failure at frame 90.";
            if (!sealedPocket)
            {
                reporter.Info("NavigationDeadlock async-no-route: pocket probe did not seal; recording as not reproduced.");
            }
        };

        return setup;
    }

    private static CaseSetup SetupSquadOffsetRetrace(
        Game game, ObjectDefinition unitDefinition, ObjectDefinition wallDefinition, Player owner, Player neutral, NavigationGrid navigation)
    {
        // A closed wall row forces the squad march around its east end; the block spawns
        // adjacent to the row so follower offsets retrace along the wall face.
        for (var x = 700f; x <= 1300f; x += WallSpacing)
        {
            Spawn(game, wallDefinition, neutral, x, WallY);
        }

        var members = new List<GameObject>(8);
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                members.Add(Spawn(game, unitDefinition, owner, 850 + column * UnitSpacing, 930 - row * UnitSpacing));
            }
        }

        var destination = new Vector3(1500, 1050, 0);

        void IssueOrders()
        {
            GroupMovement.MoveSquad(navigation, members, destination);
        }

        return new CaseSetup
        {
            OrderedUnits = members,
            Goals = members.ToDictionary(unit => unit.Id.Index, _ => destination),
            IdleUnits = [],
            AsyncOrderPaths = false,
            SetupNote = "Squad move around a wall end with the block adjacent to the wall face.",
            IssueOrders = IssueOrders,
        };
    }

    private static (bool Confirmed, string Note) EvaluateMechanism(
        string name,
        CaseSetup setup,
        IReadOnlyList<NavigationStallRecord> records,
        IReadOnlyList<NavigationWaitForCycle> cycles,
        int settledUnits,
        int impassableRetracePoints,
        IReadOnlyList<NavigationStallRecord> currentStalls,
        int reroutedFollowers,
        bool validateOffsetRoutes)
    {
        var maxFrames = records.Count > 0 ? records.Max(record => record.FramesWithoutProgress) : 0;
        switch (name)
        {
            case NavigationDeadlockCasePlanner.HeadOnPair:
                {
                    var blocked = records.Where(record => record.Reason == nameof(NavigationStallReason.BlockedByUnit)).ToList();
                    return (cycles.Count >= 1 && blocked.Count >= 2,
                        $"{blocked.Count} mutually blocked records, {cycles.Count} wait-for cycle(s), max {maxFrames} frames without progress.");
                }

            case NavigationDeadlockCasePlanner.CorridorOpposed20:
                {
                    var blocked = records.Count(record => record.Reason == nameof(NavigationStallReason.BlockedByUnit));
                    return (settledUnits < setup.OrderedUnits.Count && blocked > 0,
                        $"{blocked} blocked records, {cycles.Count} wait-for cycle(s), {settledUnits}/{setup.OrderedUnits.Count} settled, max {maxFrames} frames.");
                }

            case NavigationDeadlockCasePlanner.CrowdGoal100:
                {
                    var crowdWait = records.Count(record => record.Reason == nameof(NavigationStallReason.None));
                    return (settledUnits < setup.OrderedUnits.Count,
                        $"{settledUnits}/{setup.OrderedUnits.Count} settled, {crowdWait} unattributed crowd-wait records, max {maxFrames} frames.");
                }

            case NavigationDeadlockCasePlanner.QueueBehindIdle:
                {
                    var idleIds = setup.IdleUnits.Select(unit => unit.Id.Index).ToHashSet();
                    var behindIdle = records.Count(record =>
                        record.Reason == nameof(NavigationStallReason.BlockedByUnit) && idleIds.Contains(record.BlockerId));
                    return (behindIdle > 0,
                        $"{behindIdle} records blocked by the idle tanks, {settledUnits}/{setup.OrderedUnits.Count} settled, max {maxFrames} frames.");
                }

            case NavigationDeadlockCasePlanner.AsyncNoRoute:
                {
                    var noRoute = records.Count(record => record.Reason == nameof(NavigationStallReason.NoRoute));
                    var stillAwaiting = currentStalls.Count(record => record.Reason == nameof(NavigationStallReason.AwaitingPath));
                    // WI-N7-01: worker failures complete as Failed and settle the requester, so
                    // liveness holds when the pocket settled NoRoute and nothing still awaits.
                    return (noRoute > 0 && stillAwaiting == 0,
                        $"{noRoute} NoRoute records, {stillAwaiting} units still awaiting at end (WI-N7-01 expects 0; pre-N7 the victim awaited the whole budget), {settledUnits}/{setup.OrderedUnits.Count} settled. {setup.SetupNote}");
                }

            case NavigationDeadlockCasePlanner.SquadOffsetRetrace:
                {
                    // WI-N7-02: with offset validation on the mechanism is remediated instead
                    // of reproduced: followers reroute onto the leader route and no retrace
                    // point lands on an impassable cell.
                    if (validateOffsetRoutes)
                    {
                        return (reroutedFollowers > 0 && impassableRetracePoints == 0,
                            $"{reroutedFollowers} followers rerouted onto the leader route, {impassableRetracePoints} impassable retrace points (WI-N7-02), {settledUnits}/{setup.OrderedUnits.Count} settled.");
                    }

                    return (impassableRetracePoints > 0,
                        $"{impassableRetracePoints} impassable retrace points observed, {settledUnits}/{setup.OrderedUnits.Count} settled.");
                }

            default:
                {
                    return (false, "Unknown case.");
                }
        }
    }

    private static int CountImpassableRetracePoints(NavigationGrid navigation, IReadOnlyList<GameObject> units)
    {
        var count = 0;
        foreach (var unit in units)
        {
            var targetPoints = unit.AIUpdate?.TargetPoints;
            if (targetPoints == null)
            {
                continue;
            }

            foreach (var point in targetPoints)
            {
                if (!navigation.IsPassable(point))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void BuildWallWithGap(Game game, ObjectDefinition wallDefinition, Player neutral, float gapWidth)
    {
        var wallCount = 0;
        for (var x = WallSpanFrom; x <= WallSpanTo; x += WallSpacing)
        {
            if (MathF.Abs(x - CorridorCenterX) < gapWidth / 2f + WallSpacing / 2f)
            {
                continue;
            }

            Spawn(game, wallDefinition, neutral, x, WallY);
            wallCount++;
        }
    }

    private static void BuildSealedPocket(
        Game game, ObjectDefinition wallDefinition, Player neutral, Vector3 center, float innerRadius, float outerRadius, float pitch)
    {
        // A hollow annulus on a rect grid, not a ring of segments: cell marking uses each
        // collider's axis-aligned rect with cell-center containment, so a grid with pitch
        // below the footprint in both axes tiles watertight, while the hollow interior keeps
        // passable, region-isolated cells for the unreachable goal.
        var innerCellsSq = (innerRadius / pitch) * (innerRadius / pitch);
        var outerCellsSq = (outerRadius / pitch) * (outerRadius / pitch);
        var extent = MathF.Ceiling(outerRadius / pitch);
        for (var dy = -extent; dy <= extent; dy++)
        {
            for (var dx = -extent; dx <= extent; dx++)
            {
                var cellsSq = dx * dx + dy * dy;
                if (cellsSq < innerCellsSq || cellsSq > outerCellsSq)
                {
                    continue;
                }

                Spawn(game, wallDefinition, neutral, center.X + dx * pitch, center.Y + dy * pitch);
            }
        }
    }

    private static List<GameObject> SpawnBlock(
        Game game, ObjectDefinition definition, Player owner, float centerX, float centerY, int columns, int rows)
    {
        var units = new List<GameObject>(columns * rows);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = centerX + (column - (columns - 1) / 2f) * UnitSpacing;
                var y = centerY - row * UnitSpacing;
                units.Add(Spawn(game, definition, owner, x, y));
            }
        }

        return units;
    }

    private static GameObject Spawn(Game game, ObjectDefinition definition, Player owner, float x, float y)
    {
        var planar = new Vector2(x, y);
        var height = game.Scene3D.Terrain.GetGroundHeight(planar) ?? 0f;
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        gameObject.UpdateTransform(
            new Vector3(planar.X, planar.Y, height),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f));
        gameObject.UpdateColliders();

        return gameObject;
    }
}
