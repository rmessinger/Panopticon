#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Simulation;

namespace Panopticon.Recording;

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

    /// <summary>Null in <see cref="RecordingMode.Operational"/>, where live capture is off (A1-6 §4).</summary>
    public TriggeredDetailCapture? T2 { get; }

    public SnapshotDeltaRecorder Snapshots { get; }

    /// <summary>
    /// Domain state recording (A4-4); null unless the host's domain implements
    /// <see cref="IDomainStateProjection"/>.
    /// </summary>
    public DomainStateRecorder? DomainState { get; }

    /// <summary>Recording options (WI-A1-19); null when the caller omitted them (default Full path).</summary>
    public RecordingOptions? Options { get; }

    /// <summary>Effective cadence: the domain's when reconstructable, else the options default, else 1800/648000.</summary>
    public RecordingCadence EffectiveCadence { get; }

    /// <summary>The host's domain viewed as an operational-recording participant; null when absent.</summary>
    public IReconstructableDomain? ReconstructableDomain { get; }

    /// <summary>
    /// Operational bundle writer (WI-A1-21); null unless operational mode requested a bundle
    /// directory.
    /// </summary>
    internal OperationalRecorder? Operational { get; }

    private RunRecording(
        WorldStateSampler sampler,
        RunOutputRecorder t0,
        TieredSignatureRecorder t1,
        TriggeredDetailCapture? t2,
        SnapshotDeltaRecorder snapshots,
        DomainStateRecorder? domainState,
        RecordingOptions? options,
        RecordingCadence effectiveCadence,
        IReconstructableDomain? reconstructableDomain,
        OperationalRecorder? operational)
    {
        Sampler = sampler;
        T0 = t0;
        T1 = t1;
        T2 = t2;
        Snapshots = snapshots;
        DomainState = domainState;
        Options = options;
        EffectiveCadence = effectiveCadence;
        ReconstructableDomain = reconstructableDomain;
        Operational = operational;
    }

    /// <summary>
    /// Attaches the full recording stack to a host run. The snapshot artifact streams to
    /// <paramref name="snapshotArtifactPath"/> when given; the domain-state artifact (A4-4)
    /// streams to <paramref name="domainStateArtifactPath"/> when the domain implements
    /// <see cref="IDomainStateProjection"/>. Omitting <paramref name="options"/> follows the
    /// existing <see cref="RecordingMode.Full"/> path unchanged (WI-A1-19).
    /// </summary>
    public static RunRecording Attach(
        SimulationHost host,
        IGame game,
        uint seed,
        string? scenarioId = null,
        uint sampleInterval = RunOutputRecorder.DefaultSampleInterval,
        uint snapshotInterval = 300,
        SignatureTolerances? tolerances = null,
        string? snapshotArtifactPath = null,
        string? domainStateArtifactPath = null,
        RecordingOptions? options = null,
        TriggeredDetailCaptureOptions? detailOptions = null,
        Func<WorldObjectState, string>? stateRegionClassifier = null)
    {
        var reconstructable = host.DomainModel as IReconstructableDomain;
        var effectiveCadence = reconstructable?.Cadence
            ?? options?.DefaultCadence
            ?? RecordingOptions.CreateDefaultCadence();
        Func<uint, bool>? sampleFilter = options?.Mode == RecordingMode.Operational ? effectiveCadence.IsOperationalBoundary : null;
        var sampler = new WorldStateSampler(host, game, sampleFilter);
        var t0 = new RunOutputRecorder(host, game, seed, scenarioId, sampleInterval);
        // T2 subscribes before T1 so the ring buffer has ingested the current frame's delta
        // before the trigger evaluates and trips. Operational mode has no live capture (A1-6 §4).
        // WI-A5-21: region-filtered detail routes the domain's TopologyKey as the classifier.
        Func<WorldObjectState, string>? regionClassifier = null;
        if (detailOptions?.RegionKeys is { Count: > 0 })
        {
            string Classify(WorldObjectState o)
            {
                return host.DomainModel.TopologyKey(new DomainEntitySummary(o.ObjectId, o.DefinitionName, o.OwnerIndex, o.Position, o.Health, o.MaxHealth));
            }

            regionClassifier = Classify;
        }

        var t2 = options?.Mode == RecordingMode.Operational
            ? null
            : detailOptions is null
                ? new TriggeredDetailCapture(sampler)
                : TriggeredDetailCapture.Create(sampler, detailOptions, regionClassifier);
        var t1 = new TieredSignatureRecorder(sampler, tolerances, sampleInterval, domain: host.DomainModel);
        // WI-A5-29: the state artifact becomes region-indexed when a classifier is supplied,
        // either explicitly or implicitly through detail-region configuration.
        var snapshots = new SnapshotDeltaRecorder(sampler, snapshotInterval, snapshotArtifactPath, snapshotArtifactPath is null ? null : game, stateRegionClassifier ?? regionClassifier);
        // WI-A5-07: frame-zero baseline lands before the first delta, so bundle consumers can
        // reconstruct the run's initial state unconditionally.
        snapshots.CaptureInitial();
        var domainState = host.DomainModel is IDomainStateProjection projection
            ? new DomainStateRecorder(sampler, projection, snapshotInterval, domainStateArtifactPath, domainStateArtifactPath is null ? null : game)
            : null;
        // The operational recorder subscribes last: at a both-boundary frame the stream chunk
        // (written from FrameSampled) lands before the checkpoint (written from TickComplete).
        var operational = options is { Mode: RecordingMode.Operational } && !string.IsNullOrEmpty(options.BundleDirectory)
            ? new OperationalRecorder(host, game, sampler, seed, scenarioId, options, effectiveCadence, tolerances ?? SignatureTolerances.Default)
            : null;
        return new RunRecording(sampler, t0, t1, t2, snapshots, domainState, options, effectiveCadence, reconstructable, operational);
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
        output.T1Configuration = new RunSignatureConfigurationRecord
        {
            PositionEpsilon = T1.Tolerances.PositionEpsilon,
            HealthEpsilon = T1.Tolerances.HealthEpsilon,
            Strict = T1.Tolerances.Strict,
            CellSize = T1.CellSize,
            BucketKeys = T1.UsesDomainBuckets ? "domain" : "legacy-spatial",
        };
        output.SignatureTrees.AddRange(T1.SampledFrames.Select(RunOutput.ToDto));
        foreach (var window in (T2?.Complete() ?? Array.Empty<TriggeredDetailWindow>()))
        {
            output.TriggeredCaptures.Add(RunOutput.ToDto(window));
        }

        output.SnapshotInterval = Snapshots.SnapshotInterval;
        output.SnapshotArtifactPath = Snapshots.ArtifactPath;
        output.SnapshotFrames.AddRange(Snapshots.Snapshots.Select(s => s.Frame));

        if (DomainState is not null)
        {
            output.DomainStateSchemaVersion = DomainState.Schema.Version;
            output.DomainStateArtifactPath = DomainState.ArtifactPath;
            output.DomainStateSnapshotFrames = DomainState.Snapshots.Select(s => s.Frame).ToList();
        }

        Operational?.Complete(output);

        return output;
    }

    public void Dispose()
    {
        Sampler.Dispose();
        T0.Dispose();
        T1.Dispose();
        T2?.Dispose();
        Snapshots.Dispose();
        DomainState?.Dispose();
        Operational?.Dispose();
    }
}
