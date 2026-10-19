#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.IO;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Streaming artifact writer for snapshot/delta recording (WI-A1-11): a binary file of
/// type-tagged records using the save-format <see cref="StatePersister"/> primitives, written
/// live so a crash still leaves a usable prefix. Read back with <see cref="Load"/>.
/// </summary>
public sealed class WorldStateArtifactWriter : DisposableBase
{
    private const byte SnapshotRecord = 1;
    private const byte DeltaRecord = 2;

    private readonly FileStream _stream;
    private readonly StateWriter _writer;
    private Func<WorldObjectState, string>? _regionClassifier;
    private readonly Dictionary<uint, WorldObjectState> _lastKnown = new();

    /// <summary>WI-A5-29: region keys seen so far, in first-write order.</summary>
    public IReadOnlyCollection<string> WrittenRegions { get; } = new List<string>();

    private List<string> WrittenRegionList => (List<string>)WrittenRegions;

    public WorldStateArtifactWriter(string path, IGame game, Func<WorldObjectState, string>? regionClassifier = null, string? regionSchemaId = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // FileShare.Read keeps the artifact loadable while the recorder holds
        // the write stream; disposal order (reverse registration) closes the
        // writer before the stream so the handle is released on Windows.
        _stream = AddDisposable(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
        _writer = AddDisposable(new StateWriter(_stream, game));
        InitializeRegionMode(regionClassifier, regionSchemaId);
    }

    /// <summary>WI-A5-04: writes summary artifacts without a booted engine game.</summary>
    public WorldStateArtifactWriter(string path, SageGame sageGame, Func<WorldObjectState, string>? regionClassifier = null, string? regionSchemaId = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _stream = AddDisposable(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
        _writer = AddDisposable(new StateWriter(_stream, sageGame));
        InitializeRegionMode(regionClassifier, regionSchemaId);
    }

    private void InitializeRegionMode(Func<WorldObjectState, string>? regionClassifier, string? regionSchemaId)
    {
        _regionClassifier = regionClassifier;
        if (regionClassifier is null)
        {
            return;
        }

        var header = new RegionSchemaHeader
        {
            SchemaVersion = RegionIndexedFormat.CurrentSchemaVersion,
            RegionSchemaId = string.IsNullOrEmpty(regionSchemaId) ? RegionIndexedFormat.DefaultSchemaId : regionSchemaId,
        };
        var tag = RegionIndexedFormat.SchemaHeaderTag;
        _writer.PersistByte(ref tag, "RecordType");
        _writer.PersistObject(header, "RegionSchemaHeader");
        _writer.BaseStream.Flush();
    }

    public void WriteSnapshot(WorldStateSummary summary)
    {
        if (_regionClassifier is null)
        {
            var tag = SnapshotRecord;
            _writer.PersistByte(ref tag, "RecordType");
            _writer.PersistObject(summary, "Snapshot");
            _writer.BaseStream.Flush();
            return;
        }

        // WI-A5-29: one region part per key plus the frame global; also resyncs the
        // last-known map that classifies removals in later deltas.
        _lastKnown.Clear();
        var byRegion = new Dictionary<string, RegionSnapshotRecord>();
        foreach (var obj in summary.Objects)
        {
            var key = _regionClassifier(obj);
            if (!byRegion.TryGetValue(key, out var part))
            {
                part = new RegionSnapshotRecord { Frame = summary.Frame, RegionKey = key };
                byRegion[key] = part;
                WrittenRegionList.Add(key);
            }

            part.Objects.Add(obj);
            _lastKnown[obj.ObjectId] = obj;
        }

        foreach (var part in byRegion.Values)
        {
            var partTag = RegionIndexedFormat.RegionSnapshotTag;
            _writer.PersistByte(ref partTag, "RecordType");
            _writer.PersistObject(part, "RegionSnapshot");
        }

        WriteFrameGlobal(summary.Frame, summary.PlayerMoney);
    }

    public void WriteDelta(WorldStateDelta delta)
    {
        if (_regionClassifier is null)
        {
            var tag = DeltaRecord;
            _writer.PersistByte(ref tag, "RecordType");
            _writer.PersistObject(delta, "Delta");
            _writer.BaseStream.Flush();
            return;
        }

        // WI-A5-29: one region part per key that has any change. Removals carry only an id and
        // are classified through the last-known map maintained here. An entity that changed
        // region is written as Removed(old) + Added(new) so each region's parts are
        // self-contained: a region query never needs other regions' records.
        var byRegion = new Dictionary<string, RegionDeltaRecord>();
        foreach (var added in delta.Added)
        {
            var key = Classify(added);
            Part(key).Added.Add(added);
            _lastKnown[added.ObjectId] = added;
        }

        foreach (var changed in delta.Changed)
        {
            var newKey = Classify(changed);
            if (_lastKnown.TryGetValue(changed.ObjectId, out var last) && _regionClassifier(last) != newKey)
            {
                Part(_regionClassifier(last)).Removed.Add(changed.ObjectId);
                Part(newKey).Added.Add(changed);
            }
            else
            {
                Part(newKey).Changed.Add(changed);
            }

            _lastKnown[changed.ObjectId] = changed;
        }

        foreach (var removed in delta.Removed)
        {
            var key = _lastKnown.TryGetValue(removed, out var last) ? _regionClassifier(last) : "";
            if (!byRegion.TryGetValue(key, out var part))
            {
                part = new RegionDeltaRecord { Frame = delta.Frame, RegionKey = key };
                byRegion[key] = part;
                if (key.Length > 0)
                {
                    WrittenRegionList.Add(key);
                }
            }

            part.Removed.Add(removed);
            _lastKnown.Remove(removed);
        }

        foreach (var part in byRegion.Values)
        {
            var partTag = RegionIndexedFormat.RegionDeltaTag;
            _writer.PersistByte(ref partTag, "RecordType");
            _writer.PersistObject(part, "RegionDelta");
        }

        WriteFrameGlobal(delta.Frame, delta.PlayerMoney);

        RegionDeltaRecord Part(string key)
        {
            if (!byRegion.TryGetValue(key, out var existing))
            {
                existing = new RegionDeltaRecord { Frame = delta.Frame, RegionKey = key };
                byRegion[key] = existing;
                if (key.Length > 0)
                {
                    WrittenRegionList.Add(key);
                }
            }

            return existing;
        }
    }

    private string Classify(WorldObjectState obj)
    {
        var key = _regionClassifier!(obj);
        if (!WrittenRegionList.Contains(key))
        {
            WrittenRegionList.Add(key);
        }

        return key;
    }

    private void WriteFrameGlobal(uint frame, List<uint> playerMoney)
    {
        var global = new FrameGlobalRecord { Frame = frame };
        global.PlayerMoney.AddRange(playerMoney);
        var tag = RegionIndexedFormat.FrameGlobalTag;
        _writer.PersistByte(ref tag, "RecordType");
        _writer.PersistObject(global, "FrameGlobal");
        _writer.BaseStream.Flush();
    }
}
