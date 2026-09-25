namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiTriBasedGeom at 10.1.0.0 adds no fields over NiGeometry; it exists in
/// the inheritance chain so NiTriShape can dispatch through it.
/// </summary>
public abstract record NifTriBasedGeometry : NifGeometry
{
    protected NifTriBasedGeometry(NifBinaryReader reader)
        : base(reader)
    {
    }
}
