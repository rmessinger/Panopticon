#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon;
using Panopticon.IO;
using Panopticon.Logic;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Kind of one artifact record (WI-A5-08): snapshots reset the reconstruction baseline, deltas
/// advance it one frame each.
/// </summary>
public enum WorldStateRecordKind
{
    Snapshot = 1,
    Delta = 2,
}

/// <summary>
/// Bounded-memory reader for snapshot/delta artifacts (WI-A5-08). Opening scans the file once
/// and retains only per-record offsets and frames (the artifact format carries no record-length
/// framing, so boundaries are found by decoding; decoded records are discarded). Reconstructing
/// a frame seeks to the nearest prior snapshot and applies only the deltas up to that frame, so
/// resident state stays bounded by the reconstruction cache, not the run length. A torn or
/// truncated tail yields the valid prefix plus a diagnostic instead of a load failure, matching
/// the writer's crash-leaves-a-usable-prefix contract.
/// </summary>
public sealed class WorldStateArtifactReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly SageGame _sageGame;
    private readonly List<(long Offset, uint Frame)> _snapshots = new();
    private readonly List<(long Offset, uint Frame)> _deltas = new();

    private readonly Dictionary<uint, WorldStateSummary> _cache = new();
    private readonly LinkedList<uint> _cacheOrder = new();
    private readonly Dictionary<uint, LinkedListNode<uint>> _cacheNodes = new();

    /// <param name="cacheCapacity">
    /// Maximum reconstructed frames kept resident (WI-A5-08 bounded-memory contract); the least
    /// recently used frame is evicted first.
    /// </param>
    public WorldStateArtifactReader(string path, IGame game, int cacheCapacity = 8)
        : this(path, game.SageGame, cacheCapacity)
    {
    }

    /// <summary>WI-A5-04: reconstructs recorded state without a booted engine game.</summary>
    public WorldStateArtifactReader(string path, SageGame sageGame, int cacheCapacity = 8)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _sageGame = sageGame;
        CacheCapacity = Math.Max(1, cacheCapacity);
        Scan();
    }

    /// <summary>Maximum reconstructed frames kept resident.</summary>
    public int CacheCapacity { get; }

    /// <summary>Frames of every indexed snapshot, ascending.</summary>
    public IReadOnlyList<uint> SnapshotFrames => _snapshots.ConvertAll(entry => entry.Frame);

    /// <summary>Total records in the valid prefix.</summary>
    public int RecordCount => _snapshots.Count + _deltas.Count;

    /// <summary>Earliest reconstructable frame (the first snapshot; frame zero when present).</summary>
    public uint FirstFrame => _snapshots.Count > 0 ? _snapshots[0].Frame : throw new InvalidOperationException("Artifact contains no snapshot.");

    /// <summary>Latest frame covered by the valid prefix.</summary>
    public uint LastFrame => _snapshots.Count + _deltas.Count == 0
        ? throw new InvalidOperationException("Artifact contains no records.")
        : Math.Max(_snapshots[^1].Frame, _deltas.Count > 0 ? _deltas[^1].Frame : 0);

    /// <summary>True when the file ends in a partial or undecodable record.</summary>
    public bool IsTruncated { get; private set; }

    /// <summary>Human-readable truncation diagnostic naming the failure offset.</summary>
    public string? TruncationDiagnostic { get; private set; }

    /// <summary>Resident reconstructed frames; never exceeds <see cref="CacheCapacity"/>.</summary>
    internal int CachedFrameCount => _cache.Count;

    /// <summary>
    /// Reconstructs the world state at <paramref name="frame"/> from the nearest prior snapshot
    /// plus the deltas up to that frame. Frames beyond the valid prefix reconstruct at the
    /// prefix's last state (matching <see cref="WorldStateArtifact.Reconstruct"/>).
    /// </summary>
    public WorldStateSummary ReconstructStateAt(uint frame)
    {
        if (_cache.TryGetValue(frame, out var cached))
        {
            var node = _cacheNodes[frame];
            _cacheOrder.Remove(node);
            _cacheOrder.AddFirst(node);
            return cached;
        }

        var baselineIndex = -1;
        for (var i = 0; i < _snapshots.Count; i++)
        {
            if (_snapshots[i].Frame <= frame)
            {
                baselineIndex = i;
            }
            else
            {
                break;
            }
        }

        if (baselineIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), $"No snapshot at or before frame {frame}.");
        }

        var baseline = ReadSnapshot(_snapshots[baselineIndex].Offset);
        var objects = new Dictionary<uint, WorldObjectState>();
        foreach (var obj in baseline.Objects)
        {
            objects[obj.ObjectId] = obj;
        }

        var money = new List<uint>(baseline.PlayerMoney);

        foreach (var (offset, deltaFrame) in _deltas)
        {
            if (deltaFrame <= baseline.Frame)
            {
                continue;
            }

            if (deltaFrame > frame)
            {
                break;
            }

            ReadDelta(offset).Apply(objects, money);
        }

        var result = new WorldStateSummary { Frame = frame };
        result.Objects.AddRange(objects.Values.OrderBy(o => o.ObjectId));
        result.PlayerMoney.AddRange(money);
        StoreInCache(result);
        return result;
    }

    private void StoreInCache(WorldStateSummary summary)
    {
        if (_cache.TryGetValue(summary.Frame, out var existing))
        {
            var node = _cacheNodes[summary.Frame];
            _cacheOrder.Remove(node);
            _cacheOrder.AddFirst(node);
            _cache[summary.Frame] = summary;
            return;
        }

        if (_cache.Count >= CacheCapacity)
        {
            var evicted = _cacheOrder.Last!.Value;
            _cacheOrder.RemoveLast();
            _cache.Remove(evicted);
            _cacheNodes.Remove(evicted);
        }

        _cache[summary.Frame] = summary;
        _cacheNodes[summary.Frame] = _cacheOrder.AddFirst(summary.Frame);
    }

    // One indexing pass: decode each record to find its end, keep only offset + frame. The
    // artifact format has no length framing, so this is the only way to learn boundaries; the
    // decoded bodies are discarded so memory stays proportional to the record count index.
    private void Scan()
    {
        using var reader = new StateReader(_stream, _sageGame);
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var offset = reader.BaseStream.Position;
            try
            {
                var tag = byte.MaxValue;
                reader.PersistByte(ref tag, "RecordType");
                switch (tag)
                {
                    case (byte)WorldStateRecordKind.Snapshot:
                        var snapshot = new WorldStateSummary();
                        reader.PersistObject(snapshot, "Snapshot");
                        _snapshots.Add((offset, snapshot.Frame));
                        break;
                    case (byte)WorldStateRecordKind.Delta:
                        var delta = new WorldStateDelta();
                        reader.PersistObject(delta, "Delta");
                        _deltas.Add((offset, delta.Frame));
                        break;
                    default:
                        throw new InvalidDataException($"Unknown artifact record type {tag} at offset {offset}.");
                }
            }
            catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException or InvalidStateException)
            {
                // Torn tail: the writer flushes per record, so an undecodable record is a
                // partial write at the end. Everything before it stays usable.
                reader.BaseStream.Position = offset;
                IsTruncated = true;
                TruncationDiagnostic = $"Artifact truncated at offset {offset}: valid prefix ends at frame {LastFrameSafe()}. ({exception.Message})";
                return;
            }
        }
    }

    private uint LastFrameSafe()
    {
        var snapshotFrame = _snapshots.Count > 0 ? _snapshots[^1].Frame : 0;
        var deltaFrame = _deltas.Count > 0 ? _deltas[^1].Frame : 0;
        return Math.Max(snapshotFrame, deltaFrame);
    }

    private WorldStateSummary ReadSnapshot(long offset)
    {
        _stream.Position = offset;
        using var reader = new StateReader(_stream, _sageGame);
        var tag = byte.MaxValue;
        reader.PersistByte(ref tag, "RecordType");
        var snapshot = new WorldStateSummary();
        reader.PersistObject(snapshot, "Snapshot");
        return snapshot;
    }

    private WorldStateDelta ReadDelta(long offset)
    {
        _stream.Position = offset;
        using var reader = new StateReader(_stream, _sageGame);
        var tag = byte.MaxValue;
        reader.PersistByte(ref tag, "RecordType");
        var delta = new WorldStateDelta();
        reader.PersistObject(delta, "Delta");
        return delta;
    }

    public void Dispose() => _stream.Dispose();
}
