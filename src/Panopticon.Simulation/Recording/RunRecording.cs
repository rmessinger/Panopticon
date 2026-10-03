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
    private WorldStateSummary? _previous;

    /// <summary>Raised after each stepped frame with that frame's summary and delta.</summary>
    public event Action<WorldStateSummary, WorldStateDelta>? FrameSampled;

    public WorldStateSampler(SimulationHost host, IGame game)
    {
        _host = host;
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

/// <summary>
/// Snapshot and delta recording (WI-A1-11): periodic full world-state snapshots plus per-frame
/// deltas, so any frame can be reconstructed without assuming a re-run reproduces it. Snapshots
/// stream to a binary artifact (save-format persister primitives); in-memory records serve
/// reconstruction directly.
/// </summary>
public sealed class SnapshotDeltaRecorder : IDisposable
{
    private readonly WorldStateSampler _sampler;
    private readonly WorldStateArtifactWriter? _writer;

    public uint SnapshotInterval { get; }
    public List<WorldStateSummary> Snapshots { get; } = new();
    public List<WorldStateDelta> Deltas { get; } = new();

    public SnapshotDeltaRecorder(WorldStateSampler sampler, uint snapshotInterval = 300, string? artifactPath = null, IGame? game = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(snapshotInterval);

        if (artifactPath is not null && game is null)
            throw new ArgumentException($"{nameof(game)} is required when {nameof(artifactPath)} is set.", nameof(game));

        _sampler = sampler;
        SnapshotInterval = snapshotInterval;
        ArtifactPath = artifactPath;
        _writer = artifactPath is not null ? new WorldStateArtifactWriter(artifactPath, game!) : null;
        _sampler.FrameSampled += OnFrameSampled;
    }

    public string? ArtifactPath { get; }

    private void OnFrameSampled(WorldStateSummary summary, WorldStateDelta delta)
    {
        // Full snapshots at the interval; deltas every frame (the reconstruction unit).
        if (summary.Frame % SnapshotInterval == 0)
        {
            Snapshots.Add(summary);
            _writer?.WriteSnapshot(summary);
        }

        Deltas.Add(delta);
        _writer?.WriteDelta(delta);
    }

    /// <summary>
    /// Reconstructs the world state at <paramref name="frame"/> from the recorded snapshots
    /// and deltas.
    /// </summary>
    public WorldStateSummary ReconstructStateAt(uint frame) =>
        WorldStateArtifact.Reconstruct(Snapshots, Deltas, frame);

    public void Dispose()
    {
        _sampler.FrameSampled -= OnFrameSampled;
        _writer?.Dispose();
    }
}

/// <summary>
/// T1 hierarchical signature recording (WI-A1-09): builds the tolerance-quantized signature
/// tree per sampled frame. Per-frame trees are built only while a T2 trigger is armed, keeping
/// the always-on cost proportional to the sample interval.
/// </summary>
public sealed class TieredSignatureRecorder : IDisposable
{
    private readonly WorldStateSampler _sampler;
    private readonly ISimulationDomainModel? _domain;
    private Func<uint, SignatureTreeFrame, IReadOnlyList<string>?>? _trigger;
    private TriggeredDetailCapture? _detailCapture;

    public SignatureTolerances Tolerances { get; }
    public float CellSize { get; }
    public uint SampleInterval { get; }
    public List<SignatureTreeFrame> SampledFrames { get; } = new();

    private SignatureTreeFrame? _lastBuilt;

    /// <summary>The most recently built per-frame tree (sampled or trigger-built).</summary>
    public SignatureTreeFrame? LastBuilt => _lastBuilt;

    /// <summary>
    /// Bucket keys come from <paramref name="domain"/>'s topology contract when given,
    /// otherwise the legacy origin-anchored spatial-cell strategy is used.
    /// </summary>
    public TieredSignatureRecorder(
        WorldStateSampler sampler,
        SignatureTolerances? tolerances = null,
        uint sampleInterval = RunOutputRecorder.DefaultSampleInterval,
        float cellSize = SignatureTreeBuilder.DefaultCellSize,
        ISimulationDomainModel? domain = null)
    {
        _sampler = sampler;
        _domain = domain;
        Tolerances = tolerances ?? SignatureTolerances.Default;
        SampleInterval = sampleInterval == 0 ? RunOutputRecorder.DefaultSampleInterval : sampleInterval;
        CellSize = cellSize;
        _sampler.FrameSampled += OnFrameSampled;
    }

    /// <summary>
    /// Arms T2 triggered detail capture: from now on every frame's tree is built and offered to
    /// <paramref name="trigger"/>; a non-null diverged-subtree list trips the armed
    /// <see cref="TriggeredDetailCapture"/> and is recorded with the window.
    /// </summary>
    public void ArmTrigger(Func<uint, SignatureTreeFrame, IReadOnlyList<string>?> trigger, TriggeredDetailCapture detailCapture)
    {
        _trigger = trigger;
        _detailCapture = detailCapture;
    }

    private void OnFrameSampled(WorldStateSummary summary, WorldStateDelta delta)
    {
        var sampleDue = summary.Frame % SampleInterval == 0;

        if (sampleDue)
        {
            var tree = BuildTree(summary.Frame, summary);
            _lastBuilt = tree;
            SampledFrames.Add(tree);
        }

        if (_trigger is not null)
        {
            // Reuse the sampled tree when this frame was just sampled.
            var tree = sampleDue
                ? _lastBuilt!
                : BuildTree(summary.Frame, summary);
            _lastBuilt = tree;
            var diverged = _trigger(summary.Frame, tree);
            if (diverged is { Count: > 0 } && _detailCapture is not null)
            {
                _detailCapture.Trip(summary.Frame, tree, diverged);
            }
        }
    }

    private SignatureTreeFrame BuildTree(uint frame, WorldStateSummary summary)
    {
        return _domain is not null
            ? SignatureTreeBuilder.Build(frame, summary, _domain, Tolerances)
            : SignatureTreeBuilder.Build(frame, summary, Tolerances, CellSize);
    }

    public void Dispose() => _sampler.FrameSampled -= OnFrameSampled;
}

/// <summary>
/// One T2 capture: the frame the trigger fired at (or the marked window bounds) and the bounded
/// per-frame delta window captured from the ring buffer.
/// </summary>
public sealed class TriggeredDetailWindow
{
    public bool Marked { get; init; }
    public uint TriggeredAtFrame { get; init; }
    public List<WorldStateDelta> Frames { get; } = new();
    public IReadOnlyList<string>? DivergedSubtrees { get; init; }

    public uint StartFrame => Frames.Count > 0 ? Frames[0].Frame : TriggeredAtFrame;
    public uint EndFrame => Frames.Count > 0 ? Frames[^1].Frame : TriggeredAtFrame;
}

/// <summary>
/// T2 triggered detail capture (WI-A1-10): a bounded ring buffer of recent per-frame deltas,
/// tripped live by a T1 trigger (a re-run will not reproduce a divergence, per
/// [a5-visualization.md §5](../../docs/roadmap/deep-dives/a5-visualization.md)). Callers can
/// also pre-mark a frame window. Total windows and per-window depth are bounded.
/// </summary>
public sealed class TriggeredDetailCapture : IDisposable
{
    /// <summary>Default ring depth: one second of Generals logic.</summary>
    public const int DefaultRingDepth = 30;

    /// <summary>Default cap on captured windows, bounding output size.</summary>
    public const int DefaultMaxWindows = 8;

    private readonly WorldStateSampler _sampler;
    private readonly Queue<WorldStateDelta> _ring = new();
    private readonly List<TriggeredDetailWindow> _windows = new();
    private readonly List<WorldStateDelta> _markedFrames = new();
    private bool _hasMarkedWindow;
    private uint _markedStart;
    private uint _markedEnd;

    public int RingDepth { get; }
    public int MaxWindows { get; }
    public IReadOnlyList<TriggeredDetailWindow> Windows => _windows;

    private uint _lastTripFrame;
    private bool _full;

    public TriggeredDetailCapture(WorldStateSampler sampler, int ringDepth = DefaultRingDepth, int maxWindows = DefaultMaxWindows)
    {
        if (ringDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(ringDepth));

        _sampler = sampler;
        RingDepth = ringDepth;
        MaxWindows = maxWindows;
        _sampler.FrameSampled += OnFrameSampled;
    }

    /// <summary>
    /// Pre-marks a frame window for detail capture regardless of any trigger
    /// (§5.2: "a user can also pre-mark a frame/region window").
    /// </summary>
    public static TriggeredDetailCapture WithMarkedWindow(
        WorldStateSampler sampler,
        uint startFrame,
        uint endFrame,
        int ringDepth = DefaultRingDepth,
        int maxWindows = DefaultMaxWindows)
    {
        var capture = new TriggeredDetailCapture(sampler, ringDepth, maxWindows)
        {
            _markedStart = startFrame,
            _markedEnd = endFrame,
            _hasMarkedWindow = true,
        };
        return capture;
    }

    private void OnFrameSampled(WorldStateSummary summary, WorldStateDelta delta)
    {
        // Ring buffer: keep the most recent RingDepth frames.
        _ring.Enqueue(delta);
        while (_ring.Count > RingDepth)
        {
            _ring.Dequeue();
        }

        // Marked window collection.
        if (_hasMarkedWindow && delta.Frame >= _markedStart && delta.Frame <= _markedEnd)
        {
            _markedFrames.Add(delta);
        }
    }

    /// <summary>
    /// Captures the current ring buffer as a trigger window. Cooldown of one ring depth between
    /// trips keeps a persistently-diverging run from filling every window with the same region;
    /// the first trip is always allowed.
    /// </summary>
    internal void Trip(uint frame, SignatureTreeFrame tree, IReadOnlyList<string>? divergedSubtrees = null)
    {
        if (_full || (_lastTripFrame != 0 && frame < _lastTripFrame + (uint)RingDepth))
        {
            return;
        }

        var window = new TriggeredDetailWindow
        {
            TriggeredAtFrame = frame,
            DivergedSubtrees = divergedSubtrees,
        };
        window.Frames.AddRange(_ring);
        _windows.Add(window);
        _lastTripFrame = frame;
        _full = _windows.Count >= MaxWindows;
    }

    /// <summary>Finishes collection: flushes the marked window, if any, into the window list.</summary>
    public IReadOnlyList<TriggeredDetailWindow> Complete()
    {
        if (_hasMarkedWindow && _markedFrames.Count > 0)
        {
            var window = new TriggeredDetailWindow
            {
                Marked = true,
                TriggeredAtFrame = _markedFrames[0].Frame,
            };
            window.Frames.AddRange(_markedFrames);
            _windows.Add(window);
        }

        return _windows;
    }

    public void Dispose() => _sampler.FrameSampled -= OnFrameSampled;
}

/// <summary>
/// Wires the full A1-5 tiered recording onto a <see cref="SimulationHost"/> run: T0 metrics
/// (<see cref="RunOutputRecorder"/>), T1 signature trees, T2 triggered detail, and snapshot and
/// delta recording, all fed by one <see cref="WorldStateSampler"/> pass per frame.
/// </summary>
public sealed class RunRecording : IDisposable
{
    public WorldStateSampler Sampler { get; }
    public RunOutputRecorder T0 { get; }
    public TieredSignatureRecorder T1 { get; }
    public TriggeredDetailCapture T2 { get; }
    public SnapshotDeltaRecorder Snapshots { get; }

    private RunRecording(
        WorldStateSampler sampler,
        RunOutputRecorder t0,
        TieredSignatureRecorder t1,
        TriggeredDetailCapture t2,
        SnapshotDeltaRecorder snapshots)
    {
        Sampler = sampler;
        T0 = t0;
        T1 = t1;
        T2 = t2;
        Snapshots = snapshots;
    }

    /// <summary>
    /// Attaches the full recording stack to a host run. The snapshot artifact streams to
    /// <paramref name="snapshotArtifactPath"/> when given.
    /// </summary>
    public static RunRecording Attach(
        SimulationHost host,
        IGame game,
        uint seed,
        string? scenarioId = null,
        uint sampleInterval = RunOutputRecorder.DefaultSampleInterval,
        uint snapshotInterval = 300,
        SignatureTolerances? tolerances = null,
        string? snapshotArtifactPath = null)
    {
        var sampler = new WorldStateSampler(host, game);
        var t0 = new RunOutputRecorder(host, game, seed, scenarioId, sampleInterval);
        // T2 subscribes before T1 so the ring buffer has ingested the current frame's delta
        // before the trigger evaluates and trips.
        var t2 = new TriggeredDetailCapture(sampler);
        var t1 = new TieredSignatureRecorder(sampler, tolerances, sampleInterval, domain: host.DomainModel);
        var snapshots = new SnapshotDeltaRecorder(sampler, snapshotInterval, snapshotArtifactPath, snapshotArtifactPath is null ? null : game);
        return new RunRecording(sampler, t0, t1, t2, snapshots);
    }

    /// <summary>
    /// Arms the default T2 trigger: divergence against a reference run's T1 trees (the
    /// reference side of live comparison; the comparator UI is A5-4). Frames the reference did
    /// not sample do not count as divergence.
    /// </summary>
    public void ArmReferenceTrigger(IReadOnlyDictionary<uint, SignatureTreeFrame> referenceFrames)
    {
        T1.ArmTrigger(
            (frame, tree) =>
            {
                if (!referenceFrames.TryGetValue(frame, out var reference))
                {
                    return null;
                }

                var diverged = SignatureTreeComparer.FindDivergedSubtrees(reference, tree);
                return diverged.Count > 0 ? diverged : null;
            },
            T2);
    }

    /// <summary>Produces the run output with every tier that was recorded.</summary>
    public RunOutput Complete()
    {
        var output = T0.Complete();
        output.T1SampleInterval = T1.SampleInterval;
        output.SignatureTrees.AddRange(T1.SampledFrames.Select(RunOutput.ToDto));
        foreach (var window in T2.Complete())
        {
            output.TriggeredCaptures.Add(RunOutput.ToDto(window));
        }

        output.SnapshotInterval = Snapshots.SnapshotInterval;
        output.SnapshotArtifactPath = Snapshots.ArtifactPath;
        output.SnapshotFrames.AddRange(Snapshots.Snapshots.Select(s => s.Frame));
        return output;
    }

    public void Dispose()
    {
        Sampler.Dispose();
        T0.Dispose();
        T1.Dispose();
        T2.Dispose();
        Snapshots.Dispose();
    }
}
