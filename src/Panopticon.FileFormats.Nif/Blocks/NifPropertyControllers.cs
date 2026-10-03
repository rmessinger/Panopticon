namespace Panopticon.FileFormats.Nif;

/// <summary>
/// The time-controller prefix shared by every NIF controller at 10.1.0.0:
/// next-controller reference, flags, frequency, phase, start/stop times,
/// and the target reference. EE2 KF files use target -1; the real target
/// is named in the controller sequence's controlled-block table.
/// </summary>
public abstract record NifTimeController : NifBlock
{
    public int NextControllerReference { get; }
    public ushort Flags { get; }
    public float Frequency { get; }
    public float Phase { get; }
    public float StartTime { get; }
    public float StopTime { get; }
    public int TargetReference { get; }

    protected NifTimeController(NifBinaryReader reader)
    {
        NextControllerReference = reader.ReadBlockReference("NextController");
        Flags = reader.ReadUInt16("Flags");
        Frequency = reader.ReadSingle("Frequency");
        Phase = reader.ReadSingle("Phase");
        StartTime = reader.ReadSingle("StartTime");
        StopTime = reader.ReadSingle("StopTime");
        TargetReference = reader.ReadBlockReference("Target");
    }
}

/// <summary>
/// NiTextureTransformController operations. Values are the wire encodings.
/// EE2 files only use the translate operations; the parser keeps every
/// value verbatim and conversion rejects the unsupported ones.
/// </summary>
public enum NifTextureTransformOp : uint
{
    TranslateU = 0,
    TranslateV = 1,
    Rotate = 2,
    ScaleU = 3,
    ScaleV = 4,
}

/// <summary>
/// NiMaterialColorController color targets. Values are the wire encodings.
/// </summary>
public enum NifMaterialColorTarget : ushort
{
    Ambient = 0,
    Diffuse = 1,
    Specular = 2,
    Emissive = 3,
}

/// <summary>
/// NiVisController at 10.1.0.0: time-controller fields plus a reference to
/// NiVisData (or NiBoolData, the replacement form). Flags are preserved
/// verbatim; EE2 files mix 0x2C, 0x25, and 0x21.
/// </summary>
public sealed record NifVisController : NifTimeController
{
    public int DataReference { get; }

    private NifVisController(NifBinaryReader reader)
        : base(reader)
    {
        DataReference = reader.ReadBlockReference("Data");
    }

    internal static NifVisController Read(NifBinaryReader reader)
    {
        return new NifVisController(reader);
    }
}

/// <summary>
/// NiUVController: time-controller fields plus a reference to NiUVData.
/// The legacy 10.0.1.0 KF census carries one such clip; conversion leaves
/// UV animation to C4e, so the parser only preserves the reference.
/// </summary>
public sealed record NifUVController : NifTimeController
{
    public int DataReference { get; }

    private NifUVController(NifBinaryReader reader)
        : base(reader)
    {
        DataReference = reader.ReadBlockReference("Data");
    }

    internal static NifUVController Read(NifBinaryReader reader)
    {
        return new NifUVController(reader);
    }
}

/// <summary>
/// NiAlphaController at 10.1.0.0: time-controller fields plus a reference
/// to NiFloatData. EE2 files use flags 0x28 with quadratic keys.
/// </summary>
public sealed record NifAlphaController : NifTimeController
{
    public int DataReference { get; }

    private NifAlphaController(NifBinaryReader reader)
        : base(reader)
    {
        DataReference = reader.ReadBlockReference("Data");
    }

    internal static NifAlphaController Read(NifBinaryReader reader)
    {
        return new NifAlphaController(reader);
    }
}

/// <summary>
/// NiTextureTransformController at 10.1.0.0: time-controller fields, a
/// shader-map byte, a texture slot, an operation, and a NiFloatData
/// reference. EE2 files use shader map false, slot 0, and translate
/// operations only.
/// </summary>
public sealed record NifTextureTransformController : NifTimeController
{
    public bool ShaderMap { get; }
    public uint TextureSlot { get; }
    public NifTextureTransformOp Operation { get; }
    public int DataReference { get; }

    private NifTextureTransformController(NifBinaryReader reader)
        : base(reader)
    {
        ShaderMap = reader.ReadByte("ShaderMap") != 0;
        TextureSlot = reader.ReadUInt32("TextureSlot");
        Operation = (NifTextureTransformOp)reader.ReadUInt32("Operation");
        DataReference = reader.ReadBlockReference("Data");
    }

    internal static NifTextureTransformController Read(NifBinaryReader reader)
    {
        return new NifTextureTransformController(reader);
    }
}

/// <summary>
/// NiMaterialColorController at 10.1.0.0: time-controller fields, a color
/// target, and a NiPosData reference. EE2 files use flags 0x2C with
/// quadratic keys; targets are ambient or specular.
/// </summary>
public sealed record NifMaterialColorController : NifTimeController
{
    public NifMaterialColorTarget TargetColor { get; }
    public int DataReference { get; }

    private NifMaterialColorController(NifBinaryReader reader)
        : base(reader)
    {
        TargetColor = (NifMaterialColorTarget)reader.ReadUInt16("TargetColor");
        DataReference = reader.ReadBlockReference("Data");
    }

    internal static NifMaterialColorController Read(NifBinaryReader reader)
    {
        return new NifMaterialColorController(reader);
    }
}
