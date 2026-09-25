namespace Panopticon.FileFormats.Nif;

/// <summary>
/// Base for blocks that carry the NiObjectNET fields at 10.1.0.0:
/// name, extra-data reference list, controller reference.
/// </summary>
public abstract record NifObjectNet : NifBlock
{
    public string Name { get; }
    public int[] ExtraDataReferences { get; }
    public int ControllerReference { get; }

    protected NifObjectNet(NifBinaryReader reader)
    {
        Name = reader.ReadSizedString("Name");
        ExtraDataReferences = reader.ReadBlockReferenceList("ExtraDataList");
        ControllerReference = reader.ReadBlockReference("Controller");
    }
}
