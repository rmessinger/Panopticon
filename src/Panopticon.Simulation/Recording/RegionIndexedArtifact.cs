#nullable enable annotations

using System;
using System.Collections.Generic;
using Panopticon.IO;

namespace Panopticon.Recording;

/// <summary>
/// WI-A5-29: record tags for the region-indexed state artifact (v2). A v2 artifact starts with
/// a <see cref="RegionSchemaHeader"/> (tag 0); snapshots become one
/// <see cref="RegionSnapshotRecord"/> (tag 5) per region plus a <see cref="FrameGlobalRecord"/>
/// (tag 4); deltas become one <see cref="RegionDeltaRecord"/> (tag 3) per active region plus a
/// tag-4 global. Whole-frame reconstruction unions the region records of a frame; region queries
/// decode only the requested regions' records.
/// </summary>
public static class RegionIndexedFormat
{
    public const byte SchemaHeaderTag = 0;
    public const byte RegionDeltaTag = 3;
    public const byte FrameGlobalTag = 4;
    public const byte RegionSnapshotTag = 5;

    /// <summary>Bump to invalidate older readers explicitly.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Default region schema: the domain's TopologyKey vocabulary.</summary>
    public const string DefaultSchemaId = "topology-key/1";
}

/// <summary>WI-A5-29: leading record of a region-indexed artifact; readers reject unknown versions.</summary>
public sealed class RegionSchemaHeader : IPersistableObject
{
    public int SchemaVersion { get; set; }
    public string RegionSchemaId { get; set; } = "";

    public void Persist(StatePersister persister)
    {
        persister.PersistVersion(1);

        var schemaVersion = SchemaVersion;
        persister.PersistInt32(ref schemaVersion, "SchemaVersion");
        SchemaVersion = schemaVersion;

        var schemaId = RegionSchemaId;
        persister.PersistAsciiString(ref schemaId, "RegionSchemaId");
        RegionSchemaId = schemaId;
    }
}

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
