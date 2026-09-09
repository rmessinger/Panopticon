#nullable enable

using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>Ancestry of one next-generation genome.</summary>
public sealed class NeuralNextGenomeReport
{
    public int Index { get; set; }

    public int ParentIndex { get; set; }

    public bool Elite { get; set; }
}
