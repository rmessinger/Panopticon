namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiStringExtraData at 10.1.0.0: a name plus one passive payload string.
/// NiExtraData carries no extra-data or controller fields.
/// </summary>
public sealed record NifStringExtraData : NifBlock
{
    public string Name { get; }
    public string Data { get; }

    private NifStringExtraData(NifBinaryReader reader)
    {
        Name = reader.ReadSizedString("Name");
        Data = reader.ReadSizedString("StringData");
    }

    internal static NifStringExtraData Read(NifBinaryReader reader)
    {
        return new NifStringExtraData(reader);
    }
}
