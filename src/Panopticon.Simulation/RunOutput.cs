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
    /// <summary>Manifest schema version this build writes (WI-A5-07).</summary>
    public const int CurrentManifestSchemaVersion = 1;

    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public uint Seed { get; set; }
    public string? ScenarioId { get; set; }

    /// <summary>
    /// Manifest schema version (WI-A5-07). Loaders reject manifests written under a newer
    /// version. Zero means a pre-versioning manifest; it loads unchanged under the
    /// compatibility default.
    /// </summary>
    public int ManifestSchemaVersion { get; set; } = CurrentManifestSchemaVersion;

    // A4-1: the domain adapter that produced this run ("rts", "transport-graph", ...).
    public string? DomainId { get; set; }
    public uint TotalLogicFrames { get; set; }
    public double WallClockDurationMilliseconds { get; set; }
    public RunOutcomeRecord? Outcome { get; set; }
    public List<RunMetricSample> MetricSamples { get; set; } = new();

    // T1 tier: one signature tree per sampled frame (WI-A1-09).
    public uint T1SampleInterval { get; set; }
    public List<SignatureTreeSample> SignatureTrees { get; set; } = new();

    // T1 recording configuration (WI-A5-07): what the trees were quantized and bucketed with,
    // so comparison surfaces can reject runs recorded under different settings.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunSignatureConfigurationRecord? T1Configuration { get; set; }

    // T2 tier: bounded triggered detail windows (WI-A1-10).
    public List<TriggeredDetailWindowRecord> TriggeredCaptures { get; set; } = new();

    // Snapshot/delta recording (WI-A1-11): binary artifact + index.
    public uint SnapshotInterval { get; set; }
    public string? SnapshotArtifactPath { get; set; }
    public List<uint> SnapshotFrames { get; set; } = new();

    // A4-4 domain state projection (WI-A4-44): present only when the domain implements
    // IDomainStateProjection; omitted from JSON otherwise so RTS outputs are unchanged.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DomainStateSchemaVersion { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DomainStateArtifactPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<uint>? DomainStateSnapshotFrames { get; set; }

    // Operational recording (WI-A1-21): manifest fields for operational bundles; omitted from
    // JSON otherwise so Full-mode outputs are unchanged.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecordingMode? RecordingMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationalStreamPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RunCheckpointRecord>? Checkpoints { get; set; }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static RunOutput? FromJson(string json)
    {
        var output = JsonSerializer.Deserialize<RunOutput>(json, JsonOptions);
        if (output is null)
        {
            return null;
        }

        if (output.ManifestSchemaVersion > CurrentManifestSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Run manifest reports schema version {output.ManifestSchemaVersion}; this build understands up to {CurrentManifestSchemaVersion}.");
        }

        if (output.ManifestSchemaVersion == 0)
        {
            output.ManifestSchemaVersion = CurrentManifestSchemaVersion;
        }

        return output;
    }

    public static RunOutput? LoadFromFile(string path) =>
        ResolveBundlePaths(path, FromJson(File.ReadAllText(path)));

    public void SaveToFile(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // WI-A5-07: the manifest stores artifact references relative to itself when they live
        // inside the bundle, so bundles relocate; absolute references outside the bundle stay.
        var node = JsonSerializer.SerializeToNode(this, JsonOptions) as JsonObject ?? throw new InvalidOperationException("Run output serialized to null.");
        RelativizeNode(node, "snapshotArtifactPath", directory);
        RelativizeNode(node, "domainStateArtifactPath", directory);
        RelativizeNode(node, "operationalStreamPath", directory);
        if (node["checkpoints"] is JsonArray checkpoints)
        {
            foreach (var checkpoint in checkpoints.OfType<JsonObject>())
            {
                if (checkpoint["path"]?.GetValue<string>() is { } stored)
                {
                    checkpoint["path"] = MakePortable(stored, directory);
                }
            }
        }

        File.WriteAllText(path, node.ToJsonString(JsonOptions));
    }

    private static void RelativizeNode(JsonObject node, string propertyName, string? directory)
    {
        if (node[propertyName]?.GetValue<string>() is { } stored)
        {
            node[propertyName] = MakePortable(stored, directory);
        }
    }

    internal static string MakePortable(string stored, string? directory)
    {
        if (directory is null || !Path.IsPathRooted(stored))
        {
            return stored;
        }

        var relative = Path.GetRelativePath(directory, stored);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? stored : relative;
    }

    /// <summary>
    /// WI-A5-07: resolves one stored artifact reference against the manifest's location.
    /// Bundle-relative references become absolute; absolute references pass through.
    /// </summary>
    public static string? ResolveBundlePath(string manifestPath, string? storedPath)
    {
        if (string.IsNullOrEmpty(storedPath) || Path.IsPathRooted(storedPath))
        {
            return storedPath;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        return Path.GetFullPath(Path.Combine(directory ?? ".", storedPath));
    }

    private static RunOutput? ResolveBundlePaths(string manifestPath, RunOutput? output)
    {
        if (output is null)
        {
            return null;
        }

        output.SnapshotArtifactPath = ResolveBundlePath(manifestPath, output.SnapshotArtifactPath);
        output.DomainStateArtifactPath = ResolveBundlePath(manifestPath, output.DomainStateArtifactPath);
        output.OperationalStreamPath = ResolveBundlePath(manifestPath, output.OperationalStreamPath);
        if (output.Checkpoints is not null)
        {
            foreach (var checkpoint in output.Checkpoints)
            {
                checkpoint.Path = ResolveBundlePath(manifestPath, checkpoint.Path) ?? checkpoint.Path;
            }
        }

        return output;
    }

    /// <summary>
    /// WI-A5-07: signature-tree comparison precondition. Throws when the two runs recorded
    /// their T1 trees under different quantization or bucketing, which would make divergence
    /// results meaningless. Runs without a recorded configuration used the documented defaults
    /// (domain bucket keys, default tolerances and cell size).
    /// </summary>
    public void ValidateSignatureConfigurationCompatible(RunOutput other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var a = EffectiveSignatureConfiguration();
        var b = other.EffectiveSignatureConfiguration();
        if (a.PositionEpsilon != b.PositionEpsilon || a.HealthEpsilon != b.HealthEpsilon
            || a.Strict != b.Strict || a.CellSize != b.CellSize || a.BucketKeys != b.BucketKeys)
        {
            throw new InvalidOperationException(
                $"Runs {RunId} and {other.RunId} recorded T1 trees under different configurations "
                + $"(tolerances {a.PositionEpsilon}/{a.HealthEpsilon}/{a.Strict}, cell size {a.CellSize}, "
                + $"bucket keys {a.BucketKeys} vs tolerances {b.PositionEpsilon}/{b.HealthEpsilon}/{b.Strict}, "
                + $"cell size {b.CellSize}, bucket keys {b.BucketKeys}); their trees are not comparable.");
        }
    }

    private RunSignatureConfigurationRecord EffectiveSignatureConfiguration() => T1Configuration ?? new RunSignatureConfigurationRecord
    {
        PositionEpsilon = Recording.SignatureTolerances.Default.PositionEpsilon,
        HealthEpsilon = Recording.SignatureTolerances.Default.HealthEpsilon,
        Strict = Recording.SignatureTolerances.Default.Strict,
        CellSize = Recording.SignatureTreeBuilder.DefaultCellSize,
        BucketKeys = "domain",
    };

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
        Reason = window.Reason,
        RingDepth = window.RingDepth,
        MaxWindows = window.MaxWindows,
        CooldownFrames = window.CooldownFrames,
        RegionKeys = window.RegionKeys?.ToList(),
        CapturedEntityCount = window.CapturedEntityCount,
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

/// <summary>One engine checkpoint in an operational bundle manifest (WI-A1-21).</summary>
public sealed class RunCheckpointRecord
{
    public uint Frame { get; set; }
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public string XxHash3 { get; set; } = "";
}

/// <summary>
/// T1 recording configuration as written into the run manifest (WI-A5-07): quantization
/// tolerances and the bucket-key strategy the trees were built with, so comparison surfaces
/// can reject mismatched runs before reporting meaningless divergence.
/// </summary>
public sealed class RunSignatureConfigurationRecord
{
    public float PositionEpsilon { get; set; }
    public float HealthEpsilon { get; set; }
    public bool Strict { get; set; }
    public float CellSize { get; set; }

    /// <summary>Bucket-key strategy: <c>domain</c> keys from the topology contract or <c>legacy-spatial</c>.</summary>
    public string BucketKeys { get; set; } = "domain";
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

    /// <summary>WI-A5-21: trigger reason ("divergence" or "marked").</summary>
    public string Reason { get; set; } = "divergence";

    /// <summary>WI-A5-21: budgets this window was captured under.</summary>
    public int RingDepth { get; set; }
    public int MaxWindows { get; set; }
    public uint CooldownFrames { get; set; }

    /// <summary>WI-A5-21: region keys the frames were filtered to; null when unfiltered.</summary>
    public List<string>? RegionKeys { get; set; }

    /// <summary>WI-A5-21: distinct entity ids appearing across the captured frames.</summary>
    public int CapturedEntityCount { get; set; }
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
