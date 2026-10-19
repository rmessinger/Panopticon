#nullable enable annotations

using System;
using System.Collections.Generic;
using Panopticon.IO;

namespace Panopticon.Recording;

/// <summary>WI-A5-29: the objects of one region at a snapshot frame.</summary>
public sealed class RegionSnapshotRecord : IPersistableObject
{
    public uint Frame { get; set; }
    public string RegionKey { get; set; } = "";
    public List<WorldObjectState> Objects { get; } = new();

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var frame = Frame;
        persister.PersistUInt32(ref frame, "Frame");
        Frame = frame;

        var key = RegionKey;
        persister.PersistAsciiString(ref key, "RegionKey");
        RegionKey = key;

        var count = (uint)Objects.Count;
        persister.PersistUInt32(ref count, "ObjectCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            Objects.Clear();
            for (var i = 0; i < count; i++)
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
    }
}
