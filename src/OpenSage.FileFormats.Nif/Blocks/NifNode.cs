namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiNode at 10.1.0.0: child and effect reference lists. Null (-1)
/// child references are preserved verbatim.
/// </summary>
public record NifNode : NifAvObject
{
    public int[] ChildReferences { get; }
    public int[] EffectReferences { get; }

    protected NifNode(NifBinaryReader reader)
        : base(reader)
    {
        ChildReferences = reader.ReadBlockReferenceList("Children");
        EffectReferences = reader.ReadBlockReferenceList("Effects");
    }

    internal static NifNode Read(NifBinaryReader reader)
    {
        return new NifNode(reader);
    }

}
