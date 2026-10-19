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

public static class WorldStateArtifact
{
    /// <summary>
    /// Loads a snapshot/delta artifact written by <see cref="WorldStateArtifactWriter"/>.
    /// Records appear in write order; reconstruction works over the ordered lists.
    /// </summary>
    public static (List<WorldStateSummary> Snapshots, List<WorldStateDelta> Deltas) Load(string path, IGame game)
    {
        var snapshots = new List<WorldStateSummary>();
        var deltas = new List<WorldStateDelta>();

        // FileShare.ReadWrite: artifacts are flushed per record, so loading is
        // legal while the owning recorder still holds the write stream (Windows
        // refuses an exclusive-mode open in that state). The persister leaves
        // the stream open, so dispose it explicitly; a leaked handle would keep
        // the file locked on Windows until finalization.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StateReader(stream, game);

        // WI-A5-29: region-indexed records merge back into whole-frame lists so every
        // existing consumer works unchanged over v2 artifacts.
        WorldStateSummary? pendingSnapshot = null;
        WorldStateDelta? pendingDelta = null;

        void FlushPending()
        {
            if (pendingSnapshot is not null)
            {
                snapshots.Add(pendingSnapshot);
                pendingSnapshot = null;
            }

            if (pendingDelta is not null)
            {
                deltas.Add(pendingDelta);
                pendingDelta = null;
            }
        }

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var tag = byte.MaxValue;
            reader.PersistByte(ref tag, "RecordType");
            switch (tag)
            {
                case 1:
                    FlushPending();
                    var snapshot = new WorldStateSummary();
                    reader.PersistObject(snapshot, "Snapshot");
                    snapshots.Add(snapshot);
                    break;
                case 2:
                    FlushPending();
                    var delta = new WorldStateDelta();
                    reader.PersistObject(delta, "Delta");
                    deltas.Add(delta);
                    break;
                case RegionIndexedFormat.SchemaHeaderTag:
                    var header = new RegionSchemaHeader();
                    reader.PersistObject(header, "RegionSchemaHeader");
                    if (header.SchemaVersion != RegionIndexedFormat.CurrentSchemaVersion)
                    {
                        throw new InvalidOperationException(
                            $"Region schema version {header.SchemaVersion} is not understood by this reader (understands {RegionIndexedFormat.CurrentSchemaVersion}).");
                    }

                    break;
                case RegionIndexedFormat.RegionSnapshotTag:
                    pendingDelta = null;
                    var regionSnapshot = new RegionSnapshotRecord();
                    reader.PersistObject(regionSnapshot, "RegionSnapshot");
                    if (pendingSnapshot is null || pendingSnapshot.Frame != regionSnapshot.Frame)
                    {
                        FlushPending();
                        pendingSnapshot = new WorldStateSummary { Frame = regionSnapshot.Frame };
                    }

                    pendingSnapshot.Objects.AddRange(regionSnapshot.Objects);
                    break;
                case RegionIndexedFormat.RegionDeltaTag:
                    pendingSnapshot = null;
                    var regionDelta = new RegionDeltaRecord();
                    reader.PersistObject(regionDelta, "RegionDelta");
                    if (pendingDelta is null || pendingDelta.Frame != regionDelta.Frame)
                    {
                        if (pendingDelta is not null)
                        {
                            deltas.Add(pendingDelta);
                        }

                        pendingDelta = new WorldStateDelta { Frame = regionDelta.Frame };
                    }

                    pendingDelta.Added.AddRange(regionDelta.Added);
                    pendingDelta.Removed.AddRange(regionDelta.Removed);
                    pendingDelta.Changed.AddRange(regionDelta.Changed);
                    break;
                case RegionIndexedFormat.FrameGlobalTag:
                    var global = new FrameGlobalRecord();
                    reader.PersistObject(global, "FrameGlobal");
                    if (pendingSnapshot is not null && pendingSnapshot.Frame == global.Frame)
                    {
                        pendingSnapshot.PlayerMoney.AddRange(global.PlayerMoney);
                    }
                    else if (pendingDelta is not null && pendingDelta.Frame == global.Frame)
                    {
                        pendingDelta.PlayerMoney.AddRange(global.PlayerMoney);
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unknown artifact record type {tag} at offset {reader.BaseStream.Position - 1}.");
            }
        }

        FlushPending();

        return (snapshots, deltas);
    }

    /// <summary>
    /// Reconstructs the world state at <paramref name="frame"/> from the latest snapshot at or
    /// before it plus the deltas that follow, up to and including the frame.
    /// </summary>
    public static WorldStateSummary Reconstruct(
        IReadOnlyList<WorldStateSummary> snapshots,
        IReadOnlyList<WorldStateDelta> deltas,
        uint frame)
    {
        var baselineIndex = -1;
        for (var i = 0; i < snapshots.Count; i++)
        {
            if (snapshots[i].Frame <= frame)
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

        var baseline = snapshots[baselineIndex];
        var objects = new Dictionary<uint, WorldObjectState>();
        foreach (var obj in baseline.Objects)
        {
            objects[obj.ObjectId] = obj;
        }

        var money = new List<uint>(baseline.PlayerMoney);

        foreach (var delta in deltas)
        {
            if (delta.Frame <= baseline.Frame)
            {
                continue;
            }

            if (delta.Frame > frame)
            {
                break;
            }

            delta.Apply(objects, money);
        }

        var result = new WorldStateSummary { Frame = frame };
        result.Objects.AddRange(objects.Values.OrderBy(o => o.ObjectId));
        result.PlayerMoney.AddRange(money);
        return result;
    }
}
