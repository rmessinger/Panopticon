namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiProperty base at 10.1.0.0: adds no fields over NiObjectNET but marks
/// the property half of the block set for reference validation.
/// </summary>
public abstract record NifProperty : NifObjectNet
{
    protected NifProperty(NifBinaryReader reader)
        : base(reader)
    {
    }
}
