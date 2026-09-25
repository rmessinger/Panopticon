namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiLODNode at 10.1.0.0: a switch node whose ranges live in a referenced
/// NiRangeLODData block. Children and ranges are retained verbatim for
/// diagnostics; selection is a query, never a mutation.
/// </summary>
public sealed record NifLodNode : NifSwitchNode
{
    public int LodDataReference { get; }

    private NifLodNode(NifBinaryReader reader)
        : base(reader)
    {
        LodDataReference = reader.ReadBlockReference("LODLevelData");
    }

    internal static NifLodNode Read(NifBinaryReader reader)
    {
        return new NifLodNode(reader);
    }
}
