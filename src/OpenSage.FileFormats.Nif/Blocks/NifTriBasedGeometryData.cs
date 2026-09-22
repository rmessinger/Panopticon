namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiTriBasedGeomData at 10.1.0.0: adds the declared triangle count.
/// </summary>
public abstract record NifTriBasedGeometryData : NifGeometryData
{
    public int NumTriangles { get; }

    protected NifTriBasedGeometryData(NifBinaryReader reader)
        : base(reader)
    {
        NumTriangles = reader.ReadUInt16("NumTriangles");
    }
}
