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
