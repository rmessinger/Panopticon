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

public sealed class WorldStateDeltaRecord
{
    public uint Frame { get; set; }
    public List<WorldObjectStateRecord> Added { get; set; } = new();
    public List<uint> Removed { get; set; } = new();
    public List<WorldObjectStateRecord> Changed { get; set; } = new();
    public List<uint> PlayerMoney { get; set; } = new();
}
