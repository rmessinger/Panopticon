namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiParticleColorModifier at 10.1.0.0: links the controller to a
/// NiColorData key group driving particle color over lifetime.
/// </summary>
public sealed record NifParticleColorModifier : NifParticleModifier
{
    public int ColorDataReference { get; }

    private NifParticleColorModifier(NifBinaryReader reader)
        : base(reader)
    {
        ColorDataReference = reader.ReadBlockReference("ColorData");
    }

    internal static NifParticleColorModifier Read(NifBinaryReader reader)
    {
        return new NifParticleColorModifier(reader);
    }
}
