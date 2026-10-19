#nullable enable annotations

using System;
using System.Collections.Generic;
using Panopticon.IO;

namespace Panopticon.Recording;

/// <summary>WI-A5-29: the part of one frame's delta that belongs to one region.</summary>
public sealed class RegionDeltaRecord : IPersistableObject
{
    public uint Frame { get; set; }
    public string RegionKey { get; set; } = "";
    public List<WorldObjectState> Added { get; } = new();
    public List<uint> Removed { get; } = new();
    public List<WorldObjectState> Changed { get; } = new();

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var frame = Frame;
        persister.PersistUInt32(ref frame, "Frame");
        Frame = frame;

        var key = RegionKey;
        persister.PersistAsciiString(ref key, "RegionKey");
        RegionKey = key;

        PersistList(persister, Added, "Added");
        PersistIdList(persister, Removed, "Removed");
        PersistList(persister, Changed, "Changed");
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

    private static void PersistIdList(StatePersister persister, List<uint> list, string name)
    {
        var count = (uint)list.Count;
        persister.PersistUInt32(ref count, name + "Count");
        if (persister.Mode == StatePersistMode.Read)
        {
            list.Clear();
            for (var i = 0; i < count; i++)
            {
                var id = 0u;
                persister.PersistUInt32(ref id, name);
                list.Add(id);
            }
        }
        else
        {
            foreach (var id in list)
            {
                var value = id;
                persister.PersistUInt32(ref value, name);
            }
        }
    }
}
