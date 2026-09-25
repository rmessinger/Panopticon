using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiZBufferProperty at 10.1.0.0: depth-test flags and test function.
/// </summary>
public sealed record NifZBufferProperty : NifProperty
{
    public ushort Flags { get; }
    public uint Function { get; }

    private NifZBufferProperty(NifBinaryReader reader)
        : base(reader)
    {
        Flags = reader.ReadUInt16("Flags");
        Function = reader.ReadUInt32("Function");
        if (Function > 7)
        {
            throw new InvalidDataException(
                $"NIF z-buffer property has test function {Function}; valid range is 0-7.");
        }
    }

    internal static NifZBufferProperty Read(NifBinaryReader reader)
    {
        return new NifZBufferProperty(reader);
    }
}
