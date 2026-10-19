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

public sealed class RunMetricSample
{
    public uint Frame { get; set; }
    public List<RunPlayerMetrics> Players { get; set; } = new();

    // A4-1 domain metric series rows (transport: edge throughput, node occupancy); the RTS
    // adapter emits none, so existing run outputs are unchanged.
    public List<RunDomainMetricValue> DomainMetrics { get; set; } = new();
}
