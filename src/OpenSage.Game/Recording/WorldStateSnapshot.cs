#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.IO;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Simulation;

namespace OpenSage.Recording;

/// <summary>
/// Comparable state of one world object at one instant: identity plus the quantities the tiered
/// recording (T1 signatures, T2 detail, snapshots) is defined over. Part of the A1-5 run
/// outputs; not a full save-format object blob (whole-object persistence needs live team and
/// map context and cannot round-trip standalone).
/// </summary>
public sealed class WorldObjectState : IPersistableObject
{
    public uint ObjectId { get; set; }
    public string DefinitionName { get; set; } = "";
    public int OwnerIndex { get; set; } = -1;
    public Vector3 Position { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; set; }

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var objectId = ObjectId;
        persister.PersistUInt32(ref objectId, "ObjectId");
        ObjectId = objectId;

        var definitionName = DefinitionName;
        persister.PersistAsciiString(ref definitionName, "DefinitionName");
        DefinitionName = definitionName;

        var ownerIndex = OwnerIndex;
        persister.PersistInt32(ref ownerIndex, "OwnerIndex");
        OwnerIndex = ownerIndex;

        var x = Position.X;
        var y = Position.Y;
        var z = Position.Z;
        persister.PersistSingle(ref x, "X");
        persister.PersistSingle(ref y, "Y");
        persister.PersistSingle(ref z, "Z");
        Position = new Vector3(x, y, z);

        var health = Health;
        var maxHealth = MaxHealth;
        persister.PersistSingle(ref health, "Health");
        persister.PersistSingle(ref maxHealth, "MaxHealth");
        Health = health;
        MaxHealth = maxHealth;
    }
}

/// <summary>
/// Comparable world state at one logic frame: per-object states sorted by object id plus the
/// per-player money vector. The unit all A1-5 tiers sample from.
/// </summary>
public sealed class WorldStateSummary : IPersistableObject
{
    public uint Frame { get; set; }
    public List<WorldObjectState> Objects { get; } = new();
    public List<uint> PlayerMoney { get; } = new();

    internal static WorldStateSummary Capture(ISimulationDomainModel domain, uint frame)
    {
        var summary = new WorldStateSummary { Frame = frame };
        foreach (var entity in domain.CaptureEntities())
        {
            summary.Objects.Add(new WorldObjectState
            {
                ObjectId = entity.EntityId,
                DefinitionName = entity.KindName,
                OwnerIndex = entity.OwnerIndex,
                Position = entity.Position,
                Health = entity.ScalarA,
                MaxHealth = entity.ScalarB,
            });
        }
        foreach (var money in domain.CapturePlayerMoneyVector())
        {
            summary.PlayerMoney.Add(money);
        }
        return summary;
    }

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var frame = Frame;
        persister.PersistUInt32(ref frame, "Frame");
        Frame = frame;

        var objectCount = (uint)Objects.Count;
        persister.PersistUInt32(ref objectCount, "ObjectCount");

        if (persister.Mode == StatePersistMode.Read)
        {
            Objects.Clear();
            for (var i = 0; i < objectCount; i++)
            {
                var obj = new WorldObjectState();
                persister.PersistObject(obj, "Object");
                Objects.Add(obj);
            }
        }
        else
        {
            foreach (var obj in Objects)
            {
                persister.PersistObject(obj, "Object");
            }
        }

        var playerCount = (uint)PlayerMoney.Count;
        persister.PersistUInt32(ref playerCount, "PlayerCount");

        if (persister.Mode == StatePersistMode.Read)
        {
            PlayerMoney.Clear();
            for (var i = 0; i < playerCount; i++)
            {
                var money = 0u;
                persister.PersistUInt32(ref money, "Money");
                PlayerMoney.Add(money);
            }
        }
        else
        {
            foreach (var money in PlayerMoney)
            {
                var value = money;
                persister.PersistUInt32(ref value, "Money");
            }
        }
    }
}

/// <summary>
/// Frame-to-frame change set between two consecutive <see cref="WorldStateSummary"/> samples:
/// objects added (full state), removed (id only), and changed (full new state), plus the full
/// money vector. Reconstruction is additive: apply deltas to a baseline snapshot.
/// </summary>
public sealed class WorldStateDelta : IPersistableObject
{
    public uint Frame { get; set; }
    public List<WorldObjectState> Added { get; } = new();
    public List<uint> Removed { get; } = new();
    public List<WorldObjectState> Changed { get; } = new();
    public List<uint> PlayerMoney { get; } = new();

    public static WorldStateDelta Diff(uint frame, WorldStateSummary previous, WorldStateSummary current)
    {
        var delta = new WorldStateDelta
        {
            Frame = frame,
        };
        delta.PlayerMoney.AddRange(current.PlayerMoney);

        // Both lists are sorted ascending by object id (GameLogic iterates id order).
        int i = 0, j = 0;
        while (i < previous.Objects.Count && j < current.Objects.Count)
        {
            var before = previous.Objects[i];
            var after = current.Objects[j];

            if (before.ObjectId == after.ObjectId)
            {
                if (HasChanged(before, after))
                {
                    delta.Changed.Add(after);
                }
                i++;
                j++;
            }
            else if (before.ObjectId < after.ObjectId)
            {
                delta.Removed.Add(before.ObjectId);
                i++;
            }
            else
            {
                delta.Added.Add(after);
                j++;
            }
        }
        while (i < previous.Objects.Count)
        {
            delta.Removed.Add(previous.Objects[i].ObjectId);
            i++;
        }
        while (j < current.Objects.Count)
        {
            delta.Added.Add(current.Objects[j]);
            j++;
        }

        return delta;
    }

    private static bool HasChanged(WorldObjectState before, WorldObjectState after) =>
        before.Position != after.Position
        || before.Health != after.Health
        || before.MaxHealth != after.MaxHealth
        || before.OwnerIndex != after.OwnerIndex
        || before.DefinitionName != after.DefinitionName;

    /// <summary>
    /// Applies this delta to a state dictionary being reconstructed.
    /// </summary>
    internal void Apply(Dictionary<uint, WorldObjectState> objects, List<uint> money)
    {
        foreach (var id in Removed)
        {
            objects.Remove(id);
        }
        foreach (var added in Added)
        {
            objects[added.ObjectId] = added;
        }
        foreach (var changed in Changed)
        {
            objects[changed.ObjectId] = changed;
        }
        if (PlayerMoney.Count > 0)
        {
            money.Clear();
            money.AddRange(PlayerMoney);
        }
    }

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var frame = Frame;
        persister.PersistUInt32(ref frame, "Frame");
        Frame = frame;

        PersistList(persister, Added, "Added");

        var removedCount = (uint)Removed.Count;
        persister.PersistUInt32(ref removedCount, "RemovedCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            Removed.Clear();
            for (var i = 0; i < removedCount; i++)
            {
                var id = 0u;
                persister.PersistUInt32(ref id, "Removed");
                Removed.Add(id);
            }
        }
        else
        {
            foreach (var id in Removed)
            {
                var value = id;
                persister.PersistUInt32(ref value, "Removed");
            }
        }

        PersistList(persister, Changed, "Changed");

        var moneyCount = (uint)PlayerMoney.Count;
        persister.PersistUInt32(ref moneyCount, "MoneyCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            PlayerMoney.Clear();
            for (var i = 0; i < moneyCount; i++)
            {
                var money = 0u;
                persister.PersistUInt32(ref money, "Money");
                PlayerMoney.Add(money);
            }
        }
        else
        {
            foreach (var money in PlayerMoney)
            {
                var value = money;
                persister.PersistUInt32(ref value, "Money");
            }
        }
    }

    private static void PersistList(StatePersister persister, List<WorldObjectState> list, string name)
    {
        var count = (uint)list.Count;
        persister.PersistUInt32(ref count, name + "Count");
        if (persister.Mode == StatePersistMode.Read)
        {
            list.Clear();
            for (var i = 0; i < count; i++)
            {
                var obj = new WorldObjectState();
                persister.PersistObject(obj, name);
                list.Add(obj);
            }
        }
        else
        {
            foreach (var obj in list)
            {
                persister.PersistObject(obj, name);
            }
        }
    }
}

/// <summary>
/// Streaming artifact writer for snapshot/delta recording (WI-A1-11): a binary file of
/// type-tagged records using the save-format <see cref="StatePersister"/> primitives, written
/// live so a crash still leaves a usable prefix. Read back with <see cref="Load"/>.
/// </summary>
public sealed class WorldStateArtifactWriter : DisposableBase
{
    private const byte SnapshotRecord = 1;
    private const byte DeltaRecord = 2;

    private readonly StateWriter _writer;

    public WorldStateArtifactWriter(string path, IGame game)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        _writer = AddDisposable(new StateWriter(new FileStream(path, FileMode.Create, FileAccess.Write), game));
        AddDisposeAction(_writer.Dispose);
    }

    public void WriteSnapshot(WorldStateSummary summary)
    {
        var tag = SnapshotRecord;
        _writer.PersistByte(ref tag, "RecordType");
        _writer.PersistObject(summary, "Snapshot");
        _writer.BaseStream.Flush();
    }

    public void WriteDelta(WorldStateDelta delta)
    {
        var tag = DeltaRecord;
        _writer.PersistByte(ref tag, "RecordType");
        _writer.PersistObject(delta, "Delta");
        _writer.BaseStream.Flush();
    }
}

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

        using var reader = new StateReader(new FileStream(path, FileMode.Open, FileAccess.Read), game);
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var tag = byte.MaxValue;
            reader.PersistByte(ref tag, "RecordType");
            switch (tag)
            {
                case 1:
                    var snapshot = new WorldStateSummary();
                    reader.PersistObject(snapshot, "Snapshot");
                    snapshots.Add(snapshot);
                    break;
                case 2:
                    var delta = new WorldStateDelta();
                    reader.PersistObject(delta, "Delta");
                    deltas.Add(delta);
                    break;
                default:
                    throw new InvalidDataException($"Unknown artifact record type {tag} at offset {reader.BaseStream.Position - 1}.");
            }
        }

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
