using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// A float RGBA color as stored in NiColorData keys and particle
/// controller fields.
/// </summary>
public readonly record struct NifColor4(float R, float G, float B, float A)
{
    public static NifColor4 White { get; } = new(1f, 1f, 1f, 1f);

    public Vector4 ToVector4()
    {
        return new Vector4(R, G, B, A);
    }
}

/// <summary>
/// NiColorData at 10.1.0.0: one RGBA key group consumed by
/// NiParticleColorModifier chains.
/// </summary>
public sealed record NifColorData : NifBlock
{
    public NifKeyGroup<NifKey<NifColor4>>? Keys { get; }

    private NifColorData(NifBinaryReader reader)
    {
        Keys = NifKeyGroupReader.ReadColor4Group(reader, "NiColorData", "Keys");
    }

    internal static NifColorData Read(NifBinaryReader reader)
    {
        return new NifColorData(reader);
    }
}
