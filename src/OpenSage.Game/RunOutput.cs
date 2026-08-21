#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSage.Logic;

namespace OpenSage;

/// <summary>
/// Terminal <see cref="GameOutcome"/> of a headless run, serialized without live object
/// references.
/// </summary>
public sealed class RunOutcomeRecord
{
    public OutcomeStatus Status { get; set; }
    public OutcomeReason Reason { get; set; }
    public List<string> WinningPlayers { get; set; } = new();
    public int? DecidedOnFrame { get; set; }
}

/// <summary>
/// Per-player metrics captured in one sample. Only counters that already exist on
/// <c>Player</c> and <c>GameLogic</c>; this tier adds no gameplay counters.
/// </summary>
public sealed class RunPlayerMetrics
{
    public string? Player { get; set; }
    public uint Money { get; set; }
    public int OwnedObjects { get; set; }
}

public sealed class RunMetricSample
{
    public uint Frame { get; set; }
    public List<RunPlayerMetrics> Players { get; set; } = new();
}

/// <summary>
/// T0 (aggregate) run output of a headless <see cref="SimulationHost"/> run (A1-5): run
/// identity, seed, frame count, wall-clock duration, terminal outcome, and a bounded per-player
/// metric time series. Serialized with the P0 harness JSON conventions (camelCase, string
/// enums, indented) so the A5 visualizer reads one format. T1/T2 tiers are separate follow-ups.
/// </summary>
public sealed class RunOutput
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public uint Seed { get; set; }
    public string? ScenarioId { get; set; }
    public uint TotalLogicFrames { get; set; }
    public double WallClockDurationMilliseconds { get; set; }
    public RunOutcomeRecord? Outcome { get; set; }
    public List<RunMetricSample> MetricSamples { get; set; } = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static RunOutput? FromJson(string json) => JsonSerializer.Deserialize<RunOutput>(json, JsonOptions);

    public static RunOutput? LoadFromFile(string path) => FromJson(File.ReadAllText(path));

    public void SaveToFile(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, ToJson());
    }
}

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
        if (sampleInterval == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleInterval));
        }

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
        foreach (var player in _game.PlayerManager.Players)
        {
            sample.Players.Add(new RunPlayerMetrics
            {
                Player = player.DisplayName,
                Money = player.BankAccount.Money,
                OwnedObjects = player.OwnedObjects.Count,
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
