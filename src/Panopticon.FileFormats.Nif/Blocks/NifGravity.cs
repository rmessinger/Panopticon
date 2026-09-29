using System;
using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiGravity at 10.1.0.0: a particle-space force. Only the directional
/// constant force (type 0) is decoded; point gravity belongs to the
/// rejected family per the C4h deep dive.
/// </summary>
public sealed record NifGravity : NifParticleModifier
{
    public const uint DirectionalConstant = 0;

    public float Decay { get; }
    public float Force { get; }
    public uint ForceType { get; }
    public Vector3 Position { get; }
    public Vector3 Direction { get; }

    private NifGravity(NifBinaryReader reader)
        : base(reader)
    {
        Decay = reader.ReadSingle("Decay");
        Force = reader.ReadSingle("Force");
        ForceType = reader.ReadUInt32("ForceType");
        if (ForceType != DirectionalConstant)
        {
            throw new NotSupportedException(
                $"NIF NiGravity block uses force type {ForceType}; C4h supports the " +
                "directional constant force (0) only.");
        }

        Position = reader.ReadVector3("Position");
        Direction = reader.ReadVector3("Direction");
    }

    internal static NifGravity Read(NifBinaryReader reader)
    {
        return new NifGravity(reader);
    }
}
