using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiAVObject fields at 10.1.0.0: flags, local transform, property
/// references, collision reference. The local transform is T * R * S
/// with a row-major rotation.
/// </summary>
public abstract record NifAvObject : NifObjectNet
{
    public ushort Flags { get; }
    public Vector3 Translation { get; }
    public NifMatrix33 Rotation { get; }
    public float Scale { get; }
    public int[] PropertyReferences { get; }
    public int CollisionObjectReference { get; }

    protected NifAvObject(NifBinaryReader reader)
        : base(reader)
    {
        Flags = reader.ReadUInt16("Flags");
        Translation = reader.ReadVector3("Translation");
        Rotation = reader.ReadMatrix33("Rotation");
        Scale = reader.ReadSingle("Scale");
        PropertyReferences = reader.ReadBlockReferenceList("Properties");
        CollisionObjectReference = reader.ReadBlockReference("CollisionObject");
    }
}
