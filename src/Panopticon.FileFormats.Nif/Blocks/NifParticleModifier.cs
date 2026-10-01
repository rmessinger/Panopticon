namespace Panopticon.FileFormats.Nif;

/// <summary>
/// Base for legacy particle modifiers at 10.1.0.0: next-modifier
/// reference (forming a chain under the controller) and the owning
/// controller reference.
/// </summary>
public abstract record NifParticleModifier : NifBlock
{
    public int NextModifierReference { get; }
    public int ControllerReference { get; }

    protected NifParticleModifier(NifBinaryReader reader)
    {
        NextModifierReference = reader.ReadBlockReference("NextModifier");
        ControllerReference = reader.ReadBlockReference("Controller");
    }
}
