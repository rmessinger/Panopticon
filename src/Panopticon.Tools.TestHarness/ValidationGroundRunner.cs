using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Runs a <see cref="ValidationGroundRunPlan"/> to completion: spawns the roster, settles, issues
/// each unit's primary action, polls until every unit resolves (or the frame cap hits), then
/// reports the timing results and, if configured, compares against or refreshes the golden
/// baseline. Pure execution — plan construction happens in <see cref="ScenarioBuilder"/>, status
/// text goes through the injected <see cref="ITestHarnessReporter"/>.
/// </summary>
public static class ValidationGroundRunner
{
    // Run until every unit has resolved (timed out, completed, or gapped) or the cap hits, unless
    // ExitAfterTargetsDestroyed is false (windowed), in which case it runs until the window closes.
    private const uint MaxRunFrames = 5_000;

    public static void Run(GameInstallation installation, TestHarnessConfig config, ValidationGroundRunPlan runPlan, ITestHarnessReporter reporter)
    {
        // The validation ground runs thousands of frames with the full roster fighting; the engine's
        // per-frame Info logs (weapon/animation state transitions) would otherwise produce gigabytes
        // of output and dominate run time. The measurement is logic-frame counts, unaffected by log
        // verbosity, so drop the Info level (Warn/Error still flow).
        var loggingConfig = NLog.LogManager.Configuration;
        if (loggingConfig != null)
        {
            foreach (var rule in loggingConfig.LoggingRules)
            {
                rule.DisableLoggingForLevel(NLog.LogLevel.Info);
            }

            NLog.LogManager.ReconfigExistingLoggers();
        }

        var scenario = runPlan.Scenario;
        var plan = runPlan.SpawnPlan;
        var totalUnits = plan.UnitPlacements.Count;

        Game? game = null;
        GameWindow? window = null;
        TextureCopier? textureCopier = null;
        ValidationGroundOverlay? overlay = null;
        try
        {
            if (config.Headless)
            {
                game = new Game(installation, config.Renderer, new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud }, null);
            }
            else
            {
                Platform.Start();
                window = new GameWindow("Panopticon - Unit Validation Ground", 100, 100, 1280, 800, config.Fullscreen);
                game = new Game(installation, config.Renderer, new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud }, window);
                textureCopier = new TextureCopier(game, window.Swapchain.Framebuffer.OutputDescription);
            }

            game.StartSkirmishOrMultiPlayerGame(scenario.MapPath, new Network.EchoConnection(), runPlan.PlayerSettings, scenario.Seed, isMultiPlayer: false);
            game.StartRun();
            CameraZoomInitializer.ApplyOnce(game, config.MaxCameraZoomMultiplier);

            // The validation ground's ending criteria are foreign to the skirmish victory system
            // (every unit resolved, frame cap, or window close), so make it inert rather than
            // appeasing its defeat rule with spawned structures: with no starting buildings and no
            // economy, the default rule would mark every roster player defeated and freeze logic.
            game.VictorySystem.ConditionRule = new Panopticon.Logic.NeverDefeatedVictoryConditionRule();

            // The default tactical view looks at the map's authored centre at the game's default
            // zoom; re-aim the camera at the formation and zoom out until every cluster (units,
            // standoff targets, and supply sources) fits on screen, so windowed runs open on the
            // whole roster. Cluster placement spreads factions hundreds of world units apart.
            var placements = runPlan.SpawnPlan.UnitPlacements;
            var framed = new List<System.Numerics.Vector2>(placements.Count + runPlan.SpawnPlan.TargetPlacements.Count + runPlan.SpawnPlan.SupplySourcePlacements.Count);
            foreach (var p in placements)
            {
                framed.Add(new System.Numerics.Vector2(p.Position.X, p.Position.Y));
            }

            foreach (var p in runPlan.SpawnPlan.TargetPlacements)
            {
                framed.Add(new System.Numerics.Vector2(p.Position.X, p.Position.Y));
            }

            foreach (var p in runPlan.SpawnPlan.SupplySourcePlacements)
            {
                framed.Add(new System.Numerics.Vector2(p.Position.X, p.Position.Y));
            }

            var view = game.Scene3D.TacticalView;
            view.LookAt(framed.Count > 0
                ? new System.Numerics.Vector3(
                    framed.Average(p => p.X),
                    framed.Average(p => p.Y),
                    0f)
                : new System.Numerics.Vector3(scenario.Layout.TargetClusterCenter.X, scenario.Layout.TargetClusterCenter.Y, 0f));
            view.InitHeightForMap();
            view.SetAngleAndPitchToDefault();
            view.SetZoomToDefault();
            ValidationGroundCamera.FrameFormation(game.Scene3D, framed);

            // The validation ground spawns and orders units directly; an active skirmish AI would
            // both dominate per-frame cost and override those orders. Per deep dive §2.1 ("no AI,
            // no economy"), disable AI on the three roster players (neutral/civilian at 0/1 have none).
            foreach (var idx in new[] { 2, 3, 4 })
            {
                game.Scene3D.Players[idx].DisableAI();
            }

            // Headless runs deterministically (fixed-timestep, logic-only) for the regression gate.
            // Windowed renders every frame at real-time pace so a human can watch the roster act, and
            // only issues orders after the settle stage has rendered the scene.
            if (config.Headless)
            {
                game.EnableFixedLogicTimestep();
            }

            var host = new ValidationGroundHost();
            host.Spawn(plan, game, scenario.TargetHp);

            var timer = new UnitActionTimer();
            foreach (var gap in host.Gaps)
            {
                timer.RecordGap(gap.DefinitionName, gap.FactionIndex, gap.ActionKind, gap.Reason);
            }

            // In windowed hold mode the run starts paused with the orders held: the formation can be
            // inspected (camera pans/zooms while paused), and Space (or the overlay button) resumes,
            // runs the settle stage, and issues the orders. Headless always auto-issues after the
            // settle stage for byte-identical regression reports.
            var holdOrders = !config.Headless && config.ValidationGroundHoldOrders;
            var ordersIssued = false;
            var observe = host.BuildObservationSeam(game);
            uint startFrame = 0;

            Action step;
            if (config.Headless)
            {
                // Offscreen overlay screenshots (docs/screenshots.md): lazily construct the ImGui
                // overlay windowless inside the step (the panel framebuffer must exist to supply
                // its output description) and render it onto the scene for capture. Reuses the
                // overlay local so the finally block disposes it.
                step = () => StepLogicOnly(game, ref overlay);
            }
            else
            {
                overlay = new ValidationGroundOverlay(game, window!);
                step = () => StepOnce(game, window!, textureCopier!, overlay!);
            }

            if (holdOrders)
            {
                game.IsLogicRunning = false;
                reporter.Info($"Holding orders: the scenario starts paused. Resume (and attack) with Space or the overlay button. ValidationGroundHoldOrders=false disables this.");
            }
            else
            {
                RunSettleStage(step, scenario.SettleFrames);
                IssueOrders(host, game, timer, ref ordersIssued, ref startFrame);
            }

            // Headless always exits on completion: there is no window to close, so suppressing the
            // exit would hang. In windowed mode, ExitAfterTargetsDestroyed=false keeps the game
            // running after the roster has acted so it can be observed; the loop then ends only when
            // the window is closed (game.IsRunning flips to false).
            var exitOnCompletion = config.ExitAfterTargetsDestroyed || config.Headless;
            if (!exitOnCompletion)
            {
                reporter.Info("ExitAfterTargetsDestroyed is false; the game will remain running until the window is closed.");
            }

            while (game.IsRunning)
            {
                if (overlay != null)
                {
                    var progress = timer.BuildReport();
                    overlay.State = new ValidationGroundOverlayState(
                        HoldingOrders: holdOrders && !ordersIssued,
                        Paused: !game.IsLogicRunning,
                        ResolvedUnits: progress.TotalUnitCount,
                        TotalUnits: totalUnits,
                        Gaps: progress.Gaps.Count);
                }

                step();

                // Space / the overlay button: resumes from the starting hold (issuing the orders),
                // or toggles active pause once the run is under way. Paused logic frames do not
                // advance, so pausing never consumes a unit's frame budget.
                if (overlay?.PrimaryActionRequested == true)
                {
                    if (holdOrders && !ordersIssued)
                    {
                        game.IsLogicRunning = true;
                        RunSettleStage(step, scenario.SettleFrames);
                        IssueOrders(host, game, timer, ref ordersIssued, ref startFrame);
                    }
                    else
                    {
                        game.IsLogicRunning = !game.IsLogicRunning;
                    }
                }

                var frame = CurrentLogicFrame(game);
                if (ordersIssued)
                {
                    timer.Poll(frame, observe);
                    // A unit that destroys its target has finished; clear its weapon so it stops firing
                    // (and its turret stops auto-acquiring new enemies), keeping the run bounded.
                    host.DisarmUnitsWhoseTargetIsDestroyed(game);

                    if (exitOnCompletion)
                    {
                        var report = timer.BuildReport();
                        if (report.TotalUnitCount >= totalUnits || frame - startFrame > MaxRunFrames)
                        {
                            break;
                        }
                    }
                }
            }

            Finish(timer, config, reporter);
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

    private static void RunSettleStage(Action step, int settleFrames)
    {
        // Settle stage: no orders until this many frames have elapsed. In windowed mode this also
        // renders the scene before any order is issued.
        for (var i = 0; i < settleFrames; i++)
        {
            step();
        }
    }

    private static void IssueOrders(ValidationGroundHost host, Game game, UnitActionTimer timer, ref bool ordersIssued, ref uint startFrame)
    {
        if (ordersIssued)
        {
            return;
        }

        host.IssueOrders(game, timer);
        startFrame = CurrentLogicFrame(game);
        ordersIssued = true;
    }

    private static void Finish(UnitActionTimer timer, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var report = timer.BuildReport();
        UnitTimingBaseline? baseline = null;
        if (!string.IsNullOrEmpty(config.ValidationGroundBaselinePath))
        {
            baseline = UnitTimingBaseline.Load(config.ValidationGroundBaselinePath!);
        }

        reporter.Info(ValidationGroundReportWriter.WriteTextReport(report, baseline));

        if (!string.IsNullOrEmpty(config.ValidationGroundReportPath))
        {
            ValidationGroundReportWriter.WriteJson(report, baseline, config.ValidationGroundReportPath!);
            reporter.Info($"Report JSON saved to: {config.ValidationGroundReportPath}");
        }

        if (baseline != null)
        {
            if (config.ValidationGroundRefreshBaseline)
            {
                RefreshBaseline(baseline, report, config.ValidationGroundBaselinePath!, reporter);
                return;
            }

            if (baseline.HasRegression(report) || report.Timeouts.Count > 0)
            {
                Environment.ExitCode = 2;
            }
        }
        else if (report.Timeouts.Count > 0)
        {
            Environment.ExitCode = 3;
        }
    }

    private static void RefreshBaseline(UnitTimingBaseline baseline, ValidationGroundReport report, string path, ITestHarnessReporter reporter)
    {
        // Capture measured timings as the new golden values. Only run under the explicit refresh flag.
        baseline.Entries.Clear();
        foreach (var timing in report.Timings)
        {
            baseline.Entries.Add(new UnitTimingBaselineEntry
            {
                DefinitionName = timing.DefinitionName,
                FactionIndex = timing.FactionIndex,
                ActionKind = timing.ActionKind.ToString(),
                GoldenFrames = timing.Frames,
                TolerancePercent = 10.0,
                MinimumFrameFloor = 3,
            });
        }

        baseline.Save(path);
        reporter.Info($"Baseline refreshed at: {path}");
    }

    /// <summary>
    /// Advances the simulation without rendering. Used by the validation ground, whose measurement
    /// is logic-frame counts: rendering a full scene with the roster fighting dominates run time
    /// and adds nothing to the timing. MapTime still advances via the game's delta timer, so logic
    /// frames tick as Update is called.
    /// </summary>
    private static void StepLogicOnly(Game game, ref ValidationGroundOverlay? overlayShot)
    {
        game.Update(Array.Empty<Input.InputMessage>());

        // Env-gated offscreen capture for visual validation (docs/screenshots.md): renders every
        // logic step and writes a PNG, capped so a frame-capped run cannot fill the disk. The
        // overlay, when present, renders on top of the scene before the capture.
        var captureRequested = overlayShot != null
            || Environment.GetEnvironmentVariable("TESTHARNESS_VG_CAPTURE") is not null
            || Environment.GetEnvironmentVariable("TESTHARNESS_VG_OVERLAY_SHOT") is not null;
        if (captureRequested && VgCaptureCount < MaxVgCaptures)
        {
            game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, 1024, 768));
            if (overlayShot == null && Environment.GetEnvironmentVariable("TESTHARNESS_VG_OVERLAY_SHOT") is not null)
            {
                overlayShot = new ValidationGroundOverlay(
                    game,
                    game.Panel.Framebuffer.OutputDescription,
                    (int)game.Panel.Framebuffer.Width,
                    (int)game.Panel.Framebuffer.Height);
            }

            game.Render();
            if (overlayShot != null)
            {
                overlayShot.Update(1 / 30f, new EmptyInputSnapshot());
                overlayShot.Render(game.GraphicsDevice);
            }

            VgCapture ??= new FrameCaptureService();
            VgCaptureCount++;
            VgCapture!.Capture(
                game.GraphicsDevice,
                game.Panel.Framebuffer.ColorTargets[0].Target,
                $"vg frame {VgCaptureCount}");
        }
    }

    private sealed class EmptyInputSnapshot : InputSnapshot
    {
        public IReadOnlyList<KeyEvent> KeyEvents { get; } = Array.Empty<KeyEvent>();

        public IReadOnlyList<MouseEvent> MouseEvents { get; } = Array.Empty<MouseEvent>();

        public IReadOnlyList<char> KeyCharPresses { get; } = Array.Empty<char>();

        public System.Numerics.Vector2 MousePosition { get; } = new(-100, -100);

        public float WheelDelta { get; }

        public bool IsMouseDown(MouseButton button) => false;
    }

    private const int MaxVgCaptures = 500;
    private static FrameCaptureService? VgCapture;
    private static int VgCaptureCount;

    private static void StepOnce(Game game, GameWindow window, TextureCopier textureCopier, ValidationGroundOverlay overlay)
    {
        if (!window.PumpEvents())
        {
            game.Exit();
            return;
        }

        overlay.Update((float)game.RenderTime.DeltaTime.TotalSeconds, window.CurrentInputSnapshot);
        FilterOverlayInput(window, overlay);

        game.Update(window.MessageQueue);
        game.Panel.EnsureFrame(window.ClientBounds);
        game.Render();
        textureCopier.Execute(
            game.Panel.Framebuffer.ColorTargets[0].Target,
            window.Swapchain.Framebuffer);
        overlay.Render(game.GraphicsDevice);
        game.GraphicsDevice.SwapBuffers(window.Swapchain);
        window.MessageQueue.Clear();
    }

    /// <summary>
    /// Space is the overlay's hotkey and never reaches the game; mouse input is dropped on frames
    /// where ImGui captures the mouse, so clicking the overlay does not select units or issue
    /// orders in the world.
    /// </summary>
    private static void FilterOverlayInput(GameWindow window, ValidationGroundOverlay overlay)
    {
        if (window.MessageQueue.Count == 0)
        {
            return;
        }

        var kept = new List<InputMessage>(window.MessageQueue.Count);
        while (window.MessageQueue.TryDequeue(out var message))
        {
            var isSpaceKey = message.MessageType is InputMessageType.KeyDown or InputMessageType.KeyUp
                && message.Value.Key == Key.Space;
            var isMouse = message.MessageType
                is InputMessageType.MouseMove or InputMessageType.MouseWheel
                or InputMessageType.MouseLeftButtonDown or InputMessageType.MouseLeftButtonUp
                or InputMessageType.MouseRightButtonDown or InputMessageType.MouseRightButtonUp
                or InputMessageType.MouseMiddleButtonDown or InputMessageType.MouseMiddleButtonUp;

            if (isSpaceKey || (isMouse && overlay.WantCaptureMouse))
            {
                continue;
            }

            kept.Add(message);
        }

        foreach (var message in kept)
        {
            window.MessageQueue.Enqueue(message);
        }
    }

    private static uint CurrentLogicFrame(Game game) =>
        unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));
}
