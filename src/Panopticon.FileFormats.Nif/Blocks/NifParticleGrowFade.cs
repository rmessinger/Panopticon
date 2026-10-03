namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiParticleGrowFade at 10.1.0.0: scales particles up over grow time
/// and down over fade time, both in seconds from spawn.
/// </summary>
public sealed record NifParticleGrowFade : NifParticleModifier
{
    public float GrowTime { get; }
    public float FadeTime { get; }

    private NifParticleGrowFade(NifBinaryReader reader)
        : base(reader)
    {
        GrowTime = reader.ReadSingle("GrowTime");
        FadeTime = reader.ReadSingle("FadeTime");
    }

    internal static NifParticleGrowFade Read(NifBinaryReader reader)
    {
        return new NifParticleGrowFade(reader);
    }
}
