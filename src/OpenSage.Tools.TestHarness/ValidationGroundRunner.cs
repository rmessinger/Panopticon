using System;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Runs a <see cref="ValidationGroundRunPlan"/> to completion: spawns the roster, settles, issues
/// each unit's primary action, polls until every unit resolves (or the frame cap hits), then
/// reports the timing results and, if configured, compares against or refreshes the golden
/// baseline. Pure execution — plan construction happens in <see cref="ScenarioBuilder"/>, status
/// text goes through the injected <see cref="ITestHarnessReporter"/>.
/// </summary>
public static class ValidationGroundRunner
{
    // Run until every unit has resolved (timed out, completed, or gapped) or the cap hits.
    private const uint MaxRunFrames = 5_000;

    public static void Run(GameInstallation installation, TestHarnessConfig config, ValidationGroundRunPlan runPlan, ITestHarnessReporter reporter)
    {
        // The validation ground runs thousands of frames with the full roster fighting; the engine's
        // per-frame Info logs (weapon/animation state transitions) would otherwise produce gigabytes
        // of output and dominate run time. The measurement is logic-frame counts, unaffected by log
        // verbosity, so drop the Info level (Warn/Error still flow).
        foreach (var rule in NLog.LogManager.Configuration.LoggingRules)
        {
            rule.DisableLoggingForLevel(NLog.LogLevel.Info);
        }
        NLog.LogManager.ReconfigExistingLoggers();

        var scenario = runPlan.Scenario;
        var plan = runPlan.SpawnPlan;
        var totalUnits = plan.UnitPlacements.Count;

        Game? game = null;
        GameWindow? window = null;
        TextureCopier? textureCopier = null;
        try
        {
            if (config.Headless)
            {
                game = new Game(installation, config.Renderer, new Configuration { NoAudio = true }, null);
            }
            else
            {
                Platform.Start();
                window = new GameWindow("OpenSAGE - Unit Validation Ground", 100, 100, 1280, 800, config.Fullscreen);
                game = new Game(installation, config.Renderer, new Configuration { NoAudio = true }, window);
                textureCopier = new TextureCopier(game, window.Swapchain.Framebuffer.OutputDescription);
            }

            game.StartSkirmishOrMultiPlayerGame(scenario.MapPath, new Network.EchoConnection(), runPlan.PlayerSettings, scenario.Seed, isMultiPlayer: false);
            game.StartRun();
            CameraZoomInitializer.ApplyOnce(game, config.MaxCameraZoomMultiplier);

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

            var renders = !config.Headless;
            Action step = renders ? () => StepOnce(game, window, textureCopier!) : () => StepLogicOnly(game);

            var host = new ValidationGroundHost();
            host.Spawn(plan, game, scenario.TargetHp);
            host.SpawnKeepAliveStructures(game);

            var timer = new UnitActionTimer();
            foreach (var gap in host.Gaps)
            {
                timer.RecordGap(gap.DefinitionName, gap.FactionIndex, gap.ActionKind, gap.Reason);
            }

            // Settle stage: no orders until this many frames have elapsed. In windowed mode this also
            // renders the scene before any order is issued.
            for (var i = 0; i < scenario.SettleFrames; i++)
            {
                step();
            }

            host.IssueOrders(game, timer);
            var observe = host.BuildObservationSeam(game);

            var startFrame = CurrentLogicFrame(game);
            while (game.IsRunning)
            {
                step();
                var frame = CurrentLogicFrame(game);
                timer.Poll(frame, observe);
                // A unit that destroys its target has finished; clear its weapon so it stops firing
                // (and its turret stops auto-acquiring new enemies), keeping the run bounded.
                host.DisarmUnitsWhoseTargetIsDestroyed(game);

                var report = timer.BuildReport();
                if (report.TotalUnitCount >= totalUnits || frame - startFrame > MaxRunFrames)
                {
                    break;
                }
            }

            Finish(timer, config, reporter);
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
    private static void StepLogicOnly(Game game)
    {
        game.Update(Array.Empty<Input.InputMessage>());
    }

    private static void StepOnce(Game game, GameWindow? window, TextureCopier textureCopier)
    {
        if (window != null)
        {
            if (!window.PumpEvents())
            {
                game.Exit();
                return;
            }

            game.Update(window.MessageQueue);
            game.Panel.EnsureFrame(window.ClientBounds);
            game.Render();
            textureCopier.Execute(
                game.Panel.Framebuffer.ColorTargets[0].Target,
                window.Swapchain.Framebuffer);
            game.GraphicsDevice.SwapBuffers(window.Swapchain);
            window.MessageQueue.Clear();
        }
        else
        {
            game.Update(Array.Empty<Input.InputMessage>());
            game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, 1280, 800));
            game.Render();
        }
    }

    private static uint CurrentLogicFrame(Game game) =>
        unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));
}
