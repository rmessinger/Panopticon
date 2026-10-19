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
