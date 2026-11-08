using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.IO;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Network;
using Panopticon.Simulation;
using Panopticon.Tools.H5Migrate;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;
// The Navigation class shares its name with its namespace; alias it to keep call sites readable.
using NavigationGrid = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-H5-05: the behavioral equivalence harness. Runs one declarative scenario headlessly at a
/// fixed seed against original content (legacy .map plus INI roster) and converted content (H3
/// map package through the WI-H5-04 Game.LoadMap branch plus an H4 unit package published into a
/// scoped AssetStore layer), twice per side for repeat stability, then asserts equal end state
/// through the P0 assertion layer: outcomes, survivors, and the engagement counts the harness
/// already reports. Any inequality fails with a named diff report carrying both values. The
/// comparison is same-runtime only; the play-identically claim is proven by this run alone
/// (see BehavioralParityReport.ClaimText).
/// </summary>
public static class BehavioralParityRunner
{
    public const string ScenarioId = "behavioral-parity";

    // Exit codes follow the harness convention: 1 setup error, 5 unmet check.
    private const int ExitSetupError = 1;
    private const int ExitCheckFailed = 5;

    /// <summary>The named end-state assertion set: P0 assertion paths captured per run and
    /// compared across runs through the standard assertion evaluator.</summary>
    public static readonly IReadOnlyList<string> DefaultAssertionPaths =
    [
        "outcome.status",
        "outcome.reason",
        "outcome.winningPlayer",
        "outcome.decidedFrame",
        "player[2].unitCount",
        "player[3].unitCount",
    ];

    public static int Run(
        GameInstallation installation,
        BehavioralParityScenario scenario,
        BehavioralParityOptions options,
        ITestHarnessReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(reporter);

        var artifactsDirectory = options.ArtifactsDirectory;
        Directory.CreateDirectory(artifactsDirectory);
        QuietenPerFrameLogging();

        // ── Content preparation: convert the legacy map and the INI roster in-fixture ──
        var mapPackageDirectory = Path.Combine(artifactsDirectory, "converted-map");
        var unitPackageDirectory = Path.Combine(artifactsDirectory, "converted-units");
        ResetDirectory(mapPackageDirectory);
        ResetDirectory(unitPackageDirectory);

        if (!PrepareContent(installation, scenario, artifactsDirectory, mapPackageDirectory, unitPackageDirectory, reporter, out var setupError))
        {
            reporter.Error(setupError);
            return ExitSetupError;
        }

        reporter.Info($"converted map package: {mapPackageDirectory}");
        reporter.Info($"converted unit package: {unitPackageDirectory}");

        // ── Four runs: original and converted, each repeated at the same seed ──
        // Each SideRun keeps its Game open so the comparison can evaluate assertions against the
        // live game; disposal happens right after the comparison that consumes it.
        var runs = new List<BehavioralParityRunRecord>();
        var original = RunSide(installation, scenario, "original", 1, scenario.SourceMapPath, null, options, reporter);
        if (original.Record.Error is not null)
        {
            reporter.Error($"original run 1 failed: {original.Record.Error}");
            return FinishWithRuns(runs, original.Record, scenario, options, mapPackageDirectory, unitPackageDirectory);
        }

        runs.Add(original.Record);
        var converted = RunSide(installation, scenario, "converted", 1, mapPackageDirectory, unitPackageDirectory, options, reporter);
        if (converted.Record.Error is not null)
        {
            reporter.Error($"converted run 1 failed: {converted.Record.Error}");
            original.Dispose();
            return FinishWithRuns(runs, converted.Record, scenario, options, mapPackageDirectory, unitPackageDirectory);
        }

        runs.Add(converted.Record);

        // Parity verdict while the converted game is alive: original-recorded expectations become
        // StateAssertions evaluated against it through the standard P0 evaluator.
        var assertionPaths = scenario.AssertionSet == "default" ? DefaultAssertionPaths : [];
        var parity = Compare(original, converted, isDeterminismCheck: false, assertionPaths, reporter);
        converted.Dispose();
        original.Dispose();

        var originalRepeat = RunSide(installation, scenario, "original", 2, scenario.SourceMapPath, null, options, reporter);
        if (originalRepeat.Record.Error is not null)
        {
            reporter.Error($"original run 2 failed: {originalRepeat.Record.Error}");
            originalRepeat.Dispose();
            return Finish(runs, scenario, reporter, options, mapPackageDirectory, unitPackageDirectory, parity,
                ErrorSet(original.Record, originalRepeat.Record, originalRepeat.Record.Error),
                ErrorSet(converted.Record, converted.Record, "converted run 2 not executed"));
        }

        runs.Add(originalRepeat.Record);
        var originalStability = Compare(original, originalRepeat, isDeterminismCheck: true, assertionPaths, reporter);
        originalRepeat.Dispose();

        var convertedRepeat = RunSide(installation, scenario, "converted", 2, mapPackageDirectory, unitPackageDirectory, options, reporter);
        if (convertedRepeat.Record.Error is not null)
        {
            reporter.Error($"converted run 2 failed: {convertedRepeat.Record.Error}");
            convertedRepeat.Dispose();
            return Finish(runs, scenario, reporter, options, mapPackageDirectory, unitPackageDirectory, parity,
                originalStability, ErrorSet(converted.Record, convertedRepeat.Record, convertedRepeat.Record.Error));
        }

        runs.Add(convertedRepeat.Record);
        var convertedStability = Compare(converted, convertedRepeat, isDeterminismCheck: true, assertionPaths, reporter);
        convertedRepeat.Dispose();

        return Finish(runs, scenario, reporter, options, mapPackageDirectory, unitPackageDirectory, parity,
            originalStability, convertedStability);
    }

    /// <summary>
    /// Same-content stability probe (no conversion): the original side, run twice at the scenario
    /// seed, compared through the same P0 assertion layer as the parity verdict. Guards against
    /// ambient nondeterminism being mistaken for parity; also the standing determinism gate the
    /// parity run relies on. Exit 0 when the repeat matches, 5 with a determinism-stop report.
    /// </summary>
    public static int RunStabilityProbe(
        GameInstallation installation,
        BehavioralParityScenario scenario,
        BehavioralParityOptions options,
        ITestHarnessReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(reporter);

        Directory.CreateDirectory(options.ArtifactsDirectory);
        QuietenPerFrameLogging();

        var runs = new List<BehavioralParityRunRecord>();
        var first = RunSide(installation, scenario, "original", 1, scenario.SourceMapPath, null, options, reporter);
        runs.Add(first.Record);
        if (first.Record.Error is not null)
        {
            reporter.Error($"stability probe run 1 failed: {first.Record.Error}");
            return ExitSetupError;
        }

        var second = RunSide(installation, scenario, "original", 2, scenario.SourceMapPath, null, options, reporter);
        if (second.Record.Error is not null)
        {
            first.Dispose();
            second.Dispose();
            reporter.Error($"stability probe run 2 failed: {second.Record.Error}");
            return ExitSetupError;
        }

        runs.Add(second.Record);
        var stability = Compare(first, second, isDeterminismCheck: true, DefaultAssertionPaths, reporter);
        second.Dispose();
        first.Dispose();

        var report = new BehavioralParityReport(
            scenario.Name, scenario.SourceMapPath, scenario.Seed, scenario.AssertionSet,
            "(stability probe: no conversion)", "(stability probe: no conversion)", runs,
            new BehavioralParityComparisonSet("original", "converted", false, true, []),
            stability,
            new BehavioralParityComparisonSet("original", "original", true, true, []),
            stability.AllPassed,
            BehavioralParityReport.ClaimText);
        var reportPath = options.ReportPath ?? Path.Combine(options.ArtifactsDirectory, "h5-parity-stability.json");
        BehavioralParityReportIo.WriteJson(report, reportPath);
        reporter.Info($"Stability report saved to: {reportPath}");
        reporter.Info(BehavioralParityReportIo.WriteText(report));

        if (!stability.AllPassed)
        {
            reporter.Error(
                "DETERMINISM STOP: same-content runs diverged at one seed. Artifacts are preserved under "
                + options.ArtifactsDirectory
                + ". Report verbatim for user determination; never re-baseline.");
            return ExitCheckFailed;
        }

        return 0;
    }

    private static int Finish(
        List<BehavioralParityRunRecord> runs,
        BehavioralParityScenario scenario,
        ITestHarnessReporter reporter,
        BehavioralParityOptions options,
        string mapPackageDirectory,
        string unitPackageDirectory,
        BehavioralParityComparisonSet parity,
        BehavioralParityComparisonSet originalStability,
        BehavioralParityComparisonSet convertedStability)
    {
        var allPassed = parity.AllPassed && originalStability.AllPassed && convertedStability.AllPassed;
        var report = new BehavioralParityReport(
            scenario.Name, scenario.SourceMapPath, scenario.Seed, scenario.AssertionSet,
            mapPackageDirectory, unitPackageDirectory, runs,
            parity, originalStability, convertedStability,
            allPassed, BehavioralParityReport.ClaimText);

        var reportPath = options.ReportPath ?? Path.Combine(options.ArtifactsDirectory, "h5-parity-report.json");
        BehavioralParityReportIo.WriteJson(report, reportPath);
        reporter.Info($"Parity report saved to: {reportPath}");
        reporter.Info(BehavioralParityReportIo.WriteText(report));

        // Determinism stop rule: a same-content deviation is reported verbatim for user
        // determination; run artifacts (packages, run records, reports) are preserved and no
        // baseline is touched.
        var determinismBroke = !originalStability.AllPassed || !convertedStability.AllPassed;
        if (determinismBroke)
        {
            reporter.Error(
                "DETERMINISM STOP: same-content runs diverged at one seed. Artifacts are preserved under "
                + options.ArtifactsDirectory
                + ". Report verbatim for user determination; never re-baseline.");
            return ExitCheckFailed;
        }

        return allPassed ? 0 : ExitCheckFailed;
    }

    private static int FinishWithRuns(
        List<BehavioralParityRunRecord> runs,
        BehavioralParityRunRecord failed,
        BehavioralParityScenario scenario,
        BehavioralParityOptions options,
        string mapPackageDirectory,
        string unitPackageDirectory)
    {
        runs.Add(failed);
        var report = new BehavioralParityReport(
            scenario.Name, scenario.SourceMapPath, scenario.Seed, scenario.AssertionSet,
            mapPackageDirectory, unitPackageDirectory, runs,
            new BehavioralParityComparisonSet("original", "converted", false, false, []),
            new BehavioralParityComparisonSet("original", "original", true, false, []),
            new BehavioralParityComparisonSet("converted", "converted", true, false, []),
            AllPassed: false,
            BehavioralParityReport.ClaimText);
        BehavioralParityReportIo.WriteJson(report, options.ReportPath ?? Path.Combine(options.ArtifactsDirectory, "h5-parity-report.json"));
        return ExitCheckFailed;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Content preparation
    // ──────────────────────────────────────────────────────────────────────────

    private static bool PrepareContent(
        GameInstallation installation,
        BehavioralParityScenario scenario,
        string artifactsDirectory,
        string mapPackageDirectory,
        string unitPackageDirectory,
        ITestHarnessReporter reporter,
        out string error)
    {
        error = string.Empty;

        var sourceMapPath = Path.Combine(artifactsDirectory, "source.map");
        using (var fileSystem = installation.CreateFileSystem())
        {
            var entry = fileSystem.GetFile(scenario.SourceMapPath);
            if (entry is null)
            {
                error = $"Source map '{scenario.SourceMapPath}' not found in the installation file system.";
                return false;
            }

            using var source = entry.Open();
            using var target = File.Create(sourceMapPath);
            source.CopyTo(target);
        }

        var convertMapOut = new StringWriter();
        var convertMapErr = new StringWriter();
        var mapExit = H5MigrateCli.Run(
            ["convert-map", "--input", Path.GetFullPath(sourceMapPath), "--out", Path.GetFullPath(mapPackageDirectory)],
            convertMapOut,
            convertMapErr,
            CancellationToken.None);
        if (mapExit != 0)
        {
            error = $"convert-map failed (exit {mapExit}): {convertMapErr}{convertMapOut}";
            return false;
        }

        reporter.Info(convertMapOut.ToString().TrimEnd());

        var convertIniOut = new StringWriter();
        var convertIniErr = new StringWriter();
        var iniExit = H5MigrateCli.Run(
            ["convert-ini", "--p6b", "--out", Path.GetFullPath(unitPackageDirectory), "--game-path", installation.Path],
            convertIniOut,
            convertIniErr,
            CancellationToken.None);
        if (iniExit != 0)
        {
            error = $"convert-ini failed (exit {iniExit}): {convertIniErr}{convertIniOut}";
            return false;
        }

        reporter.Info(convertIniOut.ToString().TrimEnd());
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // One headless run of one side
    // ──────────────────────────────────────────────────────────────────────────

    private sealed record SideRun(
        BehavioralParityRunRecord Record,
        Game? Game,
        RunEndState? EndState,
        string? Error) : IDisposable
    {
        public void Dispose() => Game?.Dispose();
    }

    private static SideRun RunSide(
        GameInstallation installation,
        BehavioralParityScenario scenario,
        string side,
        int runIndex,
        string mapPath,
        string? unitPackageDirectory,
        BehavioralParityOptions options,
        ITestHarnessReporter reporter)
    {
        try
        {
            var game = new Game(
                installation,
                null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true },
                null);

            // The converted side loads the roster from the H4 unit package, materialized and
            // published into a scope of the live game's AssetStore so package-authored
            // definitions shadow the INI ones by name (the sanctioned override path).
            if (unitPackageDirectory is not null)
            {
                var publishError = PublishUnitPackage(game, unitPackageDirectory, out var packageRecordCount);
                if (publishError is not null)
                {
                    game.Dispose();
                    return new SideRun(
                        MakeErrorRecord(side, runIndex, scenario.Seed, mapPath, publishError), null, null, publishError);
                }

                reporter.Info($"[{side}#{runIndex}] published {packageRecordCount} package-authored records into a scoped store layer");
            }

            var playerSettings = new[]
            {
                new PlayerSetting(1, "America", MassBattlePlacement.ParseColor("1", 0), 0, PlayerOwner.Player, "H5ParityA"),
                new PlayerSetting(2, "America", MassBattlePlacement.ParseColor("2", 1), 1, PlayerOwner.Player, "H5ParityB"),
            };

            game.StartSkirmishOrMultiPlayerGame(mapPath, new EchoConnection(), playerSettings, (int)scenario.Seed, isMultiPlayer: false);
            game.StartRun();
            game.EnableFixedLogicTimestep();
            game.VictoryOptions = new VictoryOptions(
                scenario.SettleFrames + scenario.EngagementFrames + 1,
                VictoryScoreMetric.UnitCount,
                VictoryConditionKind.ArmyEliminated);

            var endState = RunEngagement(game, scenario, side, runIndex, options, reporter);
            var record = BuildRecord(side, runIndex, scenario.Seed, mapPath, endState);
            return new SideRun(record, game, endState, null);
        }
        catch (Exception ex)
        {
            return new SideRun(
                MakeErrorRecord(side, runIndex, scenario.Seed, mapPath, ex.ToString()), null, null, ex.ToString());
        }
    }

    /// <summary>
    /// Materializes the H4 unit package and publishes it into a scope of the game's AssetStore.
    /// Every published id is declared an override: shadowing the same-named INI asset in the
    /// lower scope is the point of the converted side.
    /// </summary>
    private static string? PublishUnitPackage(Game game, string unitPackageDirectory, out int recordCount)
    {
        recordCount = 0;
        var reader = new H4UnitPackageReader(unitPackageDirectory);
        var roots = new List<string> { "DefaultThingTemplate" };
        roots.AddRange(P6bRosterNames);
        var set = reader.Read(roots);
        var readError = set.Diagnostics.FirstOrDefault(d => d.IsError);
        if (readError is not null)
        {
            return $"package read failed: {readError.Code}: {readError.Message}";
        }

        var resolution = UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry());
        if (!resolution.Succeeded)
        {
            return "package resolution failed: "
                + string.Join("; ", resolution.Diagnostics.Where(d => d.IsError).Select(d => d.Message));
        }

        var publisher = new UnitContentPublisher(game.AssetStore);
        var context = new UnitMaterializationContext(
            new UnitConversionPolicy(SageGame.CncGenerals),
            publisher.Lookup,
            reader.Natives,
            game.AssetStore);
        var materialized = UnitRuntimeMaterializer.Materialize(resolution, context);
        if (!materialized.Succeeded)
        {
            return "package materialization failed: "
                + string.Join("; ", materialized.Diagnostics.Where(d => d.IsError).Select(d => d.Message));
        }

        var publication = publisher.Publish(materialized, null, materialized.Assets.Keys.ToList());
        if (!publication.Succeeded)
        {
            return "package publication failed: "
                + string.Join("; ", publication.Diagnostics.Where(d => d.IsError).Select(d => d.Message));
        }

        recordCount = materialized.Assets.Count;
        return null;
    }

    /// <summary>The P6b reference roster, matching the convert-ini --p6b preset.</summary>
    public static readonly IReadOnlyList<string> P6bRosterNames =
    [
        "AmericaInfantryRanger",
        "AmericaTankCrusader",
        "AmericaVehicleComanche",
        "AmericaVehicleDozer",
        "AmericaVehicleMedic",
        "AmericaParticleCannonUplink",
    ];

    private static RunEndState RunEngagement(
        Game game,
        BehavioralParityScenario scenario,
        string side,
        int runIndex,
        BehavioralParityOptions options,
        ITestHarnessReporter reporter)
    {
        var players = new[] { game.Scene3D.Players[2], game.Scene3D.Players[3] };

        var startA = game.Scene3D.Waypoints.TryGetPlayerStart(1, out var wpA) && wpA is not null ? wpA.Position : new Vector3(400, 400, 0);
        var startB = game.Scene3D.Waypoints.TryGetPlayerStart(2, out var wpB) && wpB is not null ? wpB.Position : new Vector3(600, 600, 0);
        var midpoint = (startA + startB) * 0.5f;
        var toB = startB - startA;
        var distance = toB.Length();
        var direction = distance > 1f ? toB / distance : new Vector3(1, 0, 0);
        var separation = Math.Clamp(distance * 0.30f, 260f, 680f);
        var centers = new[]
        {
            midpoint - (direction * separation * 0.5f),
            midpoint + (direction * separation * 0.5f),
        };
        reporter.Info(
            $"[{side}#{runIndex}] player starts ({startA.X:F0},{startA.Y:F0}) and ({startB.X:F0},{startB.Y:F0}); "
            + $"spawn centers ({centers[0].X:F0},{centers[0].Y:F0}) and ({centers[1].X:F0},{centers[1].Y:F0})");

        var navigation = game.Scene3D.Navigation;
        var armies = new List<SideArmy>();
        for (var sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            armies.Add(SpawnArmy(game, players[sideIndex], scenario.Roster, centers[sideIndex], centers[1 - sideIndex], navigation));
        }

        using var tracker = new KillLossTracker(game.GameLogic, armies.SelectMany(a => a.Units));

        // Presentation-only: point the camera at the battle midpoint so optional PNG captures of
        // the converted-content run frame the fight. Headless logic never reads the camera.
        game.Scene3D.TacticalView.InitHeightForMap();
        game.Scene3D.TacticalView.SetAngleAndPitchToDefault();
        game.Scene3D.TacticalView.LookAt(new Vector3(midpoint.X, midpoint.Y, 0f));

        var captureFrames = options.CaptureDirectory is not null && side == "converted";

        // Settle is unmeasured (spawn transients), mirroring the probe's warm-up. Stepping drives
        // game.Update one logic frame at a time (the ScenarioCapture driver), so captures and
        // order refreshes land on exact frames and both sides consume an identical schedule.
        for (var i = 0; i < scenario.SettleFrames; i++)
        {
            game.Update([]);
        }

        IssueGeneralAttack(game, armies, centers);

        var haltedOnOutcome = false;
        var captured = 0;
        for (var frame = 0; frame < scenario.EngagementFrames; frame++)
        {
            game.Update([]);

            if (frame % scenario.OrderRefreshInterval == 0 && frame > 0)
            {
                IssueGeneralAttack(game, armies, centers);
            }

            if (captureFrames && options.CaptureStride > 0 && frame % options.CaptureStride == 0)
            {
                RenderAndCapture(game, Path.Combine(options.CaptureDirectory!, $"h5_parity_converted_{captured:D4}.png"));
                captured++;
            }

            if (game.CurrentOutcome.Status == OutcomeStatus.Decided || !game.IsRunning)
            {
                haltedOnOutcome = true;
                break;
            }
        }

        reporter.Info(
            $"[{side}#{runIndex}] engagement finished at frame {CurrentLogicFrame(game)}, outcome {DescribeOutcome(game)}, halted={haltedOnOutcome}"
            + (captureFrames ? $", captured {captured} frames" : string.Empty));

        return CaptureEndState(game, CurrentLogicFrame(game), haltedOnOutcome, armies, tracker);
    }

    private sealed record SideArmy(Player Player, List<GameObject> Units, List<(string Definition, int Count)> Spawned);

    private static SideArmy SpawnArmy(
        Game game,
        Player owner,
        IReadOnlyList<BehavioralParityRosterEntry> roster,
        Vector3 center,
        Vector3 enemyCenter,
        NavigationGrid navigation)
    {
        var yaw = MassBattlePlacement.YawToward(center, enemyCenter);
        var units = new List<GameObject>();
        var spawned = new List<(string Definition, int Count)>();

        // Grid walk in block order, twice-footprint spacing, skipping impassable slots; the same
        // walk on both sides of the same map content, so skips apply identically.
        var cursorY = center.Y - 400f;
        var column = 0;
        const float zoneWidth = 800f;
        foreach (var entry in roster)
        {
            var definition = game.AssetStore.ObjectDefinitions.GetByName(entry.Definition);
            if (definition is null)
            {
                spawned.Add((entry.Definition, 0));
                continue;
            }

            var pitch = MathF.Max(4f * definition.Geometry.MajorRadius, 30f);
            var columns = Math.Max(1, (int)(zoneWidth / pitch));
            if (column != 0)
            {
                column = 0;
                cursorY += pitch;
            }

            var placed = 0;
            var guard = 0;
            while (placed < entry.Count && guard < (entry.Count * 8) + 64)
            {
                guard++;
                var x = center.X - (zoneWidth / 2f) + (pitch / 2f) + (column * pitch);
                var y = cursorY + (pitch / 2f);
                column++;
                if (column >= columns)
                {
                    column = 0;
                    cursorY += pitch;
                }

                if (!navigation.IsPassable(MassBattlePlacement.GroundPosition(game, x, y)))
                {
                    continue;
                }

                units.Add(MassBattlePlacement.SpawnAt(game, definition, owner, x, y, yaw));
                placed++;
            }

            spawned.Add((entry.Definition, placed));
        }

        return new SideArmy(owner, units, spawned);
    }

    /// <summary>The general attack, mirroring the M1 probe: move on the opposing center with a
    /// weapon target on the nearest living enemy, in attack-move so units pause and fire.</summary>
    private static void IssueGeneralAttack(Game game, List<SideArmy> armies, Vector3[] centers)
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
                if (enemy is not null)
                {
                    unit.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, enemy.Id));
                }

                unit.AIUpdate?.SetTargetPoint(centers[1 - i]);
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

    private static void RenderAndCapture(Game game, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        game.Panel.EnsureFrame(new Panopticon.Mathematics.Rectangle(0, 0, 1280, 720));
        game.Render();
        FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, path);
    }

    private static uint CurrentLogicFrame(Game game) =>
        unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));

    // ──────────────────────────────────────────────────────────────────────────
    // End-state capture and comparison
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Records one assertion path's actual value through the P0 path resolver.</summary>
    private sealed class EndStateProbeAssertion : ScenarioAssertion
    {
        public string Path { get; set; } = string.Empty;

        public override AssertionResult Evaluate(IGame game, uint frame)
        {
            if (!TryResolvePath(game, Path, out var actual, out var error))
            {
                return new AssertionResult
                {
                    AssertionId = Id,
                    Passed = false,
                    ActualValue = null,
                    FailFrame = frame,
                    Message = error,
                };
            }

            return new AssertionResult
            {
                AssertionId = Id,
                Passed = true,
                ActualValue = actual.ToString(CultureInfo.InvariantCulture),
            };
        }
    }

    private sealed record RunEndState(
        uint FinalFrame,
        bool HaltedOnOutcome,
        Dictionary<string, double> AssertionValues,
        Dictionary<string, int> SpawnedCounts,
        Dictionary<string, int> SurvivorCounts,
        Dictionary<string, int> KillCounts,
        Dictionary<string, int> LossCounts);

    private static RunEndState CaptureEndState(
        Game game,
        uint finalFrame,
        bool haltedOnOutcome,
        List<SideArmy> armies,
        KillLossTracker tracker)
    {
        var probes = DefaultAssertionPaths
            .Select(path => new EndStateProbeAssertion { Id = path, Path = path })
            .ToList();
        var results = new AssertionEvaluator(probes, game).Evaluate();

        var assertionValues = new Dictionary<string, double>();
        foreach (var (path, result) in DefaultAssertionPaths.Zip(results.Results))
        {
            if (result.ActualValue is not null
                && double.TryParse(result.ActualValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                assertionValues[path] = value;
            }
        }

        var survivorCounts = new Dictionary<string, int>();
        foreach (var (sideIndex, army) in armies.Index())
        {
            foreach (var group in army.Units.GroupBy(u => u.Definition.Name))
            {
                survivorCounts[$"side{sideIndex}:{group.Key}:survivors"] = group.Count(u => !u.IsEffectivelyDead);
            }
        }

        var playerSideIndexes = armies.Select(a => a.Player).ToArray();
        var killCounts = CountBySide(tracker.Kills, playerSideIndexes, "kills");
        var lossCounts = CountBySide(tracker.Losses, playerSideIndexes, "losses");

        return new RunEndState(
            finalFrame,
            haltedOnOutcome,
            assertionValues,
            SpawnedDictionary(armies),
            survivorCounts,
            killCounts,
            lossCounts);
    }

    private static Dictionary<string, int> CountBySide(
        IReadOnlyDictionary<(Player? Side, string Definition), int> counts,
        Player[] playerSideIndexes,
        string suffix)
    {
        var result = new Dictionary<string, int>();
        foreach (var (key, count) in counts)
        {
            var sideIndex = key.Side is null ? -1 : Array.IndexOf(playerSideIndexes, key.Side);
            var label = sideIndex < 0 ? $"unknown:{key.Definition}" : $"side{sideIndex}:{key.Definition}";
            result[$"{label}:{suffix}"] = count;
        }

        return result;
    }

    private static Dictionary<string, int> SpawnedDictionary(List<SideArmy> armies)
    {
        var result = new Dictionary<string, int>();
        foreach (var (sideIndex, army) in armies.Index())
        {
            foreach (var (definition, count) in army.Spawned)
            {
                result[$"side{sideIndex}:{definition}:spawned"] = count;
            }
        }

        return result;
    }

    /// <summary>
    /// Compares two runs. The assertion paths go through the standard P0 evaluator: the left
    /// run's recorded values become StateAssertions evaluated against the right run's still-open
    /// game, so the verdict is an AssertionReport. Engagement tables (survivors, kills, losses,
    /// spawn counts, final frame) compare as named rows carrying both values.
    /// </summary>
    private static BehavioralParityComparisonSet Compare(
        SideRun left,
        SideRun right,
        bool isDeterminismCheck,
        IReadOnlyList<string> assertionPaths,
        ITestHarnessReporter reporter)
    {
        var comparisons = new List<BehavioralParityComparison>();
        var leftEnd = left.EndState!;
        var rightEnd = right.EndState!;

        comparisons.Add(new BehavioralParityComparison(
            "run", "finalFrame",
            leftEnd.FinalFrame.ToString(CultureInfo.InvariantCulture),
            rightEnd.FinalFrame.ToString(CultureInfo.InvariantCulture),
            leftEnd.FinalFrame == rightEnd.FinalFrame));

        comparisons.Add(new BehavioralParityComparison(
            "run", "haltedOnOutcome",
            leftEnd.HaltedOnOutcome.ToString(CultureInfo.InvariantCulture),
            rightEnd.HaltedOnOutcome.ToString(CultureInfo.InvariantCulture),
            leftEnd.HaltedOnOutcome == rightEnd.HaltedOnOutcome));

        // P0 assertion layer: evaluate left-recorded expectations against the right game.
        if (right.Game is not null)
        {
            var assertions = assertionPaths
                .Where(leftEnd.AssertionValues.ContainsKey)
                .Select(path => new StateAssertion
                {
                    Id = path,
                    Path = path,
                    Op = "==",
                    Value = leftEnd.AssertionValues[path],
                })
                .ToList();
            var report = new AssertionEvaluator(assertions, right.Game).Evaluate();
            foreach (var (path, result) in assertionPaths.Zip(report.Results))
            {
                var expected = leftEnd.AssertionValues.TryGetValue(path, out var value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : "<missing>";
                comparisons.Add(new BehavioralParityComparison(
                    "assertion", path, expected, result.ActualValue ?? result.Message, result.Passed));
            }
        }
        else
        {
            foreach (var path in assertionPaths)
            {
                var hasLeft = leftEnd.AssertionValues.TryGetValue(path, out var leftValue);
                var hasRight = rightEnd.AssertionValues.TryGetValue(path, out var rightValue);
                comparisons.Add(new BehavioralParityComparison(
                    "assertion", path,
                    hasLeft ? leftValue.ToString(CultureInfo.InvariantCulture) : "<missing>",
                    hasRight ? rightValue.ToString(CultureInfo.InvariantCulture) : "<missing>",
                    hasLeft == hasRight && (!hasLeft || leftValue == rightValue)));
            }
        }

        AppendDictionaryComparisons(comparisons, "spawned", leftEnd.SpawnedCounts, rightEnd.SpawnedCounts);
        AppendDictionaryComparisons(comparisons, "survivors", leftEnd.SurvivorCounts, rightEnd.SurvivorCounts);
        AppendDictionaryComparisons(comparisons, "kills", leftEnd.KillCounts, rightEnd.KillCounts);
        AppendDictionaryComparisons(comparisons, "losses", leftEnd.LossCounts, rightEnd.LossCounts);

        var differing = comparisons.Where(c => !c.Passed).ToList();
        foreach (var diff in differing)
        {
            reporter.Error(
                $"DIFF {diff.Kind}/{diff.Name}: left({left.Record.Side}#{left.Record.RunIndex})="
                + $"{diff.Expected} right({right.Record.Side}#{right.Record.RunIndex})={diff.Actual}");
        }

        reporter.Info(
            $"comparison {left.Record.Side}#{left.Record.RunIndex} vs {right.Record.Side}#{right.Record.RunIndex}: "
            + $"{comparisons.Count - differing.Count} of {comparisons.Count} comparisons equal");

        return new BehavioralParityComparisonSet(
            $"{left.Record.Side}#{left.Record.RunIndex}",
            $"{right.Record.Side}#{right.Record.RunIndex}",
            isDeterminismCheck,
            differing.Count == 0,
            comparisons);
    }

    private static void AppendDictionaryComparisons(
        List<BehavioralParityComparison> comparisons,
        string kind,
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right)
    {
        foreach (var key in left.Keys.Union(right.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var hasLeft = left.TryGetValue(key, out var leftValue);
            var hasRight = right.TryGetValue(key, out var rightValue);
            comparisons.Add(new BehavioralParityComparison(
                kind,
                key,
                hasLeft ? leftValue.ToString(CultureInfo.InvariantCulture) : "<missing>",
                hasRight ? rightValue.ToString(CultureInfo.InvariantCulture) : "<missing>",
                hasLeft == hasRight && (!hasLeft || leftValue == rightValue)));
        }
    }

    private static BehavioralParityComparisonSet ErrorSet(BehavioralParityRunRecord left, BehavioralParityRunRecord right, string error) =>
        new($"{left.Side}#{left.RunIndex}", $"{right.Side}#{right.RunIndex}", true, false,
        [
            new BehavioralParityComparison("run", "error", "completed", error, false),
        ]);

    private static BehavioralParityRunRecord MakeErrorRecord(string side, int runIndex, uint seed, string contentPath, string error) =>
        new(side, runIndex, seed, contentPath, 0, false, "error",
            new Dictionary<string, double>(), new Dictionary<string, int>(), new Dictionary<string, int>(),
            new Dictionary<string, int>(), new Dictionary<string, int>(), error);

    private static BehavioralParityRunRecord BuildRecord(
        string side,
        int runIndex,
        uint seed,
        string contentPath,
        RunEndState endState) =>
        new(side, runIndex, seed, contentPath, endState.FinalFrame, endState.HaltedOnOutcome,
            DescribeOutcomeFromValues(endState.AssertionValues),
            endState.AssertionValues,
            endState.SpawnedCounts,
            endState.SurvivorCounts,
            endState.KillCounts,
            endState.LossCounts,
            null);

    private static string DescribeOutcome(Game game) =>
        $"{game.CurrentOutcome.Status}/{game.CurrentOutcome.Reason}";

    private static string DescribeOutcomeFromValues(IReadOnlyDictionary<string, double> values) =>
        values.TryGetValue("outcome.status", out var status) && values.TryGetValue("outcome.reason", out var reason)
            ? $"{(OutcomeStatus)(int)status}/{(OutcomeReason)(int)reason}"
            : "unknown";

    private static void ResetDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
    }

    /// <summary>Spawn Info lines drown headless stdout (the probe's flood control).</summary>
    private static void QuietenPerFrameLogging()
    {
        var loggingConfig = NLog.LogManager.Configuration;
        if (loggingConfig == null)
        {
            return;
        }

        foreach (var rule in loggingConfig.LoggingRules)
        {
            rule.DisableLoggingForLevel(NLog.LogLevel.Info);
        }

        NLog.LogManager.ReconfigExistingLoggers();
    }
}

/// <summary>Output knobs for <see cref="BehavioralParityRunner"/>.</summary>
public sealed record BehavioralParityOptions(
    string ArtifactsDirectory,
    string? ReportPath = null,
    string? CaptureDirectory = null,
    int CaptureStride = 15);
