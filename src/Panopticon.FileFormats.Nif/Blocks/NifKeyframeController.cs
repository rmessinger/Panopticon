namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiKeyframeController at 10.1.0.0: time-controller fields plus a
/// keyframe-data reference. Flags are preserved verbatim; EE2 KF files use
/// 0x2C with frequency 1, phase 0, and null next/target references.
/// </summary>
public sealed record NifKeyframeController : NifTimeController
{
    public int DataReference { get; }

    private NifKeyframeController(NifBinaryReader reader)
        : base(reader)
    {
        DataReference = reader.ReadBlockReference("Data");
    }

    internal static NifKeyframeController Read(NifBinaryReader reader)
    {
        return new NifKeyframeController(reader);
    }
}
