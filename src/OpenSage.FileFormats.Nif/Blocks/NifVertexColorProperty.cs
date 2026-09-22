using System.IO;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiVertexColorProperty at 10.1.0.0: vertex and lighting mode. The flags
/// word reads 0xCDCD in both C4a targets (uninitialized writer bytes), so
/// it is preserved verbatim without interpretation.
/// </summary>
public sealed record NifVertexColorProperty : NifProperty
{
    public ushort Flags { get; }
    public uint VertexMode { get; }
    public uint LightingMode { get; }

    private NifVertexColorProperty(NifBinaryReader reader)
        : base(reader)
    {
        Flags = reader.ReadUInt16("Flags");
        VertexMode = reader.ReadUInt32("VertexMode");
        if (VertexMode > 2)
        {
            throw new InvalidDataException(
                $"NIF vertex color property has vertex mode {VertexMode}; valid range is 0-2.");
        }

        LightingMode = reader.ReadUInt32("LightingMode");
        if (LightingMode > 1)
        {
            throw new InvalidDataException(
                $"NIF vertex color property has lighting mode {LightingMode}; valid range is 0-1.");
        }
    }

    internal static NifVertexColorProperty Read(NifBinaryReader reader)
    {
        return new NifVertexColorProperty(reader);
    }
}
