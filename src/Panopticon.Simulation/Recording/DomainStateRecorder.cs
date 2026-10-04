#nullable enable annotations
using System;
using System.Collections.Generic;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Domain state recording (A4-4): captures <see cref="IDomainStateProjection"/> state once per
/// sampled frame, keeps periodic snapshots plus per-frame deltas for reconstruction, and
/// optionally streams them to a binary artifact written live so a crash still leaves a usable
/// prefix. Samples exactly the frames the other tiers sample through
/// <see cref="WorldStateSampler.FrameSampled"/>.
/// </summary>
public sealed class DomainStateRecorder : IDisposable
{
    private readonly WorldStateSampler _sampler;
    private readonly IDomainStateProjection _projection;
    private readonly DomainStateArtifactWriter? _writer;
    private DomainStateFrame? _previous;

    public DomainStateSchema Schema { get; }
    public uint SnapshotInterval { get; }
    public string? ArtifactPath { get; }
    public List<DomainStateFrame> Snapshots { get; } = new();
    public List<DomainStateDelta> Deltas { get; } = new();

    public DomainStateRecorder(
        WorldStateSampler sampler,
        IDomainStateProjection projection,
        uint snapshotInterval = 300,
        string? artifactPath = null,
        IGame? game = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfZero(snapshotInterval);

        if (artifactPath is not null && game is null)
        {
            throw new ArgumentException($"{nameof(game)} is required when {nameof(artifactPath)} is set.", nameof(game));
        }

        projection.StateSchema.Validate();

        _sampler = sampler;
        _projection = projection;
        Schema = projection.StateSchema;
        SnapshotInterval = snapshotInterval;
        ArtifactPath = artifactPath;
        _writer = artifactPath is not null ? new DomainStateArtifactWriter(artifactPath, game!, projection.StateSchema) : null;
        _sampler.FrameSampled += OnFrameSampled;
    }

    private void OnFrameSampled(WorldStateSummary summary, WorldStateDelta worldDelta)
    {
        var current = _projection.CaptureState(summary.Frame);
        var previous = _previous;
        _previous = current;

        DomainStateDelta delta;
        if (previous is null)
        {
            delta = new DomainStateDelta
            {
                Frame = current.Frame,
            };
        }
        else
        {
            delta = DomainStateDelta.Diff(current.Frame, previous, current);
        }

        Deltas.Add(delta);
        _writer?.WriteDelta(delta);

        // Full snapshots at the interval; deltas every frame (the reconstruction unit).
        if (current.Frame % SnapshotInterval == 0)
        {
            Snapshots.Add(current);
            _writer?.WriteSnapshot(current);
        }
    }

    /// <summary>
    /// Reconstructs the domain state at <paramref name="frame"/> from the recorded snapshots
    /// and deltas.
    /// </summary>
    public DomainStateFrame ReconstructStateAt(uint frame) =>
        DomainStateReconstruction.Reconstruct(Snapshots, Deltas, frame);

    public void Dispose()
    {
        _sampler.FrameSampled -= OnFrameSampled;
        _writer?.Dispose();
    }
}
