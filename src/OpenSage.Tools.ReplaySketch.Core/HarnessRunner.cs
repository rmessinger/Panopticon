using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.IO;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

public static class HarnessRunner
{
    private const uint DefaultFrameTimeout = 18_000;

    // ──────────────────────────────────────────────────────────────────────────
    // Schedule entry: one row per non-AfterAction action across all players.
    // DirectDispatch is non-null only for handle-dependent open commands that
    // cannot be exported to the .rep file.
    // ──────────────────────────────────────────────────────────────────────────
    private record struct ScheduledAction(
        uint Frame,
        int PlayerIndex,
        string CommandId,
        ActionEntry? DirectDispatch,
        PositionContext Ctx);

    public static AssertionReport RunAndAssert(
        ReplayScenario scenario,
        GameInstallation installation,
        string tempDirectory,
        Action<RunOutput>? onRunCompleted = null)
    {
        ReplayActionMetrics.Reset();

        Directory.CreateDirectory(tempDirectory);
        var replayPath = Path.Combine(tempDirectory, "sketch_run.rep");

        var map = MapMetadataService.Load(installation, scenario.MapPath);

        // Build a concrete scenario where handle-dependent actions are replaced with no-ops
        // so that ReplayExporter does not encounter unresolvable arguments at export time.
        var concreteScenario = BuildConcreteScenario(scenario);

        // Pre-compute the per-frame tracking/dispatch schedule for ALL non-AfterAction actions.
        // Note: RandomTiming frame alignment is best-effort when the schedule's seeded Random(0)
        // diverges from ReplayExporter's unseeded Random. FixedTiming is always exact.
        var schedule = BuildActionSchedule(scenario, map);

        var exportError = ReplayExporter.Export(concreteScenario, map, replayPath, out var deferredActions);
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
            var replayFile = OpenSage.Data.Rep.ReplayFile.FromFileSystemEntry(replayEntry);

            game.LoadReplayFile(replayEntry);
            game.StartRun();

            var evaluator = new AssertionEvaluator(scenario.Assertions, game);
            evaluator.Subscribe();

            using var monitor = new ReplayActionMonitor(game);
            var pendingInjections = BuildDeferredInjector(monitor, deferredActions);

            var resolver = new ObjectReferenceResolver(game);
            // Seed the dispatch RNG from the replay's recorded run seed so harness runs are
            // reproducible (A2, WI-A2-03); the engine's GameLogic.Random is seeded from the
            // same value when the replay is loaded.
            var dispatchRng = new Random(replayFile.Header.Metadata.SD);

            // A1-4 SimulationHost stepping (WI-A1-08): fixed-timestep, faster-than-real-time.
            // Frame alignment: TickStarting fires before step k (k = e.Frame), when the world
            // has completed k-1 frames — matching the pre-Update ComputeLogicFrame of the
            // previous loop.
            var host = new SimulationHost(game.GameEngine);

            // A2 (WI-A2-04): optional T0 run-output capture for statistical aggregation. T0
            // metrics only; the tiered T1/T2 recording stays out of scope here.
            var runOutputRecorder = onRunCompleted is not null
                ? new RunOutputRecorder(host, game, (uint)replayFile.Header.Metadata.SD, scenario.MapPath)
                : null;
            var openCommands = 0;
            var timedOut = false;

            host.TickStarting += (_, e) =>
            {
                var logicFrame = e.Frame - 1;

                // For every action whose scheduled frame matches the current frame:
                //   - Call BeginCommand to start tracking any objects this action creates.
                //   - For handle-dependent actions: also resolve args and inject directly,
                //     since they were replaced with no-ops in the exported .rep.
                var frameActions = schedule.Where(s => s.Frame == logicFrame).ToList();
                foreach (var item in frameActions)
                {
                    resolver.BeginCommand(item.CommandId);
                    openCommands++;
                    if (item.DirectDispatch is { } action)
                    {
                        var resolved = action.Command.Resolve(resolver);
                        var order = resolved.ToOrder(item.PlayerIndex, item.Ctx, dispatchRng);
                        game.InjectOrders(new[] { order });
                    }
                }

                DrainPendingInjections(game, pendingInjections);
            };

            host.TickComplete += (_, e) =>
            {
                // End command tracking after the update so any ObjectCreated events fired
                // during the update are correctly attributed to their command.
                for (var i = 0; i < openCommands; i++)
                {
                    resolver.EndCommand();
                }
                openCommands = 0;

                if (e.Frame - 1 >= DefaultFrameTimeout)
                {
                    timedOut = true;
                }
            };

            try
            {
                host.Run(
                    (int)DefaultFrameTimeout + 1,
                    h => !game.IsRunning || evaluator.IsAborted || timedOut);
            }
            finally
            {
                resolver.Dispose();
                if (runOutputRecorder is not null)
                {
                    onRunCompleted!(runOutputRecorder.Complete());
                    runOutputRecorder.Dispose();
                }
            }

            return evaluator.Evaluate();
        }
        finally
        {
            Platform.Stop();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Handle-dependency helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static bool ContainsUnresolvableArgs(ActionEntry action) =>
        action.Command.Arguments.Any(a => a is HandleCommandArgument or QuerySelectorCommandArgument);

    /// <summary>
    /// Returns a copy of <paramref name="scenario"/> in which every handle-dependent action
    /// is replaced with an <see cref="ActionType.EnemyBaseDestroyed"/> no-op carrying the same
    /// timing and id. This preserves cumulative frame alignment for subsequent concrete actions.
    /// </summary>
    private static ReplayScenario BuildConcreteScenario(ReplayScenario scenario)
    {
        var concrete = new ReplayScenario
        {
            MapPath = scenario.MapPath,
            BaseRadiusWorldUnits = scenario.BaseRadiusWorldUnits,
            Assertions = scenario.Assertions,
        };

        foreach (var player in scenario.Players)
        {
            var slot = new PlayerSlotConfig
            {
                Name = player.Name,
                FactionIndex = player.FactionIndex,
                Color = player.Color,
                StartPosition = player.StartPosition,
                Team = player.Team,
            };

            foreach (var action in player.Actions)
            {
                if (ContainsUnresolvableArgs(action))
                {
                    // Replace with a sentinel that advances the frame counter but emits no order.
                    slot.Actions.Add(new ActionEntry(
                        label: $"[no-op:{action.Label}]",
                        type: ActionType.EnemyBaseDestroyed,
                        position: null,
                        timing: action.Timing,
                        id: action.Id));
                }
                else
                {
                    slot.Actions.Add(action);
                }
            }

            concrete.Players.Add(slot);
        }

        return concrete;
    }

    /// <summary>
    /// Pre-computes the per-frame dispatch schedule for every non-<see cref="AfterActionTiming"/>
    /// action. AfterActionTiming actions are handled by the existing deferred-injection mechanism
    /// and are excluded here.
    /// </summary>
    private static List<ScheduledAction> BuildActionSchedule(
        ReplayScenario scenario, MapMetadataService map)
    {
        var schedule = new List<ScheduledAction>();
        var rng = new Random(0);

        for (var ownerIdx = 0; ownerIdx < scenario.Players.Count; ownerIdx++)
        {
            var player = scenario.Players[ownerIdx];
            var enemyIdx = ownerIdx == 0 ? 1 : 0;
            var ctx = TerrainValidator.BuildContextPublic(scenario, map, ownerIdx, enemyIdx);
            var playerIndex = ownerIdx + 2;

            uint cumulativeFrame = 0;
            foreach (var action in player.Actions)
            {
                if (action.Timing is AfterActionTiming)
                {
                    continue;
                }

                cumulativeFrame += action.Timing.Resolve(rng);

                schedule.Add(new ScheduledAction(
                    Frame: cumulativeFrame,
                    PlayerIndex: playerIndex,
                    CommandId: action.Id,
                    DirectDispatch: ContainsUnresolvableArgs(action) ? action : null,
                    Ctx: ctx));
            }
        }

        return schedule;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Deferred injection (AfterActionTiming, existing mechanism)
    // ──────────────────────────────────────────────────────────────────────────

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
                    && deferred.PlayerIndex == (int?)e.Player?.Id
                    && MatchesDefinition(deferred, e))
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
                    && MatchesDefinition(deferred, e))
                {
                    pending.Add(((int)deferred.ExtraFrames, deferred.Orders));
                }
            }
        };

        return pending;
    }

    /// <summary>
    /// A deferred action with no <see cref="DeferredAction.TriggerDefinitionName"/> matches any
    /// event of the right type/phase/player (preserves prior behaviour for trigger types that
    /// don't target a specific definition, e.g. GatherResources/AttackEnemyBase/EnemyBaseDestroyed).
    /// Otherwise the runtime event's definition name must match exactly, so that scenarios with
    /// several actions sharing an ActionType (e.g. one Construct per building) only trigger their
    /// own children instead of every sibling's.
    /// </summary>
    private static bool MatchesDefinition(DeferredAction deferred, ReplayActionEventArgs e) =>
        deferred.TriggerDefinitionName is null || deferred.TriggerDefinitionName == e.DefinitionName;

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

    // ──────────────────────────────────────────────────────────────────────────
    // ObjectReferenceResolver — wraps HandleTracker + Game.ObjectCreated event
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class ObjectReferenceResolver : IObjectReferenceResolver, IDisposable
    {
        private readonly Game _game;
        private readonly HandleTracker _tracker = new();

        public ObjectReferenceResolver(Game game)
        {
            _game = game;
            _game.ObjectCreated += OnObjectCreated;
        }

        public void BeginCommand(string commandId) => _tracker.BeginCommand(commandId);

        public void EndCommand() => _tracker.EndCommand();

        private void OnObjectCreated(object? sender, GameObjectCreatedEventArgs e) =>
            _tracker.OnObjectCreated(e.GameObject.Id);

        public ObjectId ResolveHandle(string commandId) =>
            _tracker.ResolveHandle(commandId);

        public ObjectId ResolveSelector(QuerySelectorCommandArgument selector) =>
            _game.FindFirstObjectByDefinitionName(selector.DefinitionName ?? string.Empty);

        public void Dispose() => _game.ObjectCreated -= OnObjectCreated;
    }
}
