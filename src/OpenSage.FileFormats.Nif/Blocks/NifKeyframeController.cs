namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiKeyframeController at 10.1.0.0: time-controller fields plus a
/// keyframe-data reference. Flags are preserved verbatim; EE2 KF files use
/// 0x2C with frequency 1, phase 0, and null next/target references.
/// </summary>
public sealed record NifKeyframeController : NifBlock
{
    public int NextControllerReference { get; }
    public ushort Flags { get; }
    public float Frequency { get; }
    public float Phase { get; }
    public float StartTime { get; }
    public float StopTime { get; }
    public int TargetReference { get; }
    public int DataReference { get; }

    private NifKeyframeController(NifBinaryReader reader)
    {
        NextControllerReference = reader.ReadBlockReference("NextController");
        Flags = reader.ReadUInt16("Flags");
        Frequency = reader.ReadSingle("Frequency");
        Phase = reader.ReadSingle("Phase");
        StartTime = reader.ReadSingle("StartTime");
        StopTime = reader.ReadSingle("StopTime");
        TargetReference = reader.ReadBlockReference("Target");
        DataReference = reader.ReadBlockReference("Data");
    }

    internal static NifKeyframeController Read(NifBinaryReader reader)
    {
        return new NifKeyframeController(reader);
    }
}
