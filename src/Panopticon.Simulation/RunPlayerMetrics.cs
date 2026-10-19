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

/// <summary>
/// Per-player metrics captured in one sample. Only counters that already exist on
/// <c>Player</c> and <c>GameLogic</c>; this tier adds no gameplay counters.
/// </summary>
public sealed class RunPlayerMetrics
{
    public string? Player { get; set; }
    public uint Money { get; set; }
    public int OwnedObjects { get; set; }
}
