namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiTriShape at 10.1.0.0: a triangle mesh node. Geometry lives in the
/// referenced NiTriShapeData block.
/// </summary>
public sealed record NifTriShape : NifTriBasedGeometry
{
    private NifTriShape(NifBinaryReader reader)
        : base(reader)
    {
    }

    internal static NifTriShape Read(NifBinaryReader reader)
    {
        return new NifTriShape(reader);
    }
}
