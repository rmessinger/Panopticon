using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiGeomMorpherController at 10.1.0.0 (EE2 cooked): time-controller
/// fields, morpher flags, a NiMorphData reference, and the always-update
/// byte.
/// </summary>
public sealed record NifGeomMorpherController : NifTimeController
{
    public ushort MorpherFlags { get; }

    public int DataReference { get; }

    public byte AlwaysUpdate { get; }

    private NifGeomMorpherController(NifBinaryReader reader)
        : base(reader)
    {
        MorpherFlags = reader.ReadUInt16("MorpherFlags");
        DataReference = reader.ReadBlockReference("Data");
        AlwaysUpdate = reader.ReadByte("AlwaysUpdate");
    }

    internal static NifGeomMorpherController Read(NifBinaryReader reader)
    {
        return new NifGeomMorpherController(reader);
    }
}

/// <summary>
/// One morph inside NiMorphData: weight keys plus one vector per model
/// vertex. Morph 0 holds the base shape; morphs 1..N hold relative deltas
/// when RelativeTargets is set.
/// </summary>
public sealed record NifMorph(NifKeyGroup<NifKey<float>>? WeightKeys, IReadOnlyList<Vector3> Vectors);

/// <summary>
/// NiMorphData at 10.1.0.0 (EE2 cooked): morph and vertex counts, the
/// relative-targets flag, then per morph a float key group and the vertex
/// vectors.
/// </summary>
public sealed record NifMorphData : NifBlock
{
    public uint NumMorphs { get; }

    public uint NumVertices { get; }

    public bool RelativeTargets { get; }

    public IReadOnlyList<NifMorph> Morphs { get; }

    private NifMorphData(NifBinaryReader reader)
    {
        var morphCount = reader.ReadElementCount(4, "Morphs.Count");
        NumMorphs = (uint)morphCount;
        NumVertices = reader.ReadUInt32("NumVertices");
        RelativeTargets = reader.ReadByte("RelativeTargets") != 0;

        var morphs = new NifMorph[morphCount];
        var offsetBytes = (int)(NumVertices * 12);
        for (var i = 0; i < morphCount; i++)
        {
            var weightKeys = NifKeyGroupReader.ReadFloatGroup(reader, "NiMorphData", $"Morphs[{i}]");
            if (reader.RemainingBytes < offsetBytes)
            {
                throw new InvalidDataException(
                    $"NIF NiMorphData morph {i} declares {NumVertices} vertex vectors " +
                    $"({offsetBytes} bytes) but only {reader.RemainingBytes} bytes remain.");
            }

            var vectors = new Vector3[NumVertices];
            for (var v = 0; v < NumVertices; v++)
            {
                vectors[v] = reader.ReadVector3($"Morphs[{i}].Vectors[{v}]");
            }

            morphs[i] = new NifMorph(weightKeys, vectors);
        }

        Morphs = morphs;
    }

    internal static NifMorphData Read(NifBinaryReader reader)
    {
        return new NifMorphData(reader);
    }
}
