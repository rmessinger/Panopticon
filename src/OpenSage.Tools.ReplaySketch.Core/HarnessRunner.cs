using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.IO;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

public static class HarnessRunner
{
    private const uint DefaultFrameTimeout = 18_000;

    public static AssertionReport RunAndAssert(
        ReplayScenario scenario,
        GameInstallation installation,
        string tempDirectory)
    {
        Directory.CreateDirectory(tempDirectory);
        var replayPath = Path.Combine(tempDirectory, "sketch_run.rep");

        var map = MapMetadataService.Load(installation, scenario.MapPath);
        var exportError = ReplayExporter.Export(scenario, map, replayPath, out var deferredActions);
        if (exportError is not null)
        {
            throw new InvalidOperationException($"Replay export failed: {exportError}");
        }

        Platform.Start();
        try
        {
            using var game = new Game(
                installation,
                preferredBackend: null,
                new Configuration { NoAudio = true },
                window: null);
            using var replayFileSystem = new DiskFileSystem(Path.GetDirectoryName(replayPath)!);
            var replayEntry = replayFileSystem.GetFile(Path.GetFileName(replayPath));

            game.LoadReplayFile(replayEntry);
            game.StartRun();

            var evaluator = new AssertionEvaluator(scenario.Assertions, game);
            evaluator.Subscribe();

            using var monitor = new ReplayActionMonitor(game);
            var pendingInjections = BuildDeferredInjector(monitor, deferredActions);

            while (game.IsRunning)
            {
                DrainPendingInjections(game, pendingInjections);
                game.Update(Array.Empty<OpenSage.Input.InputMessage>());

                var logicFrame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
                if (logicFrame >= DefaultFrameTimeout)
                {
                    break;
                }
            }

            return evaluator.Evaluate();
        }
        finally
        {
            Platform.Stop();
        }
    }

    private static List<(int RemainingFrames, IReadOnlyList<Order> Orders)> BuildDeferredInjector(
        ReplayActionMonitor monitor,
        IReadOnlyList<DeferredAction> deferredActions)
    {
        var pending = new List<(int RemainingFrames, IReadOnlyList<Order> Orders)>();

        monitor.ActionDispatched += (_, e) =>
        {
            foreach (var deferred in deferredActions)
            {
                if (deferred.Phase == ActionPhase.Dispatched
                    && deferred.Trigger == e.ActionType
                    && deferred.PlayerIndex == (int?)e.Player?.Id)
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
                    && deferred.PlayerIndex == (int?)e.Player?.Id)
                {
                    pending.Add(((int)deferred.ExtraFrames, deferred.Orders));
                }
            }
        };

        return pending;
    }

    private static void DrainPendingInjections(
        Game game,
        List<(int RemainingFrames, IReadOnlyList<Order> Orders)> pending)
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
