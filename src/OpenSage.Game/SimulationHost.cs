#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;

namespace OpenSage;

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
    /// Raised after each completed frame step. The recording formats built on this hook are the
    /// A1-5 run outputs; do not add recording state to the host itself.
    /// </summary>
    public event EventHandler<SimulationHostTickEventArgs>? TickComplete;

    /// <summary>
    /// Current frame step count since the run started.
    /// </summary>
    public uint Frame => _frame;

    /// <summary>
    /// The logic-side frame counter of the hosted world.
    /// </summary>
    public LogicFrame LogicFrame => _gameEngine.GameLogic.CurrentFrame;

    // The world is owned through IGameEngine, which is RTS-shaped today. The pluggable domain
    // model (entity schema, topology, interaction rules) is the A4 Simulation Domain Model seam;
    // when it lands, this constructor takes the domain adapter as a parameter.
    public SimulationHost(IGameEngine gameEngine)
    {
        _gameEngine = gameEngine;
        _game = gameEngine.Game;
        _stepper = new LogicStepper(_game);
        _frameInterval = TimeSpan.FromMilliseconds(gameEngine.MsPerLogicFrame);
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

        for (var i = 0; i < frameBudget; i++)
        {
            DispatchDueOrders();
            StepOnce();
            framesCompleted++;

            if (stopPredicate is not null && stopPredicate(this))
            {
                stopPredicateTriggered = true;
                break;
            }
        }

        return new SimulationHostRunResult(
            framesCompleted,
            LogicFrame,
            stopPredicateTriggered);
    }

    private void StepOnce()
    {
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
            foreach (var order in _pendingOrders[dueFrame])
            {
                _game.InjectOrders(new[] { order });
            }
            _pendingOrders.Remove(dueFrame);
        }
    }
}
