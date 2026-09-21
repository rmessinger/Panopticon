namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiSwitchNode at 10.1.0.0: switch flags and the active child index.
/// </summary>
public abstract record NifSwitchNode : NifNode
{
    public ushort SwitchFlags { get; }
    public int SwitchIndex { get; }

    protected NifSwitchNode(NifBinaryReader reader)
        : base(reader)
    {
        SwitchFlags = reader.ReadUInt16("SwitchFlags");
        SwitchIndex = reader.ReadInt32("SwitchIndex");
    }
}
