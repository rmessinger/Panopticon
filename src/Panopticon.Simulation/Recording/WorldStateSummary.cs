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
