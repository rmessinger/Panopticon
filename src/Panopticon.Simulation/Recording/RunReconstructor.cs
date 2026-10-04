#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Logic.Orders;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Reconstructs a recorded operational bundle (WI-A1-22 §6.1): restores the nearest checkpoint
/// at or before the requested frame through <see cref="IReconstructionBootstrap"/> +
/// <see cref="EngineCheckpointRestorer"/>, replays recorded inputs at their dispatch frames,
/// and delivers rebuilt boundary state to a sink. Runs are not deterministic, so frames after
/// the restored checkpoint are plausible, not authoritative (A1-6 §3); nothing is verified
/// against the recorded run beyond the checkpoint restore itself.
/// </summary>
public sealed class RunReconstructor
{
    private readonly string _bundleDirectory;
    private readonly IReconstructionBootstrap _bootstrap;
    private readonly RunOutput _manifest;
    private readonly string _streamPath;
    private readonly ReconstructionCache? _cache;

    private RunReconstructor(string bundleDirectory, IReconstructionBootstrap bootstrap, RunOutput manifest, ReconstructionCache? cache)
    {
        _bundleDirectory = bundleDirectory;
        _bootstrap = bootstrap;
        _manifest = manifest;
        _streamPath = Path.Combine(bundleDirectory, manifest.OperationalStreamPath!);
        _cache = cache;
    }

    /// <summary>The bundle manifest (run.json) this reconstructor was opened against.</summary>
    public RunOutput Manifest => _manifest;

    /// <summary>
    /// Opens a bundle: parses run.json and requires an operational manifest with checkpoints and
    /// a stream. The bootstrap must boot the bundle's scenario (same install, scenario, seed,
    /// roster, domain); checkpoint restore rejects a host whose domain state does not match.
    /// </summary>
    public static RunReconstructor Open(string bundlePath, IReconstructionBootstrap bootstrap, ReconstructionCache? cache = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentNullException.ThrowIfNull(bootstrap);

        var manifest = RunOutput.LoadFromFile(Path.Combine(bundlePath, "run.json"))
            ?? throw new InvalidDataException($"The bundle manifest at '{bundlePath}' is null.");
        if (manifest.RecordingMode != RecordingMode.Operational
            || string.IsNullOrEmpty(manifest.OperationalStreamPath)
            || manifest.Checkpoints is not { Count: > 0 })
        {
            throw new InvalidDataException($"'{bundlePath}' is not a completed operational run bundle.");
        }

        var streamPath = Path.Combine(bundlePath, manifest.OperationalStreamPath);
        if (!File.Exists(streamPath))
        {
            throw new FileNotFoundException("The operational stream is missing from the bundle.", streamPath);
        }

        return new RunReconstructor(bundlePath, bootstrap, manifest, cache);
    }

    /// <summary>
    /// Reconstructs the range [<paramref name="fromFrame"/>, <paramref name="toFrame"/>]:
    /// restores the nearest checkpoint at or before <paramref name="fromFrame"/> (a cached
    /// derived checkpoint when one exists, else the nearest bundle checkpoint), replays recorded
    /// inputs, steps to the last recorded boundary at or before <paramref name="toFrame"/>, and
    /// delivers every rebuilt boundary in the range to <paramref name="sink"/>. Returns the booted host, now at the final stepped frame.
    /// </summary>
    public SimulationHost Reconstruct(uint fromFrame, uint toFrame, IReconstructionSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (fromFrame > toFrame)
        {
            throw new ArgumentException($"{nameof(fromFrame)} must not exceed {nameof(toFrame)}.");
        }

        var host = _bootstrap.Boot();
        var projection = host.DomainModel as IDomainStateProjection;

        using (var reader = OpenReader(host))
        {
            // String ids are interned file-globally, so every chunk from the start must be
            // decoded in order; only in-window records are collected.
            var entries = reader.ReadIndex()
                .OrderBy(entry => entry.Frame)
                .ToList();

            var boundaryFrames = entries.Select(entry => entry.Frame).ToHashSet();
            var recordedInputs = new List<(uint InjectionFrame, uint DispatchFrame, IReadOnlyList<Order> Orders)>();
            foreach (var entry in entries.Where(entry => entry.Frame <= toFrame))
            {
                var (_, records) = reader.ReadChunk(entry);
                foreach (var record in records)
                {
                    if (record is OperationalInputs inputs)
                    {
                        foreach (var dispatch in inputs.Orders)
                        {
                            recordedInputs.Add((dispatch.InjectionFrame, dispatch.DispatchFrame, new[] { dispatch.Order }));
                        }
                    }
                }
            }

            // Prefer the nearest cached derived checkpoint so repeat scrubs re-simulate at most
            // one operational interval and agree with the earlier reconstruction.
            var runId = _manifest.RunId;
            var cached = _cache?.TryGetNearest(runId, fromFrame);

            uint checkpointFrame;
            if (cached is not null)
            {
                checkpointFrame = cached.Frame;
                RestoreCheckpointBytes(host, cached.Checkpoint, $"cached checkpoint at frame {cached.Frame}");
            }
            else
            {
                var checkpointRecord = _manifest.Checkpoints!
                    .Where(c => c.Frame <= fromFrame)
                    .OrderByDescending(c => c.Frame)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        $"The bundle has no checkpoint at or before frame {fromFrame}; the earliest is {_manifest.Checkpoints![0].Frame}.");
                checkpointFrame = checkpointRecord.Frame;
                RestoreCheckpoint(host, checkpointRecord);
            }

            var effectiveTo = entries
                .Where(entry => entry.Frame > checkpointFrame && entry.Frame <= toFrame)
                .Select(entry => entry.Frame)
                .DefaultIfEmpty(checkpointFrame)
                .Max();

            // A checkpoint at or after fromFrame that is itself a recorded boundary delivers
            // that boundary before stepping: the restored state is exactly the recorded state.
            if (checkpointFrame >= fromFrame && boundaryFrames.Contains(checkpointFrame))
            {
                var summary = WorldStateSummary.Capture(host.DomainModel, checkpointFrame);
                sink.OnBoundary(checkpointFrame, summary, projection?.CaptureState(checkpointFrame));
            }

            // Re-inject recorded inputs at their original injection moments: orders still
            // pending at checkpoint time were restored as part of the host section (skipped
            // below), orders dispatched before the checkpoint are past and skipped, orders
            // injected before the checkpoint but still pending enter the schedule up front,
            // and the rest are re-injected after the matching frame completes during Step, so
            // the pending schedule evolves exactly as it did while recording.
            var pendingFrames = host.CaptureHostState().PendingOrders.Keys.ToHashSet();
            var pendingReplayInputs = new List<(uint InjectionFrame, uint DispatchFrame, IReadOnlyList<Order> Orders)>();
            foreach (var input in recordedInputs)
            {
                if (input.DispatchFrame <= checkpointFrame || pendingFrames.Contains(input.DispatchFrame))
                {
                    continue;
                }

                if (input.InjectionFrame <= checkpointFrame)
                {
                    host.InjectOrders(input.DispatchFrame, input.Orders);
                }
                else
                {
                    pendingReplayInputs.Add(input);
                }
            }

            Step(host, fromFrame, effectiveTo, checkpointFrame, boundaryFrames, pendingReplayInputs, sink, projection, runId);
        }

        return host;
    }

    /// <summary>
    /// Writes the reconstructed range to a standard A1-5 snapshot/delta artifact: a snapshot at
    /// the first delivered boundary and one delta per subsequent boundary, loadable through
    /// <see cref="WorldStateArtifact.Load"/> and reconstructable at any boundary frame.
    /// </summary>
    public void ExportSlice(uint fromFrame, uint toFrame, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var sink = new BufferedReconstructionSink();
        var host = Reconstruct(fromFrame, toFrame, sink);
        if (sink.Frames.Count == 0)
        {
            throw new InvalidOperationException($"No recorded boundaries fall in [{fromFrame}, {toFrame}].");
        }

        using var writer = new WorldStateArtifactWriter(path, host.Game);
        writer.WriteSnapshot(sink.Summaries[0]);
        for (var i = 1; i < sink.Frames.Count; i++)
        {
            writer.WriteDelta(WorldStateDelta.Diff(sink.Frames[i], sink.Summaries[i - 1], sink.Summaries[i]));
        }
    }

    private OperationalStreamReader OpenReader(SimulationHost host)
    {
        var stream = new FileStream(_streamPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return OperationalStreamReader.Open(stream, host.Game);
    }

    private void RestoreCheckpoint(SimulationHost host, RunCheckpointRecord record)
    {
        var checkpointPath = Path.Combine(_bundleDirectory, record.Path);
        if (!File.Exists(checkpointPath))
        {
            throw new FileNotFoundException("A manifest checkpoint is missing from the bundle.", checkpointPath);
        }

        var bytes = File.ReadAllBytes(checkpointPath);
        var contentHash = EngineCheckpoint.ContentHash(bytes);
        if (!string.Equals(contentHash, record.XxHash3, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Checkpoint '{record.Path}' does not match its manifest hash.");
        }

        RestoreCheckpointBytes(host, bytes, $"'{record.Path}'");
    }

    private void RestoreCheckpointBytes(SimulationHost host, byte[] bytes, string sourceDescription)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var checkpoint = EngineCheckpoint.Read(stream);
        if (checkpoint.Header.RunId != _manifest.RunId)
        {
            throw new InvalidDataException($"Checkpoint {sourceDescription} was written for run {checkpoint.Header.RunId}.");
        }

        EngineCheckpointRestorer.Restore(host, checkpoint);
    }

    private void Step(
        SimulationHost host,
        uint fromFrame,
        uint effectiveTo,
        uint checkpointFrame,
        HashSet<uint> boundaryFrames,
        List<(uint InjectionFrame, uint DispatchFrame, IReadOnlyList<Order> Orders)> replayInputs,
        IReconstructionSink sink,
        IDomainStateProjection? projection,
        string runId)
    {
        var replayByInjectionFrame = replayInputs
            .GroupBy(input => input.InjectionFrame)
            .ToDictionary(group => group.Key, group => group.ToList());

        void OnTickComplete(object? sender, SimulationHostTickEventArgs e)
        {
            var frame = e.LogicFrame.Value;
            if (boundaryFrames.Contains(frame))
            {
                var summary = WorldStateSummary.Capture(host.DomainModel, frame);

                // Derived checkpoints let later scrubs restore straight here (WI-A1-23).
                if (_cache is not null && frame != checkpointFrame)
                {
                    StoreDerivedCheckpoint(host, frame, runId);
                }

                if (frame >= fromFrame)
                {
                    sink.OnBoundary(frame, summary, projection?.CaptureState(frame));
                }
            }

            if (replayByInjectionFrame.Remove(frame, out var dueInputs))
            {
                // The recorded run scheduled these orders after this frame fully completed
                // (InjectOrders fires between frames, after boundary sampling), so replay
                // re-injects only after delivery for this frame.
                foreach (var input in dueInputs)
                {
                    host.InjectOrders(input.DispatchFrame, input.Orders);
                }
            }
        }

        host.TickComplete += OnTickComplete;
        try
        {
            var budget = checked((int)(effectiveTo - host.LogicFrame.Value)) + 1;
            host.Run(budget, h => h.LogicFrame.Value >= effectiveTo);
        }
        finally
        {
            host.TickComplete -= OnTickComplete;
        }
    }

    private void StoreDerivedCheckpoint(SimulationHost host, uint frame, string runId)
    {
        var hostState = host.CaptureHostState();
        var sections = EngineCheckpoint.CaptureSections(host);
        using var buffer = new MemoryStream();
        EngineCheckpoint.Write(buffer, new EngineCheckpointHeader(frame, hostState.MapTime.Ticks, _manifest.RunId), sections);
        _cache!.Store(runId, frame, buffer.ToArray());
    }
}
