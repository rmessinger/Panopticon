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

public sealed class TriggeredDetailWindowRecord
{
    public bool Marked { get; set; }
    public uint TriggeredAtFrame { get; set; }
    public uint StartFrame { get; set; }
    public uint EndFrame { get; set; }
    public List<string>? DivergedSubtrees { get; set; }
    public List<WorldStateDeltaRecord> Frames { get; set; } = new();

    /// <summary>WI-A5-21: trigger reason ("divergence" or "marked").</summary>
    public string Reason { get; set; } = "divergence";

    /// <summary>WI-A5-21: budgets this window was captured under.</summary>
    public int RingDepth { get; set; }
    public int MaxWindows { get; set; }
    public uint CooldownFrames { get; set; }

    /// <summary>WI-A5-21: region keys the frames were filtered to; null when unfiltered.</summary>
    public List<string>? RegionKeys { get; set; }

    /// <summary>WI-A5-21: distinct entity ids appearing across the captured frames.</summary>
    public int CapturedEntityCount { get; set; }
}
