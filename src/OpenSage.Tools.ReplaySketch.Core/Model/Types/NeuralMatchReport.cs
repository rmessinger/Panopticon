#nullable enable

using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>One evaluated match within a generation report.</summary>
public sealed class NeuralMatchReport
{
    public uint Seed { get; set; }

    public double? Fitness { get; set; }

    public string? Outcome { get; set; }

    public uint? Frames { get; set; }

    public bool Failed { get; set; }

    public string? Error { get; set; }
}
