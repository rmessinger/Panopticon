namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiIntegerExtraData at 10.1.0.0: a name plus one passive i32 payload.
/// NiExtraData carries no extra-data or controller fields.
/// </summary>
public sealed record NifIntegerExtraData : NifBlock
{
    public string Name { get; }
    public int Data { get; }

    private NifIntegerExtraData(NifBinaryReader reader)
    {
        Name = reader.ReadSizedString("Name");
        Data = reader.ReadInt32("IntegerData");
    }

    internal static NifIntegerExtraData Read(NifBinaryReader reader)
    {
        return new NifIntegerExtraData(reader);
    }
}
