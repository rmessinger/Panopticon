namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiParticles at 10.1.0.0: a legacy particle geometry node. Geometry
/// layout matches NiGeometry (data, skin, shader); the referenced data
/// block is a NiParticlesData.
/// </summary>
public sealed record NifParticles : NifGeometry
{
    private NifParticles(NifBinaryReader reader)
        : base(reader)
    {
    }

    internal static NifParticles Read(NifBinaryReader reader)
    {
        return new NifParticles(reader);
    }
}
