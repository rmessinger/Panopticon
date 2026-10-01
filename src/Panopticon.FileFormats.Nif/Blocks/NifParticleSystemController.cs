using System.IO;
using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiParticleSystemController at 10.1.0.0: the legacy pre-NiPSys
/// emitter. Drives the referenced NiParticles with speed, declination,
/// and planar-angle spread around the initial normal, birth rate, and
/// lifetime. The cooked NiParticleInfo array is a runtime snapshot and
/// is consumed opaquely.
/// </summary>
public sealed record NifParticleSystemController : NifTimeController
{
    public const int ParticleInfoSize = 40;

    public float Speed { get; }
    public float SpeedVariation { get; }
    public float Declination { get; }
    public float DeclinationVariation { get; }
    public float PlanarAngle { get; }
    public float PlanarAngleVariation { get; }
    public Vector3 InitialNormal { get; }
    public NifColor4 InitialColor { get; }
    public float InitialSize { get; }
    public float EmitStartTime { get; }
    public float EmitStopTime { get; }
    public bool Reset { get; }
    public float BirthRate { get; }
    public float Lifetime { get; }
    public float LifetimeVariation { get; }
    public bool UseBirthRate { get; }
    public byte SpawnOnDeath { get; }
    public Vector3 EmitterDimensions { get; }
    public int EmitterReference { get; }
    public ushort ParticleSlotCount { get; }
    public byte ParticleValidCount { get; }
    public byte[] ParticleInfoPayload { get; }
    public int EmitterModifierReference { get; }
    public int ParticleModifierReference { get; }
    public int ColliderReference { get; }
    public bool StaticTargetBound { get; }

    private NifParticleSystemController(NifBinaryReader reader)
        : base(reader)
    {
        Speed = reader.ReadSingle("Speed");
        SpeedVariation = reader.ReadSingle("SpeedVariation");
        Declination = reader.ReadSingle("Declination");
        DeclinationVariation = reader.ReadSingle("DeclinationVariation");
        PlanarAngle = reader.ReadSingle("PlanarAngle");
        PlanarAngleVariation = reader.ReadSingle("PlanarAngleVariation");
        InitialNormal = reader.ReadVector3("InitialNormal");
        InitialColor = ReadColor4(reader, "InitialColor");
        InitialSize = reader.ReadSingle("InitialSize");
        EmitStartTime = reader.ReadSingle("EmitStartTime");
        EmitStopTime = reader.ReadSingle("EmitStopTime");
        Reset = reader.ReadByte("Reset") != 0;
        BirthRate = reader.ReadSingle("BirthRate");
        Lifetime = reader.ReadSingle("Lifetime");
        LifetimeVariation = reader.ReadSingle("LifetimeVariation");
        UseBirthRate = reader.ReadByte("UseBirthRate") != 0;
        SpawnOnDeath = reader.ReadByte("SpawnOnDeath");
        EmitterDimensions = reader.ReadVector3("EmitterDimensions");
        EmitterReference = reader.ReadBlockReference("Emitter");

        ReadSpawnGenerationFields(reader);

        ParticleSlotCount = reader.ReadUInt16("ParticleSlotCount");
        ParticleValidCount = reader.ReadByte("ParticleValidCount");
        reader.ReadByte("ParticleSlotFlags");

        var payloadLength = (long)ParticleSlotCount * ParticleInfoSize;
        if (payloadLength > reader.RemainingBytes)
        {
            throw new InvalidDataException(
                $"NIF field 'ParticleInfos': {ParticleSlotCount} slots of {ParticleInfoSize} bytes " +
                $"exceed remaining {reader.RemainingBytes} bytes");
        }

        ParticleInfoPayload = new byte[ParticleSlotCount * ParticleInfoSize];
        reader.ReadBytes(ParticleInfoPayload);

        EmitterModifierReference = reader.ReadBlockReference("EmitterModifier");
        ParticleModifierReference = reader.ReadBlockReference("ParticleModifier");
        ColliderReference = reader.ReadBlockReference("Collider");
        StaticTargetBound = reader.ReadByte("StaticTargetBound") != 0;
    }

    internal static NifParticleSystemController Read(NifBinaryReader reader)
    {
        return new NifParticleSystemController(reader);
    }

    private static void ReadSpawnGenerationFields(NifBinaryReader reader)
    {
        // Spawn-generation state (percentage, flags, and IDs) is cooked
        // runtime data; consumed to stay aligned and never interpreted.
        reader.ReadUInt16("SpawnGeneration.Field0");
        reader.ReadSingle("SpawnGeneration.Field1");
        reader.ReadByte("SpawnGeneration.Field2");
        reader.ReadInt32("SpawnGeneration.Field3");
        reader.ReadInt32("SpawnGeneration.Field4");
        reader.ReadByte("SpawnGeneration.Field5");
    }

    private static NifColor4 ReadColor4(NifBinaryReader reader, string fieldName)
    {
        var r = reader.ReadSingle($"{fieldName}.R");
        var g = reader.ReadSingle($"{fieldName}.G");
        var b = reader.ReadSingle($"{fieldName}.B");
        var a = reader.ReadSingle($"{fieldName}.A");
        return new NifColor4(r, g, b, a);
    }
}
