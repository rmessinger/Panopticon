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
/// T1 recording configuration as written into the run manifest (WI-A5-07): quantization
/// tolerances and the bucket-key strategy the trees were built with, so comparison surfaces
/// can reject mismatched runs before reporting meaningless divergence.
/// </summary>
public sealed class RunSignatureConfigurationRecord
{
    public float PositionEpsilon { get; set; }
    public float HealthEpsilon { get; set; }
    public bool Strict { get; set; }
    public float CellSize { get; set; }

    /// <summary>Bucket-key strategy: <c>domain</c> keys from the topology contract or <c>legacy-spatial</c>.</summary>
    public string BucketKeys { get; set; } = "domain";
}
