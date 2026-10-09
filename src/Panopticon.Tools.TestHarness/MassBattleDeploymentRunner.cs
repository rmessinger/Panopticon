using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Network;
using Veldrid;

// The Navigation class shares its name with its namespace; alias it to keep call sites readable.
using NavigationGrid = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-M0-03 deployment proof for the mass-battle map: loads the map named by a
/// <see cref="MassBattleDeploymentProfile"/> through a real skirmish start, then (1) moves one
/// representative unit per distinct ground locomotor class between the two zone centroids with
/// real pathfinded move orders, (2) spawns the full 1000-unit proxy roster per side inside the
/// zone rectangles at twice footprint spacing plus airfields behind each zone, (3) checks the
/// zone separation against the longest roster weapon range and the navigation grid between the
/// centroids, and (4) writes a JSON proof report. Zones and anchors are scenario data; the map
/// itself is never edited.
///
/// Reachability runs before the capacity spawn so the probes start and travel through empty
/// zones; the grid is locomotor-agnostic today, so classes group locomotors by template
/// <see cref="Surfaces"/> flags and one representative moves per class.
/// </summary>
public static class MassBattleDeploymentRunner
{
    public const string DefaultProfileFileName = "mass-battle-deepdivide-deployment.json";

    private const string DefaultReportFileName = "mass-battle-deployment-report.json";

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
            reporter.Error($"Could not load deployment profile '{profilePath}': {ex.Message}");
            return ExitSetupError;
        }

        if (profile.Sides.Count != 2)
        {
            reporter.Error($"Deployment profile needs exactly 2 sides, found {profile.Sides.Count}.");
            return ExitSetupError;
        }

        var blockSum = profile.Capacity.Blocks.Sum(block => block.Count);
        if (blockSum != profile.Capacity.UnitsPerSide)
        {
            reporter.Error(
                $"Capacity blocks sum to {blockSum} but unitsPerSide is {profile.Capacity.UnitsPerSide}; the zone proof must cover the full roster.");
            return ExitSetupError;
        }

        // Spawning two full armies generates the same per-frame Info log flood as the validation
        // ground; drop Info (Warn/Error still flow). Measurements are logic-frame counts.
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
                window = new GameWindow("Panopticon - Mass Battle Deployment", 100, 100, 1280, 800, config.Fullscreen);
                game = new Game(
                    installation,
                    config.Renderer,
                    new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud },
                    window);
                textureCopier = new TextureCopier(game, window.Swapchain.Framebuffer.OutputDescription);
            }

            var playerSettings = profile.Sides.Select((side, index) => new PlayerSetting(
                index,
                side.Side,
                ParseColor(side.Color, index),
                0,
                PlayerOwner.EasyAi,
                side.Id)).ToArray();
            game.StartSkirmishOrMultiPlayerGame(profile.Map.Path, new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
            game.StartRun();
            CameraZoomInitializer.ApplyOnce(game, config.MaxCameraZoomMultiplier);

            // The deployment's ending criteria (probes done, armies placed) are foreign to the
            // skirmish victory system; make it inert and keep all AI off the field.
            game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
            foreach (var player in game.Scene3D.Players)
            {
                player.DisableAI();
            }

            if (config.Headless)
            {
                game.EnableFixedLogicTimestep();
            }

            // Frame the whole deployment (both zones plus airfields) for the windowed run and the
            // proof screenshot; the gameplay-default camera height only shows a slice. Height
            // first, then aim: the look-at clamps against the constraint computed for the
            // current height.
            var zoneA = profile.Sides[0].Zone;
            var zoneB = profile.Sides[1].Zone;
            var view = game.Scene3D.TacticalView;
            var bounds = PlacementBounds(profile);
            var framingHeight = MathF.Max(
                4000f,
                1.25f * MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height));

            // Whole-map views outrun the default far plane (12000): without this bump the far
            // deployment clips out of view entirely.
            game.Scene3D.Camera.FarPlaneDistance = MathF.Max(12_000f, 3f * framingHeight);
            view.SetAngleAndPitchToDefault();
            view.SnapHeightAboveGround(framingHeight);
            view.LookAt(new Vector3(
                bounds.X + bounds.Width / 2f,
                bounds.Y + bounds.Height / 2f,
                0f));

            // Settle the camera through a render-time refresh without consuming a logic frame.
            var wasLogicRunning = game.IsLogicRunning;
            game.IsLogicRunning = false;
            game.Update(Array.Empty<InputMessage>());
            game.IsLogicRunning = wasLogicRunning;

            Action step = config.Headless
                ? () => game.Update(Array.Empty<InputMessage>())
                : () => StepWindowed(game, window!, textureCopier!);

            var navigation = game.Scene3D.Navigation;

            if (config.MassBattleDeploymentPreview)
            {
                reporter.Info("Deployment preview: map acceptance probes and proof report are disabled.");
                var capacityPassed = true;
                for (var i = 0; i < profile.Sides.Count; i++)
                {
                    var side = RunCapacityStage(game, profile, i, navigation, reporter);
                    capacityPassed &= side.Verdict == "OK";
                    reporter.Info($"{side.Id}: {side.UnitsSpawned} units, {side.AirfieldsPlaced} airfields, {side.Verdict}.");
                }

                if (!string.IsNullOrEmpty(config.MassBattleDeploymentScreenshotPath))
                {
                    CaptureScreenshot(game, config.MassBattleDeploymentScreenshotPath!, reporter);
                }

                if (!config.Headless)
                {
                    reporter.Info("Deployment ready. Close the window to exit.");
                    while (game.IsRunning)
                    {
                        step();
                    }
                }

                return capacityPassed ? 0 : ExitCheckFailed;
            }

            var reachability = RunReachabilityProbes(game, profile, step, reporter);
            var sideResults = new List<SideCapacityResult>();
            for (var i = 0; i < profile.Sides.Count; i++)
            {
                sideResults.Add(RunCapacityStage(game, profile, i, navigation, reporter));
            }

            var maxWeaponRange = sideResults.Max(side => side.LongestWeaponRange);

            var (gridPathNodes, gridEndPassable) = GridPathBetweenCentroids(navigation, profile);
            var separationGap = RectGap(zoneA, zoneB);

            string? screenshotPath = null;
            if (!string.IsNullOrEmpty(config.MassBattleDeploymentScreenshotPath))
            {
                screenshotPath = CaptureScreenshot(game, config.MassBattleDeploymentScreenshotPath!, reporter);
            }

            var report = new MassBattleDeploymentReport
            {
                Map = profile.Map.Name,
                Provenance = profile.Map.Provenance,
                ProfilePath = profilePath,
                ScreenshotPath = screenshotPath,
                Sides = sideResults,
                Separation = new SeparationResult { Gap = separationGap, MaxWeaponRange = maxWeaponRange },
                GridPathNodes = gridPathNodes,
                GridEndPassable = gridEndPassable,
                Reachability = reachability,
                AllPassed = false,
            };
            report = report with
            {
                AllPassed = sideResults.All(side => side.Verdict == "OK")
                    && report.Separation.Verdict == "OK"
                    && gridPathNodes > 0
                    && gridEndPassable
                    && reachability.All(probe => probe.Verdict == "OK"),
            };

            reporter.Info(MassBattleDeploymentReport.WriteText(report));

            var reportPath = string.IsNullOrEmpty(config.MassBattleDeploymentReportPath)
                ? System.IO.Path.Combine(AppContext.BaseDirectory, DefaultReportFileName)
                : config.MassBattleDeploymentReportPath!;
            MassBattleDeploymentReport.WriteJson(report, reportPath);
            reporter.Info($"Report JSON saved to: {reportPath}");

            if (!config.Headless)
            {
                reporter.Info("Deployment ready. Close the window to exit.");
                while (game.IsRunning)
                {
                    step();
                }
            }

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

    /// <summary>
    /// Spawns one representative per distinct ground locomotor class at zone A, orders each to the
    /// zone B centroid through a real pathfinded move order, and polls until every probe arrives
    /// or its budget runs out.
    /// </summary>
    private static List<ReachabilityClassResult> RunReachabilityProbes(
        Game game,
        MassBattleDeploymentProfile profile,
        Action step,
        ITestHarnessReporter reporter)
    {
        var zoneA = profile.Sides[0].Zone;
        var zoneB = profile.Sides[1].Zone;
        var owner = game.Scene3D.Players[2];
        var target = GroundPosition(game, zoneB.CenterX, zoneB.CenterY);
        var classes = EnumerateGroundLocomotorClasses(game).ToList();
        var probes = new List<ReachabilityProbe>();

        for (var i = 0; i < classes.Count; i++)
        {
            var (surfaces, representative) = classes[i];
            var definition = game.AssetStore.ObjectDefinitions.GetByName(representative.DefinitionName)
                ?? throw new InvalidOperationException(
                    $"Representative '{representative.DefinitionName}' for surfaces {surfaces} disappeared from the asset store.");
            var lateral = (i - (classes.Count - 1) / 2f) * 80f;
            var spawn = GroundPosition(game, zoneA.CenterX + lateral, zoneA.CenterY);
            var yaw = YawToward(spawn, target);
            var unit = SpawnAt(game, definition, owner, spawn.X, spawn.Y, yaw);
            var distance = Vector3.Distance(unit.Translation, target);
            var computedBudget = (int)(distance / MathF.Max(representative.MaxSpeed, 0.001f) * profile.Reachability.MaxSpeedSafetyFactor)
                + profile.Reachability.MinTimeoutFrames;
            var budget = Math.Clamp(computedBudget, profile.Reachability.MinTimeoutFrames, profile.Reachability.MaxFrameBudget);
            unit.AIUpdate?.SetTargetPoint(target);
            probes.Add(new ReachabilityProbe
            {
                Surfaces = surfaces,
                RepresentativeDefinition = representative.DefinitionName,
                MaxSpeed = representative.MaxSpeed,
                Unit = unit,
                Target = target,
                FrameBudget = budget,
                Distance = distance,
                StartFrame = CurrentLogicFrame(game),
            });
            reporter.Info(
                $"Reachability probe {surfaces} via {representative.DefinitionName}: {distance.ToString("F0", CultureInfo.InvariantCulture)} world units, budget {budget} frames.");
        }

        var maxElapsed = probes.Count == 0 ? 0 : probes.Max(probe => probe.FrameBudget);
        var finishFrame = CurrentLogicFrame(game);
        for (var elapsed = 0; elapsed < maxElapsed; elapsed++)
        {
            step();
            finishFrame = CurrentLogicFrame(game);
            foreach (var probe in probes.Where(probe => !probe.Finished))
            {
                if (probe.Unit.AIUpdate?.TargetPoints.Count > 0)
                {
                    probe.SawPath = true;
                }

                if (probe.Unit.IsEffectivelyDead
                    || (probe.SawPath && probe.Unit.AIUpdate?.TargetPoints.Count == 0))
                {
                    probe.Finished = true;
                    probe.FinishFrame = finishFrame;
                }
            }

            if (probes.All(probe => probe.Finished))
            {
                break;
            }
        }

        var results = new List<ReachabilityClassResult>();
        foreach (var probe in probes)
        {
            var frames = (int)((probe.Finished ? probe.FinishFrame : finishFrame) - probe.StartFrame);
            string verdict;
            var reason = "";
            if (probe.Unit.IsEffectivelyDead)
            {
                verdict = "FAIL";
                reason = "representative died in transit";
            }
            else if (!probe.SawPath)
            {
                verdict = "FAIL";
                reason = "no path was ever computed (TargetPoints stayed empty)";
            }
            else if (probe.Unit.AIUpdate?.TargetPoints.Count > 0)
            {
                verdict = "FAIL";
                reason = $"move did not complete within the {probe.FrameBudget}-frame budget";
            }
            else if (Vector3.Distance(probe.Unit.Translation, probe.Target) > profile.Reachability.ArrivalRadius)
            {
                verdict = "FAIL";
                reason =
                    $"move completed {Vector3.Distance(probe.Unit.Translation, probe.Target).ToString("F0", CultureInfo.InvariantCulture)} units from the target (arrival radius {profile.Reachability.ArrivalRadius.ToString("F0", CultureInfo.InvariantCulture)})";
            }
            else
            {
                verdict = "OK";
            }

            results.Add(new ReachabilityClassResult
            {
                Surfaces = probe.Surfaces.ToString(),
                RepresentativeDefinition = probe.RepresentativeDefinition,
                MaxSpeed = probe.MaxSpeed,
                Distance = probe.Distance,
                FrameBudget = probe.FrameBudget,
                Frames = frames,
                Verdict = verdict,
                Reason = reason,
            });
        }

        return results;
    }

    private sealed record GroundClass(string DefinitionName, float MaxSpeed);

    /// <summary>
    /// Distinct ground locomotor classes among object definitions that have an AIUpdate: grouped
    /// by locomotor template <see cref="Surfaces"/> flags, excluding air-only locomotors. The
    /// representative per class is its fastest definition (the class's best crossing capability;
    /// ties break alphabetically).
    /// </summary>
    private static IEnumerable<(Surfaces Surfaces, GroundClass Representative)> EnumerateGroundLocomotorClasses(Game game)
    {
        var classes = new SortedDictionary<Surfaces, GroundClass>();
        foreach (var definition in game.AssetStore.ObjectDefinitions)
        {
            if (definition.LocomotorSets.Count == 0
                || !definition.Behaviors.Values.Any(container => container.Data is AIUpdateModuleData))
            {
                continue;
            }

            foreach (var set in definition.LocomotorSets.Values)
            {
                foreach (var reference in set.Locomotors)
                {
                    var template = reference.Value;
                    if (!template.Surfaces.HasFlag(Surfaces.Ground) || template.Surfaces.HasFlag(Surfaces.Air))
                    {
                        continue;
                    }

                    var candidate = new GroundClass(definition.Name, template.MaxSpeed);
                    if (!classes.TryGetValue(template.Surfaces, out var existing)
                        || candidate.MaxSpeed > existing.MaxSpeed
                        || (candidate.MaxSpeed == existing.MaxSpeed
                            && string.CompareOrdinal(definition.Name, existing.DefinitionName) < 0))
                    {
                        classes[template.Surfaces] = candidate;
                    }
                }
            }
        }

        foreach (var (surfaces, representative) in classes)
        {
            yield return (surfaces, representative);
        }
    }

    /// <summary>
    /// Spawns one side's full proxy roster inside its zone: blocks are laid out row-major at
    /// twice footprint spacing (floored at the profile's minimum pitch), skipping grid slots
    /// that fall on impassable cells (the flat bands contain tree clusters and other holes),
    /// so every unit lands on passable ground with spacing preserved. Airfields spawn at
    /// their anchors, which must sit outside the zone and on the far side from the opposing
    /// zone.
    /// </summary>
    private static SideCapacityResult RunCapacityStage(
        Game game,
        MassBattleDeploymentProfile profile,
        int sideIndex,
        NavigationGrid navigation,
        ITestHarnessReporter reporter)
    {
        var side = profile.Sides[sideIndex];
        var zone = side.Zone;
        var opposing = profile.Sides[1 - sideIndex].Zone;
        var owner = game.Scene3D.Players[2 + sideIndex];
        var yaw = MathF.PI * side.FacingDegrees / 180f;
        var reasons = new List<string>();
        var spawned = 0;
        var impassable = 0;
        var skippedSlots = 0;
        var longestWeaponRange = 0f;
        var cursorY = zone.Y;
        var column = 0;

        foreach (var block in profile.Capacity.Blocks)
        {
            var definition = game.AssetStore.ObjectDefinitions.GetByName(block.Definition);
            if (definition == null)
            {
                reporter.Error($"Unknown object definition '{block.Definition}' in capacity block.");
                reasons.Add($"unknown definition '{block.Definition}'");
                continue;
            }

            var pitch = MathF.Max(profile.Capacity.SpacingFactor * 2f * definition.Geometry.MajorRadius, profile.Capacity.MinPitch);
            var columns = Math.Max(1, (int)(zone.Width / pitch));

            // Start every block on a fresh row; the roster fits several times over, so the blank
            // strip is cheaper than mixing per-block pitches on one row.
            if (column != 0)
            {
                column = 0;
                cursorY += pitch;
            }

            var placed = 0;
            while (placed < block.Count)
            {
                if (cursorY + pitch > zone.Y + zone.Height)
                {
                    reasons.Add(
                        $"block '{block.Definition}' exhausted the zone: {placed.ToString(CultureInfo.InvariantCulture)} of {block.Count.ToString(CultureInfo.InvariantCulture)} placed before running out of passable ground");
                    break;
                }

                var x = zone.X + pitch / 2f + column * pitch;
                var y = cursorY + pitch / 2f;
                column++;
                if (column >= columns)
                {
                    column = 0;
                    cursorY += pitch;
                }

                if (!navigation.IsPassable(GroundPosition(game, x, y)))
                {
                    skippedSlots++;
                    continue;
                }

                var unit = SpawnAt(game, definition, owner, x, y, yaw);
                if (!navigation.IsPassable(unit.Translation))
                {
                    impassable++;
                }

                if (placed == 0)
                {
                    longestWeaponRange = MathF.Max(longestWeaponRange, unit.CurrentWeapon?.Template.AttackRange ?? 0f);
                }

                placed++;
                spawned++;
            }
        }

        if (spawned != profile.Capacity.UnitsPerSide)
        {
            reasons.Add($"spawned {spawned} of {profile.Capacity.UnitsPerSide} requested units");
        }

        if (impassable > 0)
        {
            reasons.Add($"{impassable} spawn points are on impassable grid cells");
        }

        // Behind = on the far side of the own zone from the opposing zone (between the zone and
        // the map edge), never inside the deployment rectangle itself.
        var retreatX = zone.CenterX - opposing.CenterX;
        var retreatY = zone.CenterY - opposing.CenterY;
        var airfieldsPlaced = 0;
        foreach (var anchor in side.AirfieldAnchors)
        {
            var definition = game.AssetStore.ObjectDefinitions.GetByName(side.AirfieldDefinition);
            if (definition == null)
            {
                reporter.Error($"Unknown airfield definition '{side.AirfieldDefinition}'.");
                reasons.Add($"unknown airfield definition '{side.AirfieldDefinition}'");
                continue;
            }

            var position = GroundPosition(game, anchor.X, anchor.Y);
            if (position.Z <= 0f && game.Scene3D.Terrain.GetGroundHeight(new Vector2(anchor.X, anchor.Y)) == null)
            {
                reasons.Add($"airfield anchor {anchor.X.ToString("F0", CultureInfo.InvariantCulture)},{anchor.Y.ToString("F0", CultureInfo.InvariantCulture)} is outside the map");
                continue;
            }

            if (zone.Contains(anchor.X, anchor.Y))
            {
                reasons.Add($"airfield anchor {anchor.X.ToString("F0", CultureInfo.InvariantCulture)},{anchor.Y.ToString("F0", CultureInfo.InvariantCulture)} is inside the deployment zone");
                continue;
            }

            if ((anchor.X - zone.CenterX) * retreatX + (anchor.Y - zone.CenterY) * retreatY <= 0f)
            {
                reasons.Add($"airfield anchor {anchor.X.ToString("F0", CultureInfo.InvariantCulture)},{anchor.Y.ToString("F0", CultureInfo.InvariantCulture)} is not behind the zone");
                continue;
            }

            if (!navigation.IsPassable(position))
            {
                reasons.Add($"airfield anchor {anchor.X.ToString("F0", CultureInfo.InvariantCulture)},{anchor.Y.ToString("F0", CultureInfo.InvariantCulture)} is on impassable ground");
                continue;
            }

            SpawnAt(game, definition, owner, anchor.X, anchor.Y, yaw);
            airfieldsPlaced++;
        }

        if (airfieldsPlaced != side.AirfieldAnchors.Count)
        {
            reasons.Add($"placed {airfieldsPlaced} of {side.AirfieldAnchors.Count} airfields");
        }

        return new SideCapacityResult
        {
            Id = side.Id,
            ZoneX = zone.X,
            ZoneY = zone.Y,
            ZoneWidth = zone.Width,
            ZoneHeight = zone.Height,
            UnitsSpawned = spawned,
            UnitsRequested = profile.Capacity.UnitsPerSide,
            ImpassableSpawns = impassable,
            ImpassableSlotsSkipped = skippedSlots,
            AirfieldsPlaced = airfieldsPlaced,
            AirfieldAnchors = side.AirfieldAnchors,
            LongestWeaponRange = longestWeaponRange,
            FailureReasons = reasons,
        };
    }

    private static (int Nodes, bool EndPassable) GridPathBetweenCentroids(NavigationGrid navigation, MassBattleDeploymentProfile profile)
    {
        var zoneA = profile.Sides[0].Zone;
        var zoneB = profile.Sides[1].Zone;
        var start = new Vector3(zoneA.CenterX, zoneA.CenterY, 0f);
        var end = new Vector3(zoneB.CenterX, zoneB.CenterY, 0f);
        var path = navigation.CalculatePath(start, end, out var endPassable);
        return (path.Count, endPassable);
    }

    /// <summary>World-space bounding box over all zone rectangles and airfield anchors.</summary>
    private static (float X, float Y, float Width, float Height) PlacementBounds(MassBattleDeploymentProfile profile)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        foreach (var side in profile.Sides)
        {
            minX = MathF.Min(minX, side.Zone.X);
            minY = MathF.Min(minY, side.Zone.Y);
            maxX = MathF.Max(maxX, side.Zone.X + side.Zone.Width);
            maxY = MathF.Max(maxY, side.Zone.Y + side.Zone.Height);
            foreach (var anchor in side.AirfieldAnchors)
            {
                minX = MathF.Min(minX, anchor.X);
                minY = MathF.Min(minY, anchor.Y);
                maxX = MathF.Max(maxX, anchor.X);
                maxY = MathF.Max(maxY, anchor.Y);
            }
        }

        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static float RectGap(MassBattleRect a, MassBattleRect b)
    {
        var dx = MathF.Max(0f, MathF.Max(b.X - (a.X + a.Width), a.X - (b.X + b.Width)));
        var dy = MathF.Max(0f, MathF.Max(b.Y - (a.Y + a.Height), a.Y - (b.Y + b.Height)));
        return dx == 0f && dy == 0f ? 0f : MathF.Sqrt(dx * dx + dy * dy);
    }

    private static Vector3 GroundPosition(Game game, float x, float y)
    {
        var height = game.Scene3D.Terrain.GetGroundHeight(new Vector2(x, y)) ?? 0f;
        return new Vector3(x, y, height);
    }

    private static float YawToward(Vector3 from, Vector3 to) =>
        MathF.Atan2(to.Y - from.Y, to.X - from.X);

    private static GameObject SpawnAt(Game game, ObjectDefinition definition, Player owner, float x, float y, float yawRadians)
    {
        var position = GroundPosition(game, x, y);
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        var rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, yawRadians);
        gameObject.UpdateTransform(position, rotation);
        // Colliders only refresh on the next logic tick; refresh immediately so culling and any
        // pre-tick inspection see the object at its spawn point.
        gameObject.UpdateColliders();
        return gameObject;
    }

    private static string ResolveProfilePath(TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        if (!string.IsNullOrWhiteSpace(config.MassBattleDeploymentProfilePath))
        {
            if (System.IO.File.Exists(config.MassBattleDeploymentProfilePath))
            {
                return config.MassBattleDeploymentProfilePath;
            }

            reporter.Error($"MassBattleDeploymentProfilePath '{config.MassBattleDeploymentProfilePath}' does not exist.");
            return null!;
        }

        var embedded = System.IO.Path.Combine(AppContext.BaseDirectory, DefaultProfileFileName);
        if (System.IO.File.Exists(embedded))
        {
            return embedded;
        }

        reporter.Error(
            $"No deployment profile: set MassBattleDeploymentProfilePath or ship '{DefaultProfileFileName}' beside the executable.");
        return null!;
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
            reporter.Info($"Deployment screenshot saved to: {path}");
            return path;
        }
        catch (Exception ex)
        {
            reporter.Error($"Screenshot capture failed: {ex.Message}");
            return null;
        }
    }

    private static void StepWindowed(Game game, GameWindow window, TextureCopier textureCopier)
    {
        if (!window.PumpEvents())
        {
            game.Exit();
            return;
        }

        game.Update(window.MessageQueue);
        game.Panel.EnsureFrame(window.DrawableBounds);
        game.Render();
        textureCopier.Execute(
            game.Panel.Framebuffer.ColorTargets[0].Target,
            window.Swapchain.Framebuffer);
        game.GraphicsDevice.SwapBuffers(window.Swapchain);
        window.MessageQueue.Clear();
    }

    private static ColorRgb ParseColor(string? hex, int playerIndex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            var trimmed = hex.TrimStart('#');
            if (uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)
                && trimmed.Length == 6)
            {
                return new ColorRgb(
                    (byte)((packed >> 16) & 0xFF),
                    (byte)((packed >> 8) & 0xFF),
                    (byte)(packed & 0xFF));
            }
        }

        return playerIndex == 0 ? new ColorRgb(0x40, 0x9F, 0xFF) : new ColorRgb(0xFF, 0x50, 0x40);
    }

    private static uint CurrentLogicFrame(Game game) =>
        unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));
}
