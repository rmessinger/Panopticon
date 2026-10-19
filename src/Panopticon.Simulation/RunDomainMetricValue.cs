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

/// <summary>One value in a named, keyed domain metric series at one sample.</summary>
public sealed class RunDomainMetricValue
{
    public string Series { get; set; } = "";
    public string Key { get; set; } = "";
    public double Value { get; set; }
}
