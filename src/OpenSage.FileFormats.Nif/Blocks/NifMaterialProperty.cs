using System.Numerics;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiMaterialProperty at 10.1.0.0: fixed-function colors, glossiness,
/// and opacity.
/// </summary>
public sealed record NifMaterialProperty : NifProperty
{
    public Vector3 AmbientColor { get; }
    public Vector3 DiffuseColor { get; }
    public Vector3 SpecularColor { get; }
    public Vector3 EmissiveColor { get; }
    public float Glossiness { get; }
    public float Alpha { get; }

    private NifMaterialProperty(NifBinaryReader reader)
        : base(reader)
    {
        AmbientColor = reader.ReadColor3("AmbientColor");
        DiffuseColor = reader.ReadColor3("DiffuseColor");
        SpecularColor = reader.ReadColor3("SpecularColor");
        EmissiveColor = reader.ReadColor3("EmissiveColor");
        Glossiness = reader.ReadSingle("Glossiness");
        Alpha = reader.ReadSingle("Alpha");
    }

    internal static NifMaterialProperty Read(NifBinaryReader reader)
    {
        return new NifMaterialProperty(reader);
    }
}
