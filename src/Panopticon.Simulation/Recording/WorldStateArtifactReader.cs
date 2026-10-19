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

    // WI-A5-29: region-indexed (v2) entries, in file order.
    private readonly List<(long Offset, uint Frame, string RegionKey)> _regionSnapshots = new();
    private readonly List<(long Offset, uint Frame, string RegionKey)> _regionDeltas = new();
    private readonly List<(long Offset, uint Frame)> _globals = new();
    private readonly SortedSet<string> _knownRegions = new(StringComparer.Ordinal);

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
    public IReadOnlyList<uint> SnapshotFrames
    {
        get
        {
            var frames = new SortedSet<uint>();
            foreach (var entry in _snapshots)
            {
                frames.Add(entry.Frame);
            }

            foreach (var entry in _regionSnapshots)
            {
                frames.Add(entry.Frame);
            }

            return frames.ToList();
        }
    }

    /// <summary>Total records in the valid prefix.</summary>
    public int RecordCount => _snapshots.Count + _deltas.Count + _regionSnapshots.Count + _regionDeltas.Count + _globals.Count;

    /// <summary>WI-A5-29: true when the artifact carries the region index (v2 records).</summary>
    public bool HasRegionIndex => _regionSnapshots.Count + _regionDeltas.Count > 0;

    /// <summary>WI-A5-29: region schema id from the v2 header, or null for a v1 artifact.</summary>
    public string? RegionSchemaId { get; private set; }

    /// <summary>WI-A5-29: every region key seen in the index, ascending ordinal.</summary>
    public IReadOnlyList<string> KnownRegions => _knownRegions.ToList();

    /// <summary>WI-A5-29: full entity records decoded by the last region query (bounded-decode evidence).</summary>
    internal uint DecodedEntitiesLastQuery { get; private set; }

    /// <summary>Earliest reconstructable frame (the first snapshot; frame zero when present).</summary>
    public uint FirstFrame
    {
        get
        {
            if (_snapshots.Count > 0 && _regionSnapshots.Count > 0)
            {
                return Math.Min(_snapshots[0].Frame, _regionSnapshots[0].Frame);
            }

            if (_snapshots.Count > 0)
            {
                return _snapshots[0].Frame;
            }

            if (_regionSnapshots.Count > 0)
            {
                return _regionSnapshots[0].Frame;
            }

            throw new InvalidOperationException("Artifact contains no snapshot.");
        }
    }

    /// <summary>Latest frame covered by the valid prefix.</summary>
    public uint LastFrame => RecordCount == 0
        ? throw new InvalidOperationException("Artifact contains no records.")
        : Math.Max(
            Math.Max(
                _snapshots.Count > 0 ? _snapshots[^1].Frame : 0,
                _deltas.Count > 0 ? _deltas[^1].Frame : 0),
            Math.Max(
                _regionSnapshots.Count > 0 ? _regionSnapshots[^1].Frame : 0,
                Math.Max(
                    _regionDeltas.Count > 0 ? _regionDeltas[^1].Frame : 0,
                    _globals.Count > 0 ? _globals[^1].Frame : 0)));

    /// <summary>True when the file ends in a partial or undecodable record.</summary>
    public bool IsTruncated { get; private set; }

    /// <summary>Human-readable truncation diagnostic naming the failure offset.</summary>
    public string? TruncationDiagnostic { get; private set; }

    /// <summary>Resident reconstructed frames; never exceeds <see cref="CacheCapacity"/>.</summary>
    internal int CachedFrameCount => _cache.Count;

    /// <summary>
    /// Reconstructs the world state at <paramref name="frame"/> from the nearest prior snapshot
    /// plus the deltas up to that frame. Frames beyond the valid prefix reconstruct at the
    /// prefix's last state (matching <see cref="WorldStateArtifact.Reconstruct"/>). Works over
    /// both v1 and region-indexed v2 artifacts (WI-A5-29: the v2 path unions every region's
    /// records).
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

        var result = HasRegionIndex
            ? ReconstructFromRegions(frame, regionFilter: null)
            : ReconstructFromV1(frame);
        StoreInCache(result);
        return result;
    }

    /// <summary>
    /// WI-A5-29: reconstructs only the requested regions' state at <paramref name="frame"/>,
    /// decoding only those regions' records. Money comes from the latest frame global at or
    /// before the frame. Region parts are self-contained (cross-region moves are written as
    /// Removed in the old region plus Added in the new), so no other region is consulted.
    /// </summary>
    public WorldStateSummary ReconstructRegionStateAt(uint frame, IReadOnlyCollection<string> regionKeys)
    {
        ArgumentNullException.ThrowIfNull(regionKeys);
        return ReconstructFromRegions(frame, new HashSet<string>(regionKeys, StringComparer.Ordinal));
    }

    private WorldStateSummary ReconstructFromV1(uint frame)
    {
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
        return result;
    }

    private WorldStateSummary ReconstructFromRegions(uint frame, HashSet<string>? regionFilter)
    {
        DecodedEntitiesLastQuery = 0;
        var baselineIndex = -1;
        for (var i = 0; i < _regionSnapshots.Count; i++)
        {
            if (_regionSnapshots[i].Frame <= frame)
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

        var baselineFrame = _regionSnapshots[baselineIndex].Frame;
        var firstPart = baselineIndex;
        while (firstPart > 0 && _regionSnapshots[firstPart - 1].Frame == baselineFrame)
        {
            firstPart--;
        }

        var objects = new Dictionary<uint, WorldObjectState>();
        for (var i = firstPart; i <= baselineIndex; i++)
        {
            var (offset, _, key) = _regionSnapshots[i];
            if (regionFilter is not null && !regionFilter.Contains(key))
            {
                continue;
            }

            var part = ReadRegionSnapshot(offset);
            DecodedEntitiesLastQuery += (uint)part.Objects.Count;
            foreach (var obj in part.Objects)
            {
                objects[obj.ObjectId] = obj;
            }
        }

        var index = 0;
        while (index < _regionDeltas.Count)
        {
            var deltaFrame = _regionDeltas[index].Frame;
            if (deltaFrame <= baselineFrame)
            {
                index++;
                continue;
            }

            if (deltaFrame > frame)
            {
                break;
            }

            // One merged step per frame: apply every region's removals first, then the
            // additions and changes, so a cross-region move resolves exactly once regardless
            // of region order (filtered queries skip other regions' parts entirely).
            var removed = new List<uint>();
            var added = new List<WorldObjectState>();
            var changed = new List<WorldObjectState>();
            while (index < _regionDeltas.Count && _regionDeltas[index].Frame == deltaFrame)
            {
                var (offset, _, key) = _regionDeltas[index];
                if (regionFilter is null || regionFilter.Contains(key))
                {
                    var part = ReadRegionDelta(offset);
                    DecodedEntitiesLastQuery += (uint)(part.Added.Count + part.Changed.Count);
                    removed.AddRange(part.Removed);
                    added.AddRange(part.Added);
                    changed.AddRange(part.Changed);
                }

                index++;
            }

            foreach (var id in removed)
            {
                objects.Remove(id);
            }

            foreach (var obj in added)
            {
                objects[obj.ObjectId] = obj;
            }

            foreach (var obj in changed)
            {
                objects[obj.ObjectId] = obj;
            }
        }

        // Money is frame-global, not regional: the globals stream carries every frame
        // regardless of which regions changed, so resolve it at the requested frame.
        var money = new List<uint>();
        ApplyGlobalAtOrBefore(Math.Min(frame, LastFrame), money);

        var result = new WorldStateSummary { Frame = frame };
        result.Objects.AddRange(objects.Values.OrderBy(o => o.ObjectId));
        result.PlayerMoney.AddRange(money);
        return result;
    }

    private void ApplyGlobalAtOrBefore(uint frame, List<uint> money)
    {
        for (var i = _globals.Count - 1; i >= 0; i--)
        {
            if (_globals[i].Frame <= frame)
            {
                var global = ReadGlobal(_globals[i].Offset);
                money.AddRange(global.PlayerMoney);
                return;
            }
        }
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
                    case RegionIndexedFormat.SchemaHeaderTag:
                        var header = new RegionSchemaHeader();
                        reader.PersistObject(header, "RegionSchemaHeader");
                        if (header.SchemaVersion != RegionIndexedFormat.CurrentSchemaVersion)
                        {
                            throw new InvalidOperationException(
                                $"Region schema version {header.SchemaVersion} is not understood by this reader (understands {RegionIndexedFormat.CurrentSchemaVersion}).");
                        }

                        RegionSchemaId = header.RegionSchemaId;
                        break;
                    case RegionIndexedFormat.RegionSnapshotTag:
                        var regionSnapshot = new RegionSnapshotRecord();
                        reader.PersistObject(regionSnapshot, "RegionSnapshot");
                        _regionSnapshots.Add((offset, regionSnapshot.Frame, regionSnapshot.RegionKey));
                        _knownRegions.Add(regionSnapshot.RegionKey);
                        break;
                    case RegionIndexedFormat.RegionDeltaTag:
                        var regionDelta = new RegionDeltaRecord();
                        reader.PersistObject(regionDelta, "RegionDelta");
                        _regionDeltas.Add((offset, regionDelta.Frame, regionDelta.RegionKey));
                        _knownRegions.Add(regionDelta.RegionKey);
                        break;
                    case RegionIndexedFormat.FrameGlobalTag:
                        var global = new FrameGlobalRecord();
                        reader.PersistObject(global, "FrameGlobal");
                        _globals.Add((offset, global.Frame));
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
        var regionSnapshotFrame = _regionSnapshots.Count > 0 ? _regionSnapshots[^1].Frame : 0;
        var regionDeltaFrame = _regionDeltas.Count > 0 ? _regionDeltas[^1].Frame : 0;
        var globalFrame = _globals.Count > 0 ? _globals[^1].Frame : 0;
        return Math.Max(Math.Max(snapshotFrame, deltaFrame), Math.Max(Math.Max(regionSnapshotFrame, regionDeltaFrame), globalFrame));
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

    private RegionSnapshotRecord ReadRegionSnapshot(long offset)
    {
        _stream.Position = offset;
        using var reader = new StateReader(_stream, _sageGame);
        var tag = byte.MaxValue;
        reader.PersistByte(ref tag, "RecordType");
        var record = new RegionSnapshotRecord();
        reader.PersistObject(record, "RegionSnapshot");
        return record;
    }

    private RegionDeltaRecord ReadRegionDelta(long offset)
    {
        _stream.Position = offset;
        using var reader = new StateReader(_stream, _sageGame);
        var tag = byte.MaxValue;
        reader.PersistByte(ref tag, "RecordType");
        var record = new RegionDeltaRecord();
        reader.PersistObject(record, "RegionDelta");
        return record;
    }

    private FrameGlobalRecord ReadGlobal(long offset)
    {
        _stream.Position = offset;
        using var reader = new StateReader(_stream, _sageGame);
        var tag = byte.MaxValue;
        reader.PersistByte(ref tag, "RecordType");
        var record = new FrameGlobalRecord();
        reader.PersistObject(record, "FrameGlobal");
        return record;
    }

    public void Dispose() => _stream.Dispose();
}
