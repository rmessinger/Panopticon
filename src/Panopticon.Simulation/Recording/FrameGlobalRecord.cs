#nullable enable annotations

using System;
using System.Collections.Generic;
using Panopticon.IO;

namespace Panopticon.Recording;

/// <summary>WI-A5-29: per-frame whole-run data (the money vector) shared by all regions.</summary>
public sealed class FrameGlobalRecord : IPersistableObject
{
    public uint Frame { get; set; }
    public List<uint> PlayerMoney { get; } = new();

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var frame = Frame;
        persister.PersistUInt32(ref frame, "Frame");
        Frame = frame;

        var count = (uint)PlayerMoney.Count;
        persister.PersistUInt32(ref count, "PlayerCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            PlayerMoney.Clear();
            for (var i = 0; i < count; i++)
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
