using System;
using System.Collections.Generic;
using System.IO;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.IO;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Runs a <see cref="ReplayRunPlan"/> to completion, windowed or headless, wiring up engine action
/// events, deferred-order injection, and per-frame result recording. Pure execution — scenario
/// construction happens in <see cref="ScenarioBuilder"/>, status text goes through the injected
/// <see cref="ITestHarnessReporter"/>.
/// </summary>
public static class ReplayScenarioRunner
{
    public static void Run(GameInstallation installation, TestHarnessConfig config, ReplayRunPlan plan, ITestHarnessReporter reporter)
    {
        using var results = new ActionResultsWriter(plan.ResultsPath, config.Scenario.ToString(), config.ScenarioPath ?? "(built-in)", config.Game.ToString());

        if (config.Headless)
        {
            RunHeadless(installation, config, plan.ReplayPath, plan.DeferredActions, results, reporter);
        }
        else
        {
            Platform.Start();
            try
            {
                RunWindowed(installation, config, plan.ReplayPath, plan.DeferredActions, results, reporter);
            }
            finally
            {
                Platform.Stop();
            }
        }
    }

    private static void RunWindowed(GameInstallation installation, TestHarnessConfig config, string replayPath,
        IReadOnlyList<DeferredAction> deferredActions, ActionResultsWriter results, ITestHarnessReporter reporter)
    {
        using var window = new GameWindow("OpenSAGE - TestHarness", 100, 100, 1024, 768, config.Fullscreen);
        using var game = new Game(installation, config.Renderer, new Configuration { NoAudio = true, CameraControlScheme = config.CameraControlScheme, MouseClickMode = config.MouseClickMode }, window);
        using var textureCopier = new TextureCopier(game, window.Swapchain.Framebuffer.OutputDescription);

        using var repFileSystem = new DiskFileSystem(Path.GetDirectoryName(replayPath)!);
        var repEntry = repFileSystem.GetFile(Path.GetFileName(replayPath));

        game.LoadReplayFile(repEntry);
        game.StartRun();
        CameraZoomInitializer.ApplyOnce(game, config.MaxCameraZoomMultiplier);

        using var monitor = new ReplayActionMonitor(game);
        monitor.ActionDispatched += (_, e) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            reporter.ActionDispatched(e.ActionType, e.Player?.Name, frame);
        };
        monitor.ActionCompleted += (_, e) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            reporter.ActionCompleted(e.ActionType, e.Player?.Name, frame);
        };
        using var recorder = new EngineActionRecorder(game, results);
        var pendingInjections = BuildDeferredInjector(game, monitor, deferredActions);

        reporter.Info("Running replay — close the window or wait for it to finish.");

        while (game.IsRunning)
        {
            if (!window.PumpEvents())
            {
                break;
            }

            DrainPendingInjections(game, pendingInjections);
            game.Update(window.MessageQueue);
            game.Panel.EnsureFrame(window.ClientBounds);
            game.Render();
            textureCopier.Execute(
                game.Panel.Framebuffer.ColorTargets[0].Target,
                window.Swapchain.Framebuffer);
            window.MessageQueue.Clear();
            game.GraphicsDevice.SwapBuffers(window.Swapchain);

            recorder.PollPendingMoves();
            recorder.PollHealthChanges();
            recorder.PollSupplyCycles();
            results.MaybeFlush();
        }

        reporter.Info("Done.");
    }

    private static void RunHeadless(GameInstallation installation, TestHarnessConfig config, string replayPath,
        IReadOnlyList<DeferredAction> deferredActions, ActionResultsWriter results, ITestHarnessReporter reporter)
    {
        using var game = new Game(installation, config.Renderer, new Configuration { NoAudio = true, CameraControlScheme = config.CameraControlScheme, MouseClickMode = config.MouseClickMode }, null);

        using var repFileSystem = new DiskFileSystem(Path.GetDirectoryName(replayPath)!);
        var repEntry = repFileSystem.GetFile(Path.GetFileName(replayPath));

        game.LoadReplayFile(repEntry);
        game.StartRun();
        CameraZoomInitializer.ApplyOnce(game, config.MaxCameraZoomMultiplier);

        using var capture = new FrameCaptureService();

        // Once the match reaches a decided outcome, run a short tail period then exit.
        const uint RunoutLogicFrames = 300; // 10 game-seconds at 30 fps
        var runoutEndLogicFrame = uint.MaxValue;

        using var monitor = new ReplayActionMonitor(game);
        monitor.ActionDispatched += (_, e) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            reporter.ActionDispatched(e.ActionType, e.Player?.Name, frame);
        };
        monitor.ActionCompleted += (_, e) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            reporter.ActionCompleted(e.ActionType, e.Player?.Name, frame);

            if (config.CaptureFrames)
            {
                // Schedule a capture after every completed action so agents can verify
                // the visual state immediately after each game event.
                // The [CAPTURE] log line is emitted after the next render (FlushPending below).
                capture.ScheduleCapture(
                    $"Action '{e.ActionType}' completed for player '{e.Player?.Name ?? "unknown"}'. " +
                    "Terrain should be visible. Units and buildings should be 3-D models (not invisible). " +
                    "No solid-colour triangle artefacts. UI chrome should be present at the bottom.");
            }
        };

        game.MatchOutcomeDecided += (_, _) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            var outcome = game.CurrentOutcome;
            if (runoutEndLogicFrame == uint.MaxValue)
            {
                runoutEndLogicFrame = frame + RunoutLogicFrames;
            }
            reporter.MatchOutcomeDecided(outcome.Status, outcome.Reason, outcome.WinningPlayer?.Name, frame);
        };

        var pendingInjections = BuildDeferredInjector(game, monitor, deferredActions);
        using var recorder = new EngineActionRecorder(game, results);

        reporter.Info("Running headless replay.");
        if (config.CaptureFrames)
        {
            reporter.Info($"Captures written to: {FrameCaptureService.CaptureDirectory}");
            reporter.Info("Agent: monitor stdout for [CAPTURE] lines and inspect during the run; files are deleted on exit.");
        }

        var frameRect = new Mathematics.Rectangle(0, 0, 1024, 768);

        while (game.IsRunning)
        {
            DrainPendingInjections(game, pendingInjections);
            game.Update(Array.Empty<Input.InputMessage>());
            game.Panel.EnsureFrame(frameRect);
            game.Render();
            if (config.CaptureFrames)
            {
                capture.FlushPending(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
            }

            recorder.PollPendingMoves();
            recorder.PollHealthChanges();
            recorder.PollSupplyCycles();
            results.MaybeFlush();

            if (runoutEndLogicFrame != uint.MaxValue)
            {
                var logicFrame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
                if (logicFrame >= runoutEndLogicFrame)
                {
                    game.Exit();
                }
            }
        }

        reporter.Info("Done.");
    }

    /// <summary>
    /// Subscribes to <see cref="ReplayActionMonitor.ActionDispatched"/> and
    /// <see cref="ReplayActionMonitor.ActionCompleted"/> events and, for each
    /// <see cref="DeferredAction"/> whose trigger and <see cref="DeferredAction.Phase"/> match,
    /// enqueues its orders for injection on the next game-loop iteration (via
    /// <see cref="DrainPendingInjections"/>).
    /// </summary>
    private static List<(int RemainingFrames, IReadOnlyList<Logic.Orders.Order> Orders)> BuildDeferredInjector(
        Game game,
        ReplayActionMonitor monitor,
        IReadOnlyList<DeferredAction> deferredActions)
    {
        var pending = new List<(int RemainingFrames, IReadOnlyList<Logic.Orders.Order> Orders)>();

        monitor.ActionDispatched += (_, e) =>
        {
            foreach (var deferred in deferredActions)
            {
                if (deferred.Phase == ActionPhase.Dispatched
                    && deferred.Trigger == e.ActionType
                    && deferred.PlayerIndex == (int?)e.Player?.Id
                    && (deferred.TriggerDefinitionName == null || deferred.TriggerDefinitionName == e.DefinitionName))
                {
                    pending.Add(((int)deferred.ExtraFrames, deferred.Orders));
                }
            }
        };

        monitor.ActionCompleted += (_, e) =>
        {
            foreach (var deferred in deferredActions)
            {
                if (deferred.Phase == ActionPhase.Completed
                    && deferred.Trigger == e.ActionType
                    && deferred.PlayerIndex == (int?)e.Player?.Id
                    && (deferred.TriggerDefinitionName == null || deferred.TriggerDefinitionName == e.DefinitionName))
                {
                    pending.Add(((int)deferred.ExtraFrames, deferred.Orders));
                }
            }
        };

        return pending;
    }

    /// <summary>
    /// Injects any pending deferred orders whose frame delay has elapsed.
    /// Call once per game-loop iteration, BEFORE <c>game.Update()</c>.
    /// </summary>
    private static void DrainPendingInjections(
        Game game,
        List<(int RemainingFrames, IReadOnlyList<Logic.Orders.Order> Orders)> pending)
    {
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var (remaining, orders) = pending[i];
            if (remaining <= 0)
            {
                game.InjectOrders(orders);
                pending.RemoveAt(i);
            }
            else
            {
                pending[i] = (remaining - 1, orders);
            }
        }
    }

}
