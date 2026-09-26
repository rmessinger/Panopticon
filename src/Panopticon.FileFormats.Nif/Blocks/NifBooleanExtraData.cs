namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiBooleanExtraData at 10.1.0.0: a name plus one passive one-byte payload.
/// NiExtraData carries no extra-data or controller fields.
/// </summary>
public sealed record NifBooleanExtraData : NifBlock
{
    public string Name { get; }
    public bool Data { get; }

    private NifBooleanExtraData(NifBinaryReader reader)
    {
        Name = reader.ReadSizedString("Name");
        Data = reader.ReadByte("BooleanData") != 0;
    }

    internal static NifBooleanExtraData Read(NifBinaryReader reader)
    {
        return new NifBooleanExtraData(reader);
    }
}
