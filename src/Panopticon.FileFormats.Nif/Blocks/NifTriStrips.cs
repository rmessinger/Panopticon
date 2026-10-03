namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiTriStrips at 10.1.0.0: a triangle-strip mesh node. Geometry layout
/// matches NiGeometry; the referenced data block is a NiTriStripsData.
/// </summary>
public sealed record NifTriStrips : NifTriBasedGeometry
{
    private NifTriStrips(NifBinaryReader reader)
        : base(reader)
    {
    }

    internal static NifTriStrips Read(NifBinaryReader reader)
    {
        return new NifTriStrips(reader);
    }
}
