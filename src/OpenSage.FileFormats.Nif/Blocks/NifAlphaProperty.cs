using System.IO;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiAlphaProperty at 10.1.0.0: alpha flags bitfield and threshold.
/// Flags are preserved verbatim; the importer maps them.
/// </summary>
public sealed record NifAlphaProperty : NifProperty
{
    public ushort Flags { get; }
    public byte Threshold { get; }

    private NifAlphaProperty(NifBinaryReader reader)
        : base(reader)
    {
        Flags = reader.ReadUInt16("Flags");
        Threshold = reader.ReadByte("Threshold");
    }

    internal static NifAlphaProperty Read(NifBinaryReader reader)
    {
        return new NifAlphaProperty(reader);
    }
}
