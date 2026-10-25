using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Navigation;
using Panopticon.Network;

// The Navigation class shares its name with its namespace; alias it to keep call sites readable.
using NavigationGrid = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-M1-01 engagement scenario: direct-spawns two hostile 1000-unit armies (ground blocks plus
/// aircraft parked at pre-placed airfields) from one profile on the mass-battle map, orders a
/// scripted general attack, and records per-side casualties and per-jet sortie cycles. The same
/// definition runs headless and windowed. Aircraft rearm is observed as the full sortie cycle the
/// airfield already provides: park, heal, clip refill, then a re-issued order launches again.
///
/// There is no engine AI here; orders are scripted (M4 owns army-scale AI). WI-M1-02 attaches the
/// B1 measurement stack to this same loop.
/// </summary>
public static class MassBattleCapacityProbeRunner
{
    public const string DefaultProfileFileName = "mass-battle-deepdivide-capacity-probe.json";

    private const string DefaultReportFileName = "mass-battle-capacity-probe-report.json";

    // B1 record identity for the measured engagement (WI-M1-02).
    private const string BenchmarkScenarioId = "m1.capacity-probe";
    private const uint ProbeSeed = 1;

    internal const int DefaultFrameBudget = 9000;

    private const int SettleFrames = 30;

    // Re-issue cadence for scripted orders: re-target idle units and re-parked jets every 300
    // frames while enemies remain. Nearest-enemy search is a brute-force scan; the interval keeps
    // that cost out of the per-tick measurement.
    private const int OrderRefreshFrames = 300;

    // Exit codes follow the PhysicsStack convention: 1 setup error, 5 unmet acceptance check.
    private const int ExitSetupError = 1;
    private const int ExitCheckFailed = 5;

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
            reporter.Error($"Could not load capacity probe profile '{profilePath}': {ex.Message}");
            return ExitSetupError;
        }

        var validationError = ValidateProfile(profile);
        if (validationError != null)
        {
            reporter.Error(validationError);
            return ExitSetupError;
        }

        var frameBudget = config.MassBattleCapacityProbeFrameBudget ?? DefaultFrameBudget;

        // Same log flood as the deployment proof: spawning two armies drowns stdout in Info lines.
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
        GameWindow? window = null;
        TextureCopier? textureCopier = null;
        HarnessOverlay? overlay = null;
        try
        {
            if (config.Headless)
            {
                game = new Game(
                    installation,
                    config.Renderer,
                    new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud },
                    null);
            }
            else
            {
                Platform.Start();
                window = new GameWindow("Panopticon - Mass Battle Capacity Probe", 100, 100, 1280, 800, config.Fullscreen);
                game = new Game(
                    installation,
                    config.Renderer,
                    new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud },
                    window);
                textureCopier = new TextureCopier(game, window.Swapchain.Framebuffer.OutputDescription);
                overlay = new HarnessOverlay(game, window);
            }

            // Hostile teams: the side index is the team index, so the armies fight on contact.
            // Army-AI mode (WI-M4-03): both sides run the hierarchical decision policy through
            // the shared ArmyAiSides setup (WI-M5-01).
            var armyAi = config.MassBattleCapacityProbeArmyAi == true;
            var playerSettings = profile.Sides.Select((side, index) => new PlayerSetting(
                index,
                side.Side,
                MassBattlePlacement.ParseColor(side.Color, index),
                index,
                PlayerOwner.EasyAi,
                side.Id)
            ).ToArray();
            if (armyAi)
            {
                ArmyAiSides.ApplyPolicies(playerSettings);
            }

            game.StartSkirmishOrMultiPlayerGame(profile.Map.Path, new EchoConnection(), playerSettings, (int)ProbeSeed, isMultiPlayer: false);
            game.StartRun();
            CameraZoomInitializer.ApplyOnce(game, config.MaxCameraZoomMultiplier);
            game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
            IReadOnlyList<ArmyAiSide>? armyAiSides = armyAi
                ? ArmyAiSides.Resolve(game.Scene3D.Players, profile.Sides.Select(side => side.Id).ToArray())
                : null;
            var sidePlayers = armyAiSides is { } resolvedSides
                ? resolvedSides.Select(side => side.Player).ToList()
                : ArmyAiSides.SidePlayers(game.Scene3D.Players, profile.Sides.Count);

            if (armyAiSides is null)
            {
                foreach (var player in game.Scene3D.Players)
                {
                    player.DisableAI();
                }
            }

            if (config.Headless)
            {
                game.EnableFixedLogicTimestep();
            }

            FrameDeployment(game, profile);

            Action step = config.Headless
                ? () => game.Update(Array.Empty<InputMessage>())
                : () => HarnessWindowedFrame.Step(game, window!, textureCopier!, overlay!);

            var navigation = game.Scene3D.Navigation;
            var aircraft = profile.Capacity.Aircraft!;
            var reasons = new List<string>();

            var armies = new List<SideArmy>();
            for (var i = 0; i < profile.Sides.Count; i++)
            {
                armies.Add(SpawnSide(game, profile, i, sidePlayers[i], navigation, aircraft, reporter, reasons));
            }

            for (var i = 0; i < SettleFrames; i++)
            {
                step();
            }

            if (!armyAi)
            {
                IssueGeneralAttack(game, armies);
            }

            var invariantSampler = armyAiSides is { } samplerSides
                ? MassBattleSquadInvariantSampler.Create(samplerSides)
                : null;

            var anchorCadence = config.MassBattleCapacityProbeAsyncAnchorBurstEveryFrames;
            MassBattleAsyncPathAnchor? anchor = anchorCadence is > 0
                ? new MassBattleAsyncPathAnchor(
                    navigation,
                    anchorCadence.Value,
                    config.MassBattleCapacityProbeAsyncAnchorRequestsPerBurst ?? 8)
                : null;

            var (framesRun, benchmarkRecord) = RunEngagement(
                game, armies, step, frameBudget, config.Headless, reporter, anchor, invariantSampler, scriptedOrders: !armyAi);
            var stopReason = DescribeStopReason(armies, framesRun, frameBudget);

            string? screenshotPath = null;
            if (!string.IsNullOrEmpty(config.MassBattleCapacityProbeScreenshotPath))
            {
                screenshotPath = CaptureScreenshot(game, config.MassBattleCapacityProbeScreenshotPath!, reporter);
            }

            var report = BuildReport(
                profile, profilePath, config.Headless, armies, reasons, frameBudget, framesRun, stopReason, screenshotPath,
                anchor?.Build(), invariantSampler?.Build(), armyAiSides);
            reporter.Info(MassBattleCapacityProbeReport.WriteText(report));

            var reportPath = string.IsNullOrEmpty(config.MassBattleCapacityProbeReportPath)
                ? System.IO.Path.Combine(AppContext.BaseDirectory, DefaultReportFileName)
                : config.MassBattleCapacityProbeReportPath!;
            MassBattleCapacityProbeReport.WriteJson(report, reportPath);
            reporter.Info($"Report JSON saved to: {reportPath}");

            // B1 record beside the engagement report (WI-M1-02): same writers and schema as
            // every other benchmark run, so A2/A5 tooling consumes it unchanged.
            var reportDirectory = System.IO.Path.GetDirectoryName(reportPath) ?? AppContext.BaseDirectory;
            var benchmarkJsonPath = System.IO.Path.Combine(
                reportDirectory, System.IO.Path.GetFileNameWithoutExtension(reportPath) + "-benchmark.json");
            var benchmarkCsvPath = System.IO.Path.ChangeExtension(benchmarkJsonPath, ".csv");
            BenchmarkReportWriter.WriteJson(benchmarkRecord, benchmarkJsonPath);
            BenchmarkReportWriter.WriteCsv(benchmarkRecord, benchmarkCsvPath);
            reporter.Info($"B1 benchmark JSON saved to: {benchmarkJsonPath}");
            reporter.Info($"B1 benchmark CSV saved to: {benchmarkCsvPath}");

            return report.AllPassed ? 0 : ExitCheckFailed;
        }
        finally
        {
            overlay?.Dispose();
            textureCopier?.Dispose();
            game?.Dispose();
            window?.Dispose();
            if (!config.Headless)
            {
                Platform.Stop();
            }
        }
    }

    internal static string? ValidateProfile(MassBattleDeploymentProfile profile)
    {
        if (profile.Sides.Count != 2)
        {
            return $"Capacity probe profile needs exactly 2 sides, found {profile.Sides.Count}.";
        }

        var aircraft = profile.Capacity.Aircraft;
        if (aircraft == null || aircraft.Count <= 0)
        {
            return "Capacity probe profile needs capacity.aircraft with a positive count (the probe parks aircraft at airfields).";
        }

        var total = profile.Capacity.TotalUnitsPerSide;
        if (total != profile.Capacity.UnitsPerSide)
        {
            return $"Ground blocks ({profile.Capacity.GroundUnitsPerSide}) plus aircraft ({aircraft.Count}) sum to {total} but unitsPerSide is {profile.Capacity.UnitsPerSide}.";
        }

        return null;
    }

    /// <summary>One side's spawned force plus its per-jet sortie trackers.</summary>
    private sealed class SideArmy
    {
        public string Id { get; init; } = "";

        public int GroundRequested { get; init; }

        public int GroundSpawned { get; init; }

        public int AircraftRequested { get; init; }

        public int AircraftParked { get; init; }

        public List<GameObject> Units { get; init; } = new();

        public List<JetSortie> Jets { get; init; } = new();

        /// <summary>Centroid of the opposing deployment zone, used as the general attack point.</summary>
        public Vector3 OpposingCentroid { get; init; }

        public int AliveUnits => Units.Count(unit => !unit.IsEffectivelyDead);
    }

    private sealed class JetSortie
    {
        public JetSortie(string sideId, string definition, GameObject unit)
        {
            SideId = sideId;
            Definition = definition;
            Unit = unit;
        }

        public string SideId { get; }

        public string Definition { get; }

        public GameObject Unit { get; }

        public JetAIUpdate.JetAIState LastState { get; set; }

        public bool Airborne { get; set; }

        public int Launches { get; set; }

        public int Recoveries { get; set; }

        public int FirstLaunchFrame { get; set; } = -1;
    }

    private static SideArmy SpawnSide(
        Game game,
        MassBattleDeploymentProfile profile,
        int sideIndex,
        Player owner,
        NavigationGrid navigation,
        MassBattleAircraftProfile aircraft,
        ITestHarnessReporter reporter,
        List<string> reasons)
    {
        var side = profile.Sides[sideIndex];
        var ground = MassBattlePlacement.SpawnGroundArmy(
            game, profile, sideIndex, owner, profile.Capacity.Blocks, navigation, reporter, profile.Capacity.UnitsPerSide - aircraft.Count);
        var (airfields, airfieldReasons) = MassBattlePlacement.PlaceAirfields(
            game, profile, sideIndex, owner, navigation, reporter);
        reasons.AddRange(ground.FailureReasons.Select(reason => $"{side.Id}: {reason}"));
        reasons.AddRange(airfieldReasons.Select(reason => $"{side.Id}: {reason}"));

        var jets = ParkAircraft(game, side.Id, owner, aircraft, airfields, reasons);
        var opposingCentroid = MassBattlePlacement.GroundPosition(
            game, profile.Sides[1 - sideIndex].Zone.CenterX, profile.Sides[1 - sideIndex].Zone.CenterY);
        return new SideArmy
        {
            Id = side.Id,
            GroundRequested = profile.Capacity.UnitsPerSide - aircraft.Count,
            GroundSpawned = ground.Spawned,
            AircraftRequested = aircraft.Count,
            AircraftParked = jets.Count,
            Units = ground.Units.Concat(jets.Select(jet => jet.Unit)).ToList(),
            Jets = jets,
            OpposingCentroid = opposingCentroid,
        };
    }

    /// <summary>
    /// Spawns the side's aircraft directly into airfield parking slots through the shared
    /// parking core (<see cref="MassBattlePlacement.ParkAircraft"/>), so takeoff runs the real
    /// taxi and runway flow.
    /// </summary>
    private static List<JetSortie> ParkAircraft(
        Game game,
        string sideId,
        Player owner,
        MassBattleAircraftProfile aircraft,
        List<GameObject> airfields,
        List<string> reasons)
    {
        var definition = game.AssetStore.ObjectDefinitions.GetByName(aircraft.Definition);
        var jets = new List<JetSortie>();
        if (definition == null)
        {
            reasons.Add($"{sideId}: unknown aircraft definition '{aircraft.Definition}'");
            return jets;
        }

        var parking = MassBattlePlacement.ParkingPlaces(airfields);
        for (var i = 0; i < aircraft.Count; i++)
        {
            var jet = MassBattlePlacement.ParkAircraft(game, owner, definition, parking);
            if (jet == null)
            {
                reasons.Add($"{sideId}: no free parking slot for aircraft {i + 1} of {aircraft.Count}");
                break;
            }

            jets.Add(new JetSortie(sideId, aircraft.Definition, jet)
            {
                LastState = JetAIUpdate.JetAIState.Parked,
            });
        }

        return jets;
    }

    /// <summary>
    /// The general attack: every unit of every side moves on the opposing zone centroid and
    /// engages its nearest living enemy. Jets get a move target too, which unparks them through
    /// the real taxi and takeoff flow. Army-AI mode (WI-M4-03) passes includeGround: false, so
    /// only the jet sortie orders are issued and the ground hierarchy decides for itself.
    /// </summary>
    // WI-M4-01 scripted behavior for the plain probe. Army-AI mode (WI-M4-05) never calls
    // this: the hierarchy owns both ground and air from the first decision tick.
    private static void IssueGeneralAttack(Game game, List<SideArmy> armies)
    {
        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var enemies = armies[1 - i].Units.Where(unit => !unit.IsEffectivelyDead).ToList();
            foreach (var unit in army.Units)
            {
                if (unit.IsEffectivelyDead)
                {
                    continue;
                }

                var enemy = NearestEnemy(unit.Translation, enemies);
                if (enemy != null)
                {
                    unit.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, enemy.Id));
                }

                unit.AIUpdate?.SetTargetPoint(army.OpposingCentroid);
                unit.AIUpdate?.ConfigureAttackMove(true);
            }
        }
    }

    private static GameObject? NearestEnemy(Vector3 from, List<GameObject> enemies)
    {
        GameObject? nearest = null;
        var best = float.MaxValue;
        foreach (var enemy in enemies)
        {
            var distance = Vector3.DistanceSquared(from, enemy.Translation);
            if (distance < best)
            {
                best = distance;
                nearest = enemy;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Steps the engagement to the frame budget or the destruction of one side, tracking jet
    /// sortie transitions every frame and re-issuing orders on the refresh cadence while enemies
    /// remain (re-parked jets launch again: the rearm observation).
    /// </summary>
    /// <summary>
    /// Runs the measured engagement window (WI-M1-02): setup, spawn, and settle are unmeasured
    /// (mirroring B1 warmup); measurement spans the general attack to the frame budget or army
    /// destruction. The step bracket captures tick cost, allocations, GC, memory, system spans,
    /// and the delta of the engine path-request counter per tick. In windowed mode the step
    /// includes render and present, so tick.wallMs is the frame time and tick.fps the frame rate.
    /// </summary>
    private static (int FramesRun, BenchmarkRunRecord Record) RunEngagement(
        Game game,
        List<SideArmy> armies,
        Action step,
        int frameBudget,
        bool headless,
        ITestHarnessReporter reporter,
        MassBattleAsyncPathAnchor? anchor = null,
        MassBattleSquadInvariantSampler? invariantSampler = null,
        bool scriptedOrders = true)
    {
        var navigation = game.Scene3D.Navigation;
        var allJets = armies.SelectMany(army => army.Jets).ToList();
        var framesRun = frameBudget;
        var stopPredicateTriggered = false;

        using var recorder = new BenchmarkMetricRecorder();
        game.LogicStepper.SystemSpan += recorder.RecordSystemSpan;
        var pathDeltas = new List<double>(frameBudget);
        var pathStatisticsDeltas = new PathStatisticsDeltas(frameBudget);
        var partitionDirtyDeltas = new List<double>(frameBudget);
        var partitionCandidateDeltas = new List<double>(frameBudget);
        var partitionHitDeltas = new List<double>(frameBudget);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Periodic progress heartbeat: wall-clock cadence, not frame cadence, so the interval
        // never goes quiet exactly while frames are getting slower (the signal we care about).
        const double statusIntervalSeconds = 15.0;
        var nextStatusSeconds = statusIntervalSeconds;
        var statusFrame = 0;
        var statusElapsedMs = 0.0;

        void ReportStatus(int completedFrames, bool final)
        {
            var intervalFrames = Math.Max(1, completedFrames - statusFrame);
            var intervalMsPerFrame = (stopwatch.Elapsed.TotalMilliseconds - statusElapsedMs) / intervalFrames;
            var alive = string.Join(" ", armies.Select((army, index) => FormattableString.Invariant($"{(char)('A' + index)}={army.AliveUnits}")));
            var jetHealth = string.Join(" ", armies.Select((army, index) =>
            {
                var jets = army.Jets.Where(jet => !jet.Unit.IsEffectivelyDead).ToList();
                if (jets.Count == 0)
                {
                    return FormattableString.Invariant($"J{(char)('A' + index)}=-");
                }

                var minFraction = jets.Min(jet => jet.Unit.BodyModule.Health / jet.Unit.BodyModule.MaxHealth);
                var airborne = jets.Count(jet => ((JetAIUpdate)jet.Unit.AIUpdate).CurrentJetAIState != JetAIUpdate.JetAIState.Parked);
                var afterburner = jets.Count(jet => jet.Unit.ModelConditionFlags.Get(ModelConditionFlag.JetAfterburner));
                var exhaust = jets.Count(jet => jet.Unit.ModelConditionFlags.Get(ModelConditionFlag.JetExhaust));
                return FormattableString.Invariant($"J{(char)('A' + index)}:{jets.Count:0}@{minFraction:0.00}^{airborne}ab{afterburner}ex{exhaust}");
            }));
            var last = recorder.TickSamples.Count > 0 ? recorder.TickSamples[^1] : null;
            var head = FormattableString.Invariant(
                $"{(final ? "[probe] final" : "[probe]")} frame {completedFrames}/{frameBudget} wall {stopwatch.Elapsed.TotalSeconds:0.0}s avg {intervalMsPerFrame:0.0}ms/f");
            var tail = last != null
                ? FormattableString.Invariant(
                    $" last {last.WallMs:0.0}ms alloc {last.AllocBytes / 1048576.0:0.0}MB/f gc0 {last.Gc0} heap {(last.HeapBytes ?? 0) / 1073741824.0:0.00}GB")
                : string.Empty;
            reporter.Info(head + tail + FormattableString.Invariant($" alive {alive} jets {jetHealth}"));
            statusFrame = completedFrames;
            statusElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        }


        try
        {
            for (var frame = 0; frame < frameBudget; frame++)
            {
                if (anchor is not null && anchor.ShouldSubmit(frame))
                {
                    anchor.SubmitBurst(frame, BuildAnchorRequests(armies, frame, anchor.RequestsPerBurst));
                }

                var pathRequestsBefore = NavigationGrid.PathRequestCount;
                var pathStatsBefore = navigation.Statistics;
                var dirtyBefore = PartitionCellManager.DirtyObjectsProcessed;
                var candidatesBefore = PartitionCellManager.CandidatePairs;
                var hitsBefore = PartitionCellManager.ExactHits;
                recorder.BeginTick((uint)frame + 1);
                if (anchor is not null)
                {
                    anchor.CurrentSteppingFrame = frame + 1;
                }

                step();
                recorder.EndTick((uint)frame + 1);
                pathDeltas.Add(NavigationGrid.PathRequestCount - pathRequestsBefore);
                pathStatisticsDeltas.AddTick(pathStatsBefore, navigation.Statistics);
                partitionDirtyDeltas.Add(PartitionCellManager.DirtyObjectsProcessed - dirtyBefore);
                partitionCandidateDeltas.Add(PartitionCellManager.CandidatePairs - candidatesBefore);
                partitionHitDeltas.Add(PartitionCellManager.ExactHits - hitsBefore);
                anchor?.AfterStep();

                if (stopwatch.Elapsed.TotalSeconds >= nextStatusSeconds)
                {
                    ReportStatus(frame + 1, final: false);
                    nextStatusSeconds += statusIntervalSeconds;
                }

                TrackJetTransitions(allJets, frame);
                invariantSampler?.Sample();

                if (armies.Any(army => army.AliveUnits == 0))
                {
                    framesRun = frame + 1;
                    stopPredicateTriggered = true;
                    break;
                }

                if (scriptedOrders && (frame + 1) % OrderRefreshFrames == 0)
                {
                    RefreshOrders(game, armies);
                }
            }
        }
        finally
        {
            stopwatch.Stop();
            ReportStatus(framesRun, final: true);
            game.LogicStepper.SystemSpan -= recorder.RecordSystemSpan;
        }

        return (framesRun, BuildBenchmarkRecord(game, recorder, pathDeltas, pathStatisticsDeltas, partitionDirtyDeltas, partitionCandidateDeltas, partitionHitDeltas, stopwatch, framesRun, frameBudget, stopPredicateTriggered, headless));
    }

    /// <summary>
    /// Squad-shaped anchor requests (WI-B4-06): deterministic spread of alive ground units on
    /// side A requesting paths toward side B unit positions. Strides across both armies so
    /// successive bursts cover the field instead of the same leaders.
    /// </summary>
    private static List<(GameObject Requester, Vector3 Start, Vector3 End)> BuildAnchorRequests(
        List<SideArmy> armies,
        int frame,
        int count)
    {
        var requests = new List<(GameObject Requester, Vector3 Start, Vector3 End)>(count);

        if (armies.Count < 2)
        {
            return requests;
        }

        var attackers = armies[0].Units.Where(unit => !unit.IsEffectivelyDead).OrderBy(unit => unit.Id.Index).ToList();
        var defenders = armies[1].Units.Where(unit => !unit.IsEffectivelyDead).OrderBy(unit => unit.Id.Index).ToList();

        if (attackers.Count == 0 || defenders.Count == 0)
        {
            return requests;
        }

        var attackerStride = Math.Max(1, attackers.Count / count);
        var defenderStride = Math.Max(1, defenders.Count / count);

        for (var i = 0; i < count; i++)
        {
            var attacker = attackers[(frame + i * attackerStride) % attackers.Count];
            var defender = defenders[(frame + i * defenderStride) % defenders.Count];
            requests.Add((attacker, attacker.Translation, defender.Translation));
        }

        return requests;
    }

    private static BenchmarkRunRecord BuildBenchmarkRecord(
        Game game,
        BenchmarkMetricRecorder recorder,
        List<double> pathDeltas,
        PathStatisticsDeltas pathStatisticsDeltas,
        List<double> partitionDirtyDeltas,
        List<double> partitionCandidateDeltas,
        List<double> partitionHitDeltas,
        System.Diagnostics.Stopwatch stopwatch,
        int framesRun,
        int frameBudget,
        bool stopPredicateTriggered,
        bool headless)
    {
        var environment = EnvironmentRecord.Capture();
        environment.InstallId = game.Definition.Identifier;
        environment.Env["PANOPTICON_PROBE_MODE"] = headless ? "headless" : "windowed";
        var installPath = Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");
        if (!string.IsNullOrEmpty(installPath))
        {
            environment.Env["CNC_GENERALS_PATH"] = installPath;
        }

        var record = new BenchmarkRunRecord(
            BenchmarkScenarioId,
            ProbeSeed,
            SettleFrames,
            frameBudget,
            framesRun,
            stopPredicateTriggered,
            stopwatch.Elapsed.TotalMilliseconds)
        {
            TickSamples = [.. recorder.TickSamples],
            SystemSamples = [.. recorder.SystemSamples],
            Environment = environment,
        };

        record.Statistics.AddRange(SimulationBenchmarkRunner.BuildStatistics(record, ProbeSeed, baseline: null));
        record.Statistics.Add(BenchmarkStatistics.FromSamples(
            "tick.fps",
            [.. record.TickSamples.Select(t => t.WallMs > 0 ? 1000.0 / t.WallMs : 0.0)],
            ProbeSeed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples(
            "path.requestsPerTick",
            [.. pathDeltas],
            ProbeSeed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples(
            "path.requestsPerSecond",
            [.. pathDeltas.Select(d => d * game.GameEngine.LogicFramesPerSecond)],
            ProbeSeed));
        pathStatisticsDeltas.AddToRecord(record, game);
        AddPerTickAndPerSecondStatistics(record, "partition.dirtyObjects", partitionDirtyDeltas, game);
        AddPerTickAndPerSecondStatistics(record, "partition.candidatePairs", partitionCandidateDeltas, game);
        AddPerTickAndPerSecondStatistics(record, "partition.exactHits", partitionHitDeltas, game);
        return record;
    }

    private static void AddPerTickAndPerSecondStatistics(
        BenchmarkRunRecord record,
        string metricBaseName,
        List<double> deltas,
        Game game)
    {
        record.Statistics.Add(BenchmarkStatistics.FromSamples(
            $"{metricBaseName}PerTick",
            [.. deltas],
            ProbeSeed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples(
            $"{metricBaseName}PerSecond",
            [.. deltas.Select(d => d * game.GameEngine.LogicFramesPerSecond)],
            ProbeSeed));
    }

    /// <summary>
    /// Per-tick deltas of the Navigation statistics (WI-N0-01), captured as before/after
    /// snapshots around each step like the path-request counter. Per-origin counts attribute
    /// request load to its gameplay source; worker search time is the async worker's
    /// off-logic-thread cost.
    /// </summary>
    private sealed class PathStatisticsDeltas
    {
        private readonly List<double> _unitRequests;
        private readonly List<double> _squadLeaderRequests;
        private readonly List<double> _blockedFollowerRequests;
        private readonly List<double> _hordeRequests;
        private readonly List<double> _asyncRequests;
        private readonly List<double> _nodesExpanded;
        private readonly List<double> _failedSearches;
        private readonly List<double> _endpointAdjustments;
        private readonly List<double> _landedFollowers;
        private readonly List<double> _fallbackFollowers;
        private readonly List<double> _workerSearchMs;

        public PathStatisticsDeltas(int capacity)
        {
            _unitRequests = new List<double>(capacity);
            _squadLeaderRequests = new List<double>(capacity);
            _blockedFollowerRequests = new List<double>(capacity);
            _hordeRequests = new List<double>(capacity);
            _asyncRequests = new List<double>(capacity);
            _nodesExpanded = new List<double>(capacity);
            _failedSearches = new List<double>(capacity);
            _endpointAdjustments = new List<double>(capacity);
            _landedFollowers = new List<double>(capacity);
            _fallbackFollowers = new List<double>(capacity);
            _workerSearchMs = new List<double>(capacity);
        }

        public void AddTick(PathStatistics before, PathStatistics after)
        {
            _unitRequests.Add(after.UnitRequests - before.UnitRequests);
            _squadLeaderRequests.Add(after.SquadLeaderRequests - before.SquadLeaderRequests);
            _blockedFollowerRequests.Add(after.BlockedFollowerRequests - before.BlockedFollowerRequests);
            _hordeRequests.Add(after.HordeRequests - before.HordeRequests);
            _asyncRequests.Add(after.AsyncRequests - before.AsyncRequests);
            _nodesExpanded.Add(after.NodesExpanded - before.NodesExpanded);
            _failedSearches.Add(after.FailedSearches - before.FailedSearches);
            _endpointAdjustments.Add(after.EndpointAdjustments - before.EndpointAdjustments);
            _landedFollowers.Add(after.LandedFollowers - before.LandedFollowers);
            _fallbackFollowers.Add(after.FallbackFollowers - before.FallbackFollowers);
            _workerSearchMs.Add(after.WorkerSearchMs - before.WorkerSearchMs);
        }

        public void AddToRecord(BenchmarkRunRecord record, Game game)
        {
            AddPerTickAndPerSecondStatistics(record, "path.unitRequests", _unitRequests, game);
            AddPerTickAndPerSecondStatistics(record, "path.squadLeaderRequests", _squadLeaderRequests, game);
            AddPerTickAndPerSecondStatistics(record, "path.blockedFollowerRequests", _blockedFollowerRequests, game);
            AddPerTickAndPerSecondStatistics(record, "path.hordeRequests", _hordeRequests, game);
            AddPerTickAndPerSecondStatistics(record, "path.asyncRequests", _asyncRequests, game);
            AddPerTickAndPerSecondStatistics(record, "path.nodesExpanded", _nodesExpanded, game);
            AddPerTickAndPerSecondStatistics(record, "path.failedSearches", _failedSearches, game);
            AddPerTickAndPerSecondStatistics(record, "path.endpointAdjustments", _endpointAdjustments, game);
            AddPerTickAndPerSecondStatistics(record, "path.landedFollowers", _landedFollowers, game);
            AddPerTickAndPerSecondStatistics(record, "path.fallbackFollowers", _fallbackFollowers, game);
            AddPerTickAndPerSecondStatistics(record, "path.workerSearchMs", _workerSearchMs, game);
        }
    }

    private static void TrackJetTransitions(List<JetSortie> jets, int frame)
    {
        foreach (var jet in jets)
        {
            if (jet.Unit.IsEffectivelyDead)
            {
                continue;
            }

            var state = ((JetAIUpdate)jet.Unit.AIUpdate).CurrentJetAIState;
            if (state == jet.LastState)
            {
                continue;
            }

            if (jet.LastState == JetAIUpdate.JetAIState.Parked
                && state == JetAIUpdate.JetAIState.MovingTowardsStart)
            {
                jet.Launches++;
                jet.Airborne = true;
                if (jet.FirstLaunchFrame < 0)
                {
                    jet.FirstLaunchFrame = frame;
                }
            }

            if (jet.Airborne && state == JetAIUpdate.JetAIState.Parked)
            {
                jet.Recoveries++;
                jet.Airborne = false;
            }

            jet.LastState = state;
        }
    }

    /// <summary>
    /// Re-issues the general attack for idle units (move completed) and for jets recovered into
    /// parking, while any enemy remains. This is the scripted steady state of the engagement,
    /// not AI. Army-AI mode (WI-M4-05) never calls this; the ground hierarchy and the air
    /// commander own every re-order.
    /// </summary>
    private static void RefreshOrders(Game game, List<SideArmy> armies)
    {
        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var enemies = armies[1 - i].Units.Where(unit => !unit.IsEffectivelyDead).ToList();
            if (enemies.Count == 0)
            {
                continue;
            }

            var jetsByUnit = army.Jets.ToDictionary(jet => jet.Unit);
            foreach (var unit in army.Units)
            {
                if (unit.IsEffectivelyDead)
                {
                    continue;
                }

                if (jetsByUnit.ContainsKey(unit))
                {
                    // Only re-issue for a jet settled back into parking: mid-sortie states own it.
                    if (((JetAIUpdate)unit.AIUpdate!).CurrentJetAIState != JetAIUpdate.JetAIState.Parked)
                    {
                        continue;
                    }
                }
                else if (unit.AIUpdate?.TargetPoints.Count > 0)
                {
                    continue;
                }

                var enemy = NearestEnemy(unit.Translation, enemies);
                if (enemy != null)
                {
                    unit.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, enemy.Id));
                }

                unit.AIUpdate?.SetTargetPoint(army.OpposingCentroid);
                unit.AIUpdate?.ConfigureAttackMove(true);
            }
        }
    }

    private static string DescribeStopReason(List<SideArmy> armies, int framesRun, int frameBudget)
    {
        if (framesRun >= frameBudget)
        {
            return $"frame budget reached ({frameBudget} frames)";
        }

        var eliminated = armies.First(army => army.AliveUnits == 0);
        return $"{eliminated.Id} eliminated after {framesRun} frames";
    }

    private static MassBattleCapacityProbeReport BuildReport(
        MassBattleDeploymentProfile profile,
        string profilePath,
        bool headless,
        List<SideArmy> armies,
        List<string> reasons,
        int frameBudget,
        int framesRun,
        string stopReason,
        string? screenshotPath,
        MassBattleAsyncPathAnchorReport? asyncPathAnchor = null,
        MassBattleSquadInvariantReport? squadInvariant = null,
        IReadOnlyList<ArmyAiSide>? armyAiSides = null)
    {
        var sides = armies.Select(army => new SideEngagementResult
        {
            Id = army.Id,
            GroundRequested = army.GroundRequested,
            GroundSpawned = army.GroundSpawned,
            AircraftRequested = army.AircraftRequested,
            AircraftParked = army.AircraftParked,
            AliveAtEnd = army.AliveUnits,
            DestroyedAtEnd = army.Units.Count - army.AliveUnits,
        }).ToList();

        var allJets = armies.SelectMany(army => army.Jets).ToList();

        // WI-M4-05: per-side air mission totals and per-jet assignment counts from the live
        // policies. Null keeps the scripted M1 report shape unchanged.
        List<AirMissionSideResult>? airMissions = null;
        Dictionary<uint, int>? missionsByJet = null;
        if (armyAiSides is not null)
        {
            airMissions = [];
            missionsByJet = [];
            for (var i = 0; i < armyAiSides.Count && i < armies.Count; i++)
            {
                var policy = armyAiSides[i].Policy;
                airMissions.Add(new AirMissionSideResult
                {
                    SideId = armies[i].Id,
                    Strike = policy.AirMissionAssignmentCount(AirMission.Strike),
                    Patrol = policy.AirMissionAssignmentCount(AirMission.Patrol),
                    Escort = policy.AirMissionAssignmentCount(AirMission.Escort),
                    JetsWithMultipleMissions = armies[i].Jets.Count(
                        jet => policy.AirMissionAssignmentCountForUnit(jet.Unit.Id) >= 2),
                });
                foreach (var jet in armies[i].Jets)
                {
                    missionsByJet[jet.Unit.Id.Index] = policy.AirMissionAssignmentCountForUnit(jet.Unit.Id);
                }
            }
        }

        var sorties = allJets.Select(jet => new SortieResult
        {
            SideId = jet.SideId,
            Definition = jet.Definition,
            Launches = jet.Launches,
            Recoveries = jet.Recoveries,
            FirstLaunchFrame = jet.FirstLaunchFrame,
            FinalState = (int)((JetAIUpdate)jet.Unit.AIUpdate).CurrentJetAIState,
            FinalStateName = jet.Unit.IsEffectivelyDead
                ? "Destroyed"
                : ((JetAIUpdate)jet.Unit.AIUpdate).CurrentJetAIState.ToString(),
            MissionsAssigned = missionsByJet is not null
                && missionsByJet.TryGetValue(jet.Unit.Id.Index, out var missions)
                    ? missions
                    : null,
        }).ToList();

        var parkedShortfall = sides
            .Where(side => side.AircraftParked != side.AircraftRequested)
            .Select(side => $"{side.Id}: parked {side.AircraftParked} of {side.AircraftRequested} aircraft");
        reasons.AddRange(parkedShortfall);

        var neverLaunched = allJets.Count(jet => jet.Launches == 0);
        if (neverLaunched > 0)
        {
            reasons.Add($"{neverLaunched} of {allJets.Count} aircraft never launched");
        }

        if (allJets.All(jet => jet.Launches < 2))
        {
            reasons.Add("no aircraft completed a second sortie (rearm not observed)");
        }

        foreach (var side in sides)
        {
            if (side.DestroyedAtEnd == 0)
            {
                reasons.Add($"{side.Id} took no losses (no contact within the frame budget)");
            }
        }

        if (squadInvariant is not null)
        {
            foreach (var side in squadInvariant.Sides)
            {
                if (side.MaxUnassignedEligibleGround > 0)
                {
                    reasons.Add(
                        $"{side.SideId}: {side.MaxUnassignedEligibleGround} eligible ground units left unassigned (max observed)");
                }

                if (side.MaxCommanderlessSquads > 0)
                {
                    reasons.Add(
                        $"{side.SideId}: {side.MaxCommanderlessSquads} non-empty squads left without a live commander (max observed)");
                }

                if (side.StalledUnitsAtEnd > 0)
                {
                    reasons.Add(
                        $"{side.SideId}: {side.StalledUnitsAtEnd} units idle beyond the " +
                        $"{squadInvariant.StallThresholdFrames}-frame stall threshold while enemies remained");
                }
            }
        }

        var report = new MassBattleCapacityProbeReport
        {
            Map = profile.Map.Name,
            Provenance = profile.Map.Provenance,
            ProfilePath = profilePath,
            Headless = headless,
            AircraftDefinition = profile.Capacity.Aircraft?.Definition ?? "",
            SettleFrames = SettleFrames,
            MeasuredFramesRequested = frameBudget,
            MeasuredFramesRun = framesRun,
            StopReason = stopReason,
            ScreenshotPath = screenshotPath,
            Sides = sides,
            Sorties = sorties,
            FailureReasons = reasons.Distinct().ToList(),
            AsyncPathAnchor = asyncPathAnchor,
            SquadInvariant = squadInvariant,
            AirMissions = airMissions,
        };
        return report with { AllPassed = report.FailureReasons.Count == 0 };
    }

    private static void FrameDeployment(Game game, MassBattleDeploymentProfile profile)
    {
        var bounds = MassBattlePlacement.PlacementBounds(profile);
        var view = game.Scene3D.TacticalView;
        view.SetAngleAndPitchToDefault();
        // Focus on the engagement rather than fitting both rear deployment areas.
        view.Angle = -MathF.PI / 2f;
        view.SnapHeightAboveGround(700f);
        view.LookAt(new Vector3(
            bounds.X + bounds.Width / 2f,
            bounds.Y + bounds.Height / 2f,
            0f));
    }

    private static string? ResolveProfilePath(TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        if (!string.IsNullOrWhiteSpace(config.MassBattleCapacityProbeProfilePath))
        {
            if (System.IO.File.Exists(config.MassBattleCapacityProbeProfilePath))
            {
                return config.MassBattleCapacityProbeProfilePath;
            }

            reporter.Error($"MassBattleCapacityProbeProfilePath '{config.MassBattleCapacityProbeProfilePath}' does not exist.");
            return null;
        }

        var embedded = System.IO.Path.Combine(AppContext.BaseDirectory, DefaultProfileFileName);
        if (System.IO.File.Exists(embedded))
        {
            return embedded;
        }

        reporter.Error(
            $"No capacity probe profile: set MassBattleCapacityProbeProfilePath or ship '{DefaultProfileFileName}' beside the executable.");
        return null;
    }

    private static string? CaptureScreenshot(Game game, string path, ITestHarnessReporter reporter)
    {
        try
        {
            game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, 1920, 1080));
            game.Render();
            FrameCapturer.CaptureToPng(
                game.GraphicsDevice,
                game.Panel.Framebuffer.ColorTargets[0].Target,
                path);
            reporter.Info($"Capacity probe screenshot saved to: {path}");
            return path;
        }
        catch (Exception ex)
        {
            reporter.Error($"Screenshot capture failed: {ex.Message}");
            return null;
        }
    }
}
