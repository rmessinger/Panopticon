#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    // A4-1 domain metric series rows (transport: edge throughput, node occupancy); the RTS
    // adapter emits none, so existing run outputs are unchanged.
    public List<RunDomainMetricValue> DomainMetrics { get; set; } = new();
}

/// <summary>One value in a named, keyed domain metric series at one sample.</summary>
public sealed class RunDomainMetricValue
{
    public string Series { get; set; } = "";
    public string Key { get; set; } = "";
    public double Value { get; set; }
}

/// <summary>
/// Run output of a headless <see cref="SimulationHost"/> run (A1-5). T0 (aggregate): run
/// identity, seed, frame count, wall-clock duration, terminal outcome, and a bounded per-player
/// metric time series. T1: tolerance-quantized hierarchical signature trees per sampled frame.
/// T2: bounded triggered detail windows. Snapshot/delta recording streams to a sibling binary
/// artifact referenced by <see cref="SnapshotArtifactPath"/>. Serialized with the P0 harness
/// JSON conventions (camelCase, string enums, indented) so the A5 visualizer reads one format.
/// </summary>
public sealed class RunOutput
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public uint Seed { get; set; }
    public string? ScenarioId { get; set; }

    // A4-1: the domain adapter that produced this run ("rts", "transport-graph", ...).
    public string? DomainId { get; set; }
    public uint TotalLogicFrames { get; set; }
    public double WallClockDurationMilliseconds { get; set; }
    public RunOutcomeRecord? Outcome { get; set; }
    public List<RunMetricSample> MetricSamples { get; set; } = new();

    // T1 tier: one signature tree per sampled frame (WI-A1-09).
    public uint T1SampleInterval { get; set; }
    public List<SignatureTreeSample> SignatureTrees { get; set; } = new();

    // T2 tier: bounded triggered detail windows (WI-A1-10).
    public List<TriggeredDetailWindowRecord> TriggeredCaptures { get; set; } = new();

    // Snapshot/delta recording (WI-A1-11): binary artifact + index.
    public uint SnapshotInterval { get; set; }
    public string? SnapshotArtifactPath { get; set; }
    public List<uint> SnapshotFrames { get; set; } = new();

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

    internal static SignatureTreeSample ToDto(Recording.SignatureTreeFrame tree) => new()
    {
        Frame = tree.Frame,
        Nodes = tree.Nodes.Select(n => new SignatureNodeRecord
        {
            Key = n.Key,
            Level = n.Level,
            Hash = n.Hash,
            MemberCount = n.MemberCount,
        }).ToList(),
    };

    internal static TriggeredDetailWindowRecord ToDto(Recording.TriggeredDetailWindow window) => new()
    {
        Marked = window.Marked,
        TriggeredAtFrame = window.TriggeredAtFrame,
        StartFrame = window.StartFrame,
        EndFrame = window.EndFrame,
        DivergedSubtrees = window.DivergedSubtrees?.ToList(),
        Frames = window.Frames.Select(ToDto).ToList(),
    };

    private static WorldStateDeltaRecord ToDto(Recording.WorldStateDelta delta) => new()
    {
        Frame = delta.Frame,
        Added = delta.Added.Select(ToDto).ToList(),
        Removed = delta.Removed.ToList(),
        Changed = delta.Changed.Select(ToDto).ToList(),
        PlayerMoney = delta.PlayerMoney.ToList(),
    };

    private static WorldObjectStateRecord ToDto(Recording.WorldObjectState obj) => new()
    {
        ObjectId = obj.ObjectId,
        DefinitionName = obj.DefinitionName,
        OwnerIndex = obj.OwnerIndex,
        X = obj.Position.X,
        Y = obj.Position.Y,
        Z = obj.Position.Z,
        Health = obj.Health,
        MaxHealth = obj.MaxHealth,
    };
}

public sealed class SignatureTreeSample
{
    public uint Frame { get; set; }
    public List<SignatureNodeRecord> Nodes { get; set; } = new();
}

public sealed class SignatureNodeRecord
{
    public string Key { get; set; } = "";
    public int Level { get; set; }
    public ulong Hash { get; set; }
    public int MemberCount { get; set; }
}

public sealed class TriggeredDetailWindowRecord
{
    public bool Marked { get; set; }
    public uint TriggeredAtFrame { get; set; }
    public uint StartFrame { get; set; }
    public uint EndFrame { get; set; }
    public List<string>? DivergedSubtrees { get; set; }
    public List<WorldStateDeltaRecord> Frames { get; set; } = new();
}

public sealed class WorldStateDeltaRecord
{
    public uint Frame { get; set; }
    public List<WorldObjectStateRecord> Added { get; set; } = new();
    public List<uint> Removed { get; set; } = new();
    public List<WorldObjectStateRecord> Changed { get; set; } = new();
    public List<uint> PlayerMoney { get; set; } = new();
}

public sealed class WorldObjectStateRecord
{
    public uint ObjectId { get; set; }
    public string DefinitionName { get; set; } = "";
    public int OwnerIndex { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; set; }
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
