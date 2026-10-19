#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Samples the world state once per tick and computes the frame delta, feeding every A1-5 tier
/// from one pass: snapshot recording (WI-A1-11), T1 signature trees (WI-A1-09), and T2
/// triggered detail (WI-A1-10). Subscribers read the per-frame sample through
/// <see cref="FrameSampled"/>; the sampler keeps only consecutive summaries.
/// </summary>
public sealed class WorldStateSampler : IDisposable
{
    private readonly SimulationHost _host;
    private readonly Func<uint, bool>? _sampleFilter;
    private WorldStateSummary? _previous;

    /// <summary>Raised after each stepped frame with that frame's summary and delta.</summary>
    public event Action<WorldStateSummary, WorldStateDelta>? FrameSampled;

    public WorldStateSampler(SimulationHost host, IGame game, Func<uint, bool>? sampleFilter = null)
    {
        _host = host;
        _sampleFilter = sampleFilter;
        _host.TickComplete += OnTickComplete;
    }

    public WorldStateSummary CaptureCurrent(uint frame) => WorldStateSummary.Capture(_host.DomainModel, frame);

    private void OnTickComplete(object? sender, SimulationHostTickEventArgs e)
    {
        // A paused game (IsLogicRunning false) still completes host steps without advancing the
        // logic frame; sampling those would duplicate frames, so only new logic frames sample.
        if (_previous is not null && e.LogicFrame.Value <= _previous.Frame)
        {
            return;
        }

        // Operational mode (WI-A1-19): only cadence boundaries sample.
        if (_sampleFilter is not null && !_sampleFilter(e.LogicFrame.Value))
        {
            return;
        }

        var summary = WorldStateSummary.Capture(_host.DomainModel, e.LogicFrame.Value);
        var previous = _previous;
        _previous = summary;

        WorldStateDelta delta;
        if (previous is null)
        {
            delta = new WorldStateDelta
            {
                Frame = e.LogicFrame.Value,
            };
            delta.PlayerMoney.AddRange(summary.PlayerMoney);
        }
        else
        {
            delta = WorldStateDelta.Diff(e.LogicFrame.Value, previous, summary);
        }

        FrameSampled?.Invoke(summary, delta);
    }

    public void Dispose() => _host.TickComplete -= OnTickComplete;
}
