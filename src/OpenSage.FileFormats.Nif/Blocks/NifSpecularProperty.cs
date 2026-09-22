using System.IO;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiSpecularProperty at 10.1.0.0: specular enable flags.
/// </summary>
public sealed record NifSpecularProperty : NifProperty
{
    public ushort Flags { get; }

    private NifSpecularProperty(NifBinaryReader reader)
        : base(reader)
    {
        Flags = reader.ReadUInt16("Flags");
    }

    internal static NifSpecularProperty Read(NifBinaryReader reader)
    {
        return new NifSpecularProperty(reader);
    }
}
