#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Panopticon.Logic;
using Panopticon.Recording;

namespace Panopticon;

/// <summary>
/// Samples the T0 metric time series from a running <see cref="SimulationHost"/> via its
/// per-tick hook, at a fixed frame interval, and completes the <see cref="RunOutput"/> when the
/// run ends. Output size is bounded by the sampling interval, not per-entity state.
/// </summary>
public sealed class RunOutputRecorder : IDisposable
{
    /// <summary>
    /// Default sampling interval: one sample per second of Generals logic (30 frames).
    /// </summary>
    public const uint DefaultSampleInterval = 30;

    private readonly SimulationHost _host;
    private readonly IGame _game;
    private readonly uint _seed;
    private readonly string? _scenarioId;
    private readonly uint _sampleInterval;
    private readonly Stopwatch _stopwatch = new();
    private readonly List<RunMetricSample> _samples = new();

    public RunOutputRecorder(
        SimulationHost host,
        IGame game,
        uint seed,
        string? scenarioId = null,
        uint sampleInterval = DefaultSampleInterval)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sampleInterval);

        _host = host;
        _game = game;
        _seed = seed;
        _scenarioId = scenarioId;
        _sampleInterval = sampleInterval;

        _host.TickComplete += OnTickComplete;
        _stopwatch.Start();
    }

    private void OnTickComplete(object? sender, SimulationHostTickEventArgs e)
    {
        if (e.Frame % _sampleInterval != 0)
        {
            return;
        }

        var sample = new RunMetricSample
        {
            Frame = e.LogicFrame.Value,
        };
        var metrics = _host.DomainModel.SampleMetrics();
        foreach (var player in metrics.Players)
        {
            sample.Players.Add(new RunPlayerMetrics
            {
                Player = player.Player,
                Money = player.Money,
                OwnedObjects = player.OwnedObjects,
            });
        }

        foreach (var value in metrics.Series)
        {
            sample.DomainMetrics.Add(new RunDomainMetricValue
            {
                Series = value.Series,
                Key = value.Key,
                Value = value.Value,
            });
        }

        _samples.Add(sample);
    }

    /// <summary>
    /// Finishes the run output: stops the wall clock and records the terminal
    /// <see cref="GameOutcome"/>.
    /// </summary>
    public RunOutput Complete()
    {
        _stopwatch.Stop();

        var outcome = _game.CurrentOutcome;
        var outcomeRecord = new RunOutcomeRecord
        {
            Status = outcome.Status,
            Reason = outcome.Reason,
            DecidedOnFrame = outcome.DecidedOnFrame,
        };
        foreach (var player in outcome.WinningPlayers)
        {
            outcomeRecord.WinningPlayers.Add(player.DisplayName ?? player.Name!);
        }

        return new RunOutput
        {
            Seed = _seed,
            ScenarioId = _scenarioId,
            DomainId = _host.DomainModel.DomainId,
            TotalLogicFrames = _host.LogicFrame.Value,
            WallClockDurationMilliseconds = _stopwatch.Elapsed.TotalMilliseconds,
            Outcome = outcomeRecord,
            MetricSamples = _samples,
        };
    }

    public void Dispose()
    {
        _host.TickComplete -= OnTickComplete;
    }
}
