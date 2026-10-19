#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Simulation;

namespace Panopticon.Recording;

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

    public SnapshotDeltaRecorder(WorldStateSampler sampler, uint snapshotInterval = 300, string? artifactPath = null, IGame? game = null, Func<WorldObjectState, string>? regionClassifier = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(snapshotInterval);

        if (artifactPath is not null && game is null)
        {
            throw new ArgumentException($"{nameof(game)} is required when {nameof(artifactPath)} is set.", nameof(game));
        }

        _sampler = sampler;
        SnapshotInterval = snapshotInterval;
        ArtifactPath = artifactPath;
        _writer = artifactPath is not null ? new WorldStateArtifactWriter(artifactPath, game!, regionClassifier) : null;
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

    /// <summary>
    /// WI-A5-07: records the frame-zero baseline snapshot before any delta, so the bundle can
    /// always reconstruct the run's initial state. Idempotent: a frame-zero snapshot captured
    /// by the regular cadence does not duplicate it.
    /// </summary>
    internal void CaptureInitial()
    {
        if (Snapshots.Count > 0 && Snapshots[0].Frame == 0)
        {
            return;
        }

        var initial = _sampler.CaptureCurrent(0);
        Snapshots.Insert(0, initial);
        _writer?.WriteSnapshot(initial);
    }

    public void Dispose()
    {
        _sampler.FrameSampled -= OnFrameSampled;
        _writer?.Dispose();
    }
}
