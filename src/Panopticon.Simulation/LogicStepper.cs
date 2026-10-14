using System;
using System.Diagnostics;
using Panopticon.Mathematics;

namespace Panopticon;

/// <summary>
/// The fixed-rate logic step extracted from <see cref="Game.Update"/> (A1-3). Owns the
/// logic-timing state identified by the tick-path audit; performs exactly the work of the old
/// <c>LogicTick</c> plus the scripting-tick branch of <c>Update</c>, in the same order. Runs with
/// no presentation systems: no Scene2D, audio, cursors, input pumping, or render timer.
/// </summary>
public sealed class LogicStepper
{
    private readonly IGame _game;
    private readonly double _scriptingUpdateInterval;

    private TimeSpan _nextLogicUpdate;
    private TimeSpan _nextScriptingUpdate;

    // Are we in the middle of stepping until the next logic frame?
    private bool _isStepping;

    public TimeSpan CumulativeLogicUpdateError { get; private set; }

    /// <summary>
    /// The map time at which the next logic update will fire. Read by the windowed
    /// <c>Game.LocalLogicTick</c> interpolation fraction; not needed headless.
    /// </summary>
    internal TimeSpan NextLogicUpdate => _nextLogicUpdate;

    /// <summary>
    /// Raised after each subsystem step inside a logic tick with the system name and its
    /// wall-clock span (B1-3 per-system timing). The un-instrumented path stays branch-free:
    /// when no handler is attached, <see cref="LogicTick"/> runs the original sequence.
    /// </summary>
    public event Action<string, TimeSpan>? SystemSpan;

    /// <summary>
    /// Cancels a pending single step, matching the previous behavior of the
    /// <c>Game.IsLogicRunning</c> setter.
    /// </summary>
    internal void CancelStepping() => _isStepping = false;

    /// <summary>
    /// This is how fast we actually tick through updates.
    /// </summary>
    private float LogicUpdateInterval => _game.GameEngine.MsPerLogicFrame / _game.LogicUpdateScaleFactor;

    internal LogicStepper(IGame game)
    {
        _game = game;
        _scriptingUpdateInterval = 1000.0 / game.Definition.ScriptingTicksPerSecond;
    }

    /// <summary>
    /// Resets the stepping state for a new game, matching the reset previously performed at the
    /// end of <c>Game.StartGame</c>.
    /// </summary>
    internal void Reset()
    {
        _nextLogicUpdate = TimeSpan.Zero;
        _nextScriptingUpdate = TimeSpan.Zero;
        CumulativeLogicUpdateError = TimeSpan.Zero;
        _isStepping = false;
    }

    /// <summary>
    /// Schedules the next logic and scripting updates from the current map time, as
    /// <see cref="IGame.StartRun"/> does.
    /// </summary>
    internal void StartRun(TimeSpan totalMapTime)
    {
        _nextLogicUpdate = totalMapTime;
        _nextScriptingUpdate = totalMapTime;
    }

    /// <summary>
    /// Advances the logic and scripting steps for the given map time. Callers advance the map
    /// clock first (windowed: <c>Game.LocalLogicTick</c>; headless: a fixed delta per step).
    /// </summary>
    public void AdvanceLogic(TimeSpan totalGameTime)
    {
        // If the game is not paused and it's time to do a logic update, do so.
        if (_game.IsLogicRunning && totalGameTime >= _nextLogicUpdate)
        {
            LogicTick(totalGameTime);
            CumulativeLogicUpdateError += (totalGameTime - _nextLogicUpdate);
            // Logic updates happen at 5Hz.
            _nextLogicUpdate += TimeSpan.FromMilliseconds(LogicUpdateInterval);

            if (_isStepping)
            {
                _game.IsLogicRunning = false;
            }
        }

        // TODO: Which update should be performed first?
        if (_game.IsLogicRunning && totalGameTime >= _nextScriptingUpdate)
        {
            _game.Scripting?.ScriptingTick();
            // Scripting updates happen at 30Hz / 5Hz depending on game.
            _nextScriptingUpdate += TimeSpan.FromMilliseconds(_scriptingUpdateInterval);
        }
    }

    private void LogicTick(TimeSpan totalMapTime)
    {
        // The measured path is spelled out separately from the default path so the default
        // stays allocation-free and branch-free; timestamps use Stopwatch pairs only, since
        // closure captures here would pollute the allocation deltas B1-3 measures.

        // WI-B4-05: apply async path completions before game logic so consumers observe fresh
        // results; a no-op unless requests were submitted.
        _game.GameEngine.Navigation?.DrainPathCompletions();

        if (SystemSpan is { } spanSink)
        {
            var interval = new TimeInterval(
                totalMapTime,
                TimeSpan.FromMilliseconds(_game.GameEngine.MsPerLogicFrame));

            var start = Stopwatch.GetTimestamp();
            _game.GameLogic.Update();
            spanSink("gameLogic", Stopwatch.GetElapsedTime(start));

            start = Stopwatch.GetTimestamp();
            _game.NetworkMessageBuffer?.Tick();
            spanSink("network", Stopwatch.GetElapsedTime(start));

            start = Stopwatch.GetTimestamp();
            _game.Scene3D?.LogicTick(interval);
            spanSink("scene3d", Stopwatch.GetElapsedTime(start));

            start = Stopwatch.GetTimestamp();
            _game.PartitionCellManager?.Update();
            spanSink("partition", Stopwatch.GetElapsedTime(start));

            return;
        }

        _game.GameLogic.Update();

        _game.NetworkMessageBuffer?.Tick();

        // TODO: What is the order?
        // TODO: Calculate time correctly.
        var timeInterval = new TimeInterval(totalMapTime, TimeSpan.FromMilliseconds(_game.GameEngine.MsPerLogicFrame));
        _game.Scene3D?.LogicTick(timeInterval);

        _game.PartitionCellManager?.Update();
    }

    /// <summary>
    /// Enables single-step mode: the next <see cref="AdvanceLogic"/> that fires a logic tick
    /// pauses logic again afterwards.
    /// </summary>
    internal void StepOnce()
    {
        _game.IsLogicRunning = true;
        _isStepping = true;
    }
}
