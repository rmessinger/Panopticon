using System;
using System.IO;
namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiSourceTexture at 10.1.0.0: external by-name texture reference plus
/// format preferences. The file name stays a plain CPU string; C4a never
/// resolves .texcache.
/// </summary>
public sealed record NifSourceTexture : NifObjectNet
{
    public bool UseExternal { get; }
    public string FileName { get; }
    public int PixelDataReference { get; }
    public uint PixelLayout { get; }
    public uint UseMipmaps { get; }
    public uint AlphaFormat { get; }
    public byte IsStatic { get; }

    private NifSourceTexture(NifBinaryReader reader)
        : base(reader)
    {
        UseExternal = reader.ReadByte("UseExternal") != 0;
        FileName = reader.ReadSizedString("FileName");
        PixelDataReference = reader.ReadBlockReference("PixelData");
        if (PixelDataReference != -1)
        {
            throw new NotSupportedException(
                $"NIF source texture '{Name}' references pixel data; C4a supports by-name external textures only.");
        }

        PixelLayout = reader.ReadUInt32("PixelLayout");
        if (PixelLayout > 7)
        {
            throw new InvalidDataException(
                $"NIF source texture '{Name}' has pixel layout {PixelLayout}; valid range is 0-7.");
        }

        UseMipmaps = reader.ReadUInt32("UseMipmaps");
        if (UseMipmaps > 2)
        {
            throw new InvalidDataException(
                $"NIF source texture '{Name}' has mipmap format {UseMipmaps}; valid range is 0-2.");
        }

        AlphaFormat = reader.ReadUInt32("AlphaFormat");
        if (AlphaFormat > 3)
        {
            throw new InvalidDataException(
                $"NIF source texture '{Name}' has alpha format {AlphaFormat}; valid range is 0-3.");
        }

        IsStatic = reader.ReadByte("IsStatic");
    }

    internal static NifSourceTexture Read(NifBinaryReader reader)
    {
        return new NifSourceTexture(reader);
    }
}
