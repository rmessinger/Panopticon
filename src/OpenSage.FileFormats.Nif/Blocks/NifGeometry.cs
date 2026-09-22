using System;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiGeometry at 10.1.0.0: an AVObject with geometry data, skin, and
/// material-shader references. Shader materials are out of C4a scope.
/// </summary>
public abstract record NifGeometry : NifAvObject
{
    public int DataReference { get; }
    public int SkinInstanceReference { get; }
    public bool HasShader { get; }

    protected NifGeometry(NifBinaryReader reader)
        : base(reader)
    {
        DataReference = reader.ReadBlockReference("Data");
        SkinInstanceReference = reader.ReadBlockReference("SkinInstance");
        HasShader = reader.ReadByte("HasShader") != 0;
        if (HasShader)
        {
            throw new NotSupportedException(
                $"NIF geometry '{Name}' has a shader material; C4a supports fixed-function materials only.");
        }
    }
}
