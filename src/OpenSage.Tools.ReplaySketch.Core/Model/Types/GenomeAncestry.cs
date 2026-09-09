#nullable enable

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>Ancestry of one next-generation genome: its parent (or itself for elites).</summary>
public sealed record GenomeAncestry(int Index, int ParentIndex, bool Elite);
