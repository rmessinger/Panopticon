#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Panopticon.Logic;
using Panopticon.Recording;

namespace Panopticon;

public sealed class WorldObjectStateRecord
{
    public uint ObjectId { get; set; }
    public string DefinitionName { get; set; } = "";
    public int OwnerIndex { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; set; }
}
