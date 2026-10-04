#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Simulation;

namespace Panopticon;

/// <summary>
/// Result of a <see cref="SimulationHost.Run"/>: how many frame steps completed and which logic
/// frame the world reached.
/// </summary>
public readonly record struct SimulationHostRunResult(
    int FramesCompleted,
    LogicFrame FinalLogicFrame,
    bool StopPredicateTriggered);

/// <summary>
/// Per-tick observation payload raised by <see cref="SimulationHost.TickComplete"/> after each
/// frame step.
/// </summary>
public sealed class SimulationHostTickEventArgs : EventArgs
{
    public uint Frame { get; }
    public LogicFrame LogicFrame { get; }
    public TimeSpan MapTime { get; }

    public SimulationHostTickEventArgs(uint frame, LogicFrame logicFrame, TimeSpan mapTime)
    {
        Frame = frame;
        LogicFrame = logicFrame;
        MapTime = mapTime;
    }
}

/// <summary>
/// Domain-agnostic headless host (A1-4). Owns the logic-side world through the
/// <see cref="IGameEngine"/> seam and advances it with a <see cref="LogicStepper"/> as fast as
/// the hardware allows: no wall-clock sleep, no <c>Render</c> call, no window, audio, or input.
/// Orders are injected through the same <c>IGame.InjectOrders</c> pipeline the windowed harness
/// uses; there is no second order pipeline.
/// </summary>
public sealed class SimulationHost
{
    private readonly IGame _game;
    private readonly IGameEngine _gameEngine;
    private readonly LogicStepper _stepper;

    // Orders scheduled for a future logic frame, flushed when the host reaches that frame,
    // before stepping it (same dispatch point as the ReplaySketch harness runner).
    private readonly SortedDictionary<uint, List<Order>> _pendingOrders = new();

    private readonly TimeSpan _frameInterval;

    private uint _frame;
    private TimeSpan _mapTime;

    /// <summary>
    /// Raised before each frame step: before due orders dispatch and before the logic tick.
    /// The frame the step is about to produce is <see cref="SimulationHostTickEventArgs.Frame"/>;
    /// <see cref="SimulationHostTickEventArgs.LogicFrame"/> is the world's current (pre-step)
    /// frame. Callers inject frame-scheduled work here.
    /// </summary>
    public event EventHandler<SimulationHostTickEventArgs>? TickStarting;

    /// <summary>
    /// Raised after each completed frame step.
    /// </summary>
    public event EventHandler<SimulationHostTickEventArgs>? TickComplete;

    /// <summary>
    /// Raised per due frame with the orders actually dispatched into the game (WI-A1-21: the
    /// operational stream's Inputs records mirror real dispatch, not injection requests).
    /// </summary>
    internal event Action<uint, IReadOnlyList<Order>>? OrdersDispatched;

    /// <summary>
    /// Current frame step count since the run started.
    /// </summary>
    public uint Frame => _frame;

    /// <summary>
    /// The logic-side frame counter of the hosted world.
    /// </summary>
    public LogicFrame LogicFrame => _gameEngine.GameLogic.CurrentFrame;

    /// <summary>
    /// The injected domain model (A4-1): the projection surface for recording, topology keys,
    /// metrics, and interaction authority. The host never constructs a concrete adapter;
    /// callers pass one (RTS callers pass the reference adapter from Panopticon.Simulation).
    /// </summary>
    public ISimulationDomainModel DomainModel { get; }

    internal IGame Game => _game;

    // Checkpoint capture seam (WI-A1-15): the host frame counter, map time, and the pending
    // order schedule are private, so the checkpoint writer reads them through this snapshot.
    internal (uint Frame, TimeSpan MapTime, SortedDictionary<uint, List<Order>> PendingOrders) CaptureHostState()
    {
        return (_frame, _mapTime, _pendingOrders);
    }

    // Checkpoint restore seam (WI-A1-16): rewinds the host counters and re-seeds the pending
    // order schedule from a decoded host section.
    internal void RestoreHostState(uint frame, TimeSpan mapTime, IEnumerable<KeyValuePair<uint, List<Order>>> pendingOrders)
    {
        _frame = frame;
        _mapTime = mapTime;
        _pendingOrders.Clear();
        foreach (var (scheduledFrame, orders) in pendingOrders)
        {
            _pendingOrders[scheduledFrame] = new List<Order>(orders);
        }
    }

    public SimulationHost(IGameEngine gameEngine, ISimulationDomainModel domainModel)
    {
        _gameEngine = gameEngine;
        _game = gameEngine.Game;
        _stepper = new LogicStepper(_game);
        _frameInterval = TimeSpan.FromMilliseconds(gameEngine.MsPerLogicFrame);
        DomainModel = domainModel;
        DomainModel.Initialize(this);
    }

    /// <summary>
    /// Schedules <paramref name="orders"/> for dispatch when the run reaches
    /// <paramref name="frame"/>, before that frame is stepped. Orders scheduled for a frame the
    /// run has already passed are dispatched on the next step.
    /// </summary>
    public void InjectOrders(uint frame, IEnumerable<Order> orders)
    {
        if (!_pendingOrders.TryGetValue(frame, out var list))
        {
            list = new List<Order>();
            _pendingOrders[frame] = list;
        }

        list.AddRange(orders);
    }

    /// <summary>
    /// Runs up to <paramref name="frameBudget"/> frame steps in a tight loop, stopping early when
    /// <paramref name="stopPredicate"/> returns true (checked after each completed step). Each
    /// step advances map time by exactly one logic frame interval and performs one logic step.
    /// </summary>
    public SimulationHostRunResult Run(
        int frameBudget,
        Func<SimulationHost, bool>? stopPredicate = null)
    {
        var startLogicFrame = LogicFrame;
        var framesCompleted = 0;
        var stopPredicateTriggered = false;

        DomainModel.RunStarting();

        for (var i = 0; i < frameBudget; i++)
        {
            StepOnce();
            framesCompleted++;

            if (stopPredicate is not null && stopPredicate(this))
            {
                stopPredicateTriggered = true;
                break;
            }
        }

        var result = new SimulationHostRunResult(
            framesCompleted,
            LogicFrame,
            stopPredicateTriggered);
        DomainModel.RunComplete(in result);
        return result;
    }

    private void StepOnce()
    {
        TickStarting?.Invoke(this, new SimulationHostTickEventArgs(_frame + 1, LogicFrame, _mapTime + _frameInterval));
        DispatchDueOrders();
        _mapTime += _frameInterval;
        _stepper.AdvanceLogic(_mapTime);
        _frame++;
        TickComplete?.Invoke(this, new SimulationHostTickEventArgs(_frame, LogicFrame, _mapTime));
    }

    private void DispatchDueOrders()
    {
        if (_pendingOrders.Count == 0)
        {
            return;
        }

        // Dispatch everything scheduled for the frame about to be stepped. Later frames stay
        // pending; earlier frames are treated as immediately due.
        var currentFrame = LogicFrame.Value;
        var dueFrames = _pendingOrders.Keys.Where(f => f <= currentFrame + 1).ToList();
        foreach (var dueFrame in dueFrames)
        {
            var due = _pendingOrders[dueFrame];
            foreach (var order in due)
            {
                _game.InjectOrders(new[] { order });
            }

            OrdersDispatched?.Invoke(dueFrame, due);
            _pendingOrders.Remove(dueFrame);
        }
    }
}
