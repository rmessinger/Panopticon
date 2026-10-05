using System;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// One booted benchmark run: the headless host plus the game it borrows. Disposing the run
/// disposes the game the scenario created.
/// </summary>
public sealed class SimulationBenchmarkRun : IDisposable
{
    public SimulationBenchmarkRun(SimulationHost host, Game game)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(game);

        Host = host;
        Game = game;
    }

    public SimulationHost Host { get; }

    public Game Game { get; }

    public void Dispose()
    {
        Game.Dispose();
    }
}
