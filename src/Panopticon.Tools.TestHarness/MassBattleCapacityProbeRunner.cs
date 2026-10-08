using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
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
            }

            // Hostile teams: the side index is the team index, so the armies fight on contact.
            var playerSettings = profile.Sides.Select((side, index) => new PlayerSetting(
                index,
                side.Side,
                MassBattlePlacement.ParseColor(side.Color, index),
                index,
                PlayerOwner.EasyAi,
                side.Id)).ToArray();
            game.StartSkirmishOrMultiPlayerGame(profile.Map.Path, new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
            game.StartRun();
            CameraZoomInitializer.ApplyOnce(game, config.MaxCameraZoomMultiplier);
            game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
            foreach (var player in game.Scene3D.Players)
            {
                player.DisableAI();
            }

            if (config.Headless)
            {
                game.EnableFixedLogicTimestep();
            }

            FrameDeployment(game, profile);

            Action step = config.Headless
                ? () => game.Update(Array.Empty<InputMessage>())
                : () => MassBattlePlacement.StepWindowed(game, window!, textureCopier!);

            var navigation = game.Scene3D.Navigation;
            var aircraft = profile.Capacity.Aircraft!;
            var reasons = new List<string>();

            var armies = new List<SideArmy>();
            for (var i = 0; i < profile.Sides.Count; i++)
            {
                armies.Add(SpawnSide(game, profile, i, navigation, aircraft, reporter, reasons));
            }

            for (var i = 0; i < SettleFrames; i++)
            {
                step();
            }

            IssueGeneralAttack(game, armies);

            var framesRun = RunEngagement(game, armies, step, frameBudget);
            var stopReason = DescribeStopReason(armies, framesRun, frameBudget);

            string? screenshotPath = null;
            if (!string.IsNullOrEmpty(config.MassBattleCapacityProbeScreenshotPath))
            {
                screenshotPath = CaptureScreenshot(game, config.MassBattleCapacityProbeScreenshotPath!, reporter);
            }

            var report = BuildReport(
                profile, profilePath, config.Headless, armies, reasons, frameBudget, framesRun, stopReason, screenshotPath);
            reporter.Info(MassBattleCapacityProbeReport.WriteText(report));

            var reportPath = string.IsNullOrEmpty(config.MassBattleCapacityProbeReportPath)
                ? System.IO.Path.Combine(AppContext.BaseDirectory, DefaultReportFileName)
                : config.MassBattleCapacityProbeReportPath!;
            MassBattleCapacityProbeReport.WriteJson(report, reportPath);
            reporter.Info($"Report JSON saved to: {reportPath}");

            return report.AllPassed ? 0 : ExitCheckFailed;
        }
        finally
        {
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
        NavigationGrid navigation,
        MassBattleAircraftProfile aircraft,
        ITestHarnessReporter reporter,
        List<string> reasons)
    {
        var side = profile.Sides[sideIndex];
        var ground = MassBattlePlacement.SpawnGroundArmy(
            game, profile, sideIndex, navigation, reporter, profile.Capacity.UnitsPerSide - aircraft.Count);
        var (airfields, airfieldReasons) = MassBattlePlacement.PlaceAirfields(
            game, profile, sideIndex, navigation, reporter);
        reasons.AddRange(ground.FailureReasons.Select(reason => $"{side.Id}: {reason}"));
        reasons.AddRange(airfieldReasons.Select(reason => $"{side.Id}: {reason}"));

        var jets = ParkAircraft(game, side.Id, sideIndex, aircraft, airfields, reasons);
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
    /// Spawns the side's aircraft directly into airfield parking slots using the same wiring the
    /// production exit uses (slot report, taxiing locomotor, base assignment, parked state,
    /// parking transform), so takeoff runs the real taxi and runway flow.
    /// </summary>
    private static List<JetSortie> ParkAircraft(
        Game game,
        string sideId,
        int sideIndex,
        MassBattleAircraftProfile aircraft,
        List<GameObject> airfields,
        List<string> reasons)
    {
        var owner = game.Scene3D.Players[2 + sideIndex];
        var definition = game.AssetStore.ObjectDefinitions.GetByName(aircraft.Definition);
        var jets = new List<JetSortie>();
        if (definition == null)
        {
            reasons.Add($"{sideId}: unknown aircraft definition '{aircraft.Definition}'");
            return jets;
        }

        var parking = airfields
            .Select(airfield => (Airfield: airfield, Parking: airfield.FindBehavior<ParkingPlaceBehaviour>()))
            .Where(pair => pair.Parking != null)
            .Select(pair => (pair.Airfield, Parking: pair.Parking!))
            .ToList();

        for (var i = 0; i < aircraft.Count; i++)
        {
            var slot = parking.FirstOrDefault(pair => pair.Parking.HasFreeSlots());
            if (slot == default)
            {
                reasons.Add($"{sideId}: no free parking slot for aircraft {i + 1} of {aircraft.Count}");
                break;
            }

            var jet = game.Scene3D.GameObjects.CreateObject(definition, owner);

            // Production marks a slot UnderConstruction when the queue starts; ReportSpawn claims
            // exactly that slot. Direct spawns compress both steps back to back.
            slot.Parking.EnqueueObject();
            slot.Parking.ReportSpawn(jet.Id);
            jet.AIUpdate.SetLocomotor(LocomotorSetType.Taxiing);
            var jetAI = (JetAIUpdate)jet.AIUpdate;
            jetAI.Base = slot.Airfield;
            jetAI.CurrentJetAIState = JetAIUpdate.JetAIState.Parked;
            jet.SetTransformMatrix(slot.Parking.GetUnitCreateTransform(producedAtHelipad: false, jet.Id).Matrix * slot.Airfield.TransformMatrix);
            jet.UpdateColliders();
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
    /// the real taxi and takeoff flow.
    /// </summary>
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

                unit.AIUpdate?.SetTargetPoint(army.OpposingCentroid);
                var enemy = NearestEnemy(unit.Translation, enemies);
                if (enemy != null)
                {
                    unit.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, enemy.Id));
                }
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
    private static int RunEngagement(Game game, List<SideArmy> armies, Action step, int frameBudget)
    {
        var allJets = armies.SelectMany(army => army.Jets).ToList();
        for (var frame = 0; frame < frameBudget; frame++)
        {
            step();
            TrackJetTransitions(allJets, frame);

            if (armies.Any(army => army.AliveUnits == 0))
            {
                return frame + 1;
            }

            if ((frame + 1) % OrderRefreshFrames == 0)
            {
                RefreshOrders(game, armies);
            }
        }

        return frameBudget;
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
    /// not AI.
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

                if (jetsByUnit.TryGetValue(unit, out var jet))
                {
                    // Only re-issue for a jet settled back into parking: mid-sortie states own it.
                    if (jet.Airborne || ((JetAIUpdate)unit.AIUpdate).CurrentJetAIState != JetAIUpdate.JetAIState.Parked)
                    {
                        continue;
                    }
                }
                else if (unit.AIUpdate?.TargetPoints.Count > 0)
                {
                    continue;
                }

                unit.AIUpdate?.SetTargetPoint(army.OpposingCentroid);
                var enemy = NearestEnemy(unit.Translation, enemies);
                if (enemy != null)
                {
                    unit.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, enemy.Id));
                }
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
        string? screenshotPath)
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
        };
        return report with { AllPassed = report.FailureReasons.Count == 0 };
    }

    private static void FrameDeployment(Game game, MassBattleDeploymentProfile profile)
    {
        var bounds = MassBattlePlacement.PlacementBounds(profile);
        var framingHeight = MathF.Max(
            4000f,
            1.25f * MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height));

        // Whole-map views outrun the default far plane (12000); without this bump the far
        // deployment clips out of view entirely.
        game.Scene3D.Camera.FarPlaneDistance = MathF.Max(12_000f, 3f * framingHeight);
        var view = game.Scene3D.TacticalView;
        view.SetAngleAndPitchToDefault();
        view.SnapHeightAboveGround(framingHeight);
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
