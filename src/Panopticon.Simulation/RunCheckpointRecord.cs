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

/// <summary>One engine checkpoint in an operational bundle manifest (WI-A1-21).</summary>
public sealed class RunCheckpointRecord
{
    public uint Frame { get; set; }
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public string XxHash3 { get; set; } = "";
}
