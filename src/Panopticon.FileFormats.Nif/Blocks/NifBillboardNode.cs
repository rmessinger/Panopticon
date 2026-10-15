namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiBillboardNode at 10.1.0.0: a NiNode plus one billboard-mode word.
/// Billboard orientation is a rendering concern the importer ignores; the
/// node participates in the hierarchy like any other node.
/// </summary>
public sealed record NifBillboardNode : NifNode
{
    public ushort Mode { get; }

    private NifBillboardNode(NifBinaryReader reader)
        : base(reader)
    {
        Mode = reader.ReadUInt16("BillboardMode");
    }

    internal static new NifBillboardNode Read(NifBinaryReader reader)
    {
        return new NifBillboardNode(reader);
    }
}
