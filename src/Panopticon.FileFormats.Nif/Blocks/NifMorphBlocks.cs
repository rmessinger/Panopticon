using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiGeomMorpherController at 10.1.0.0 (EE2 cooked): time-controller
/// fields, one unknown flags word, and a NiMorphData reference. Decoded
/// structurally only; morph conversion is C6 work and the importer
/// rejects the family.
/// </summary>
public sealed record NifGeomMorpherController : NifTimeController
{
    public ushort UnknownFlags { get; }

    public int DataReference { get; }

    private NifGeomMorpherController(NifBinaryReader reader)
        : base(reader)
    {
        UnknownFlags = reader.ReadUInt16("UnknownFlags");
        DataReference = reader.ReadBlockReference("Data");
    }

    internal static NifGeomMorpherController Read(NifBinaryReader reader)
    {
        return new NifGeomMorpherController(reader);
    }
}

/// <summary>
/// One morph inside NiMorphData: weight keys plus one offset vector per
/// model vertex.
/// </summary>
public sealed record NifMorph(NifKeyGroup<NifKey<float>>? WeightKeys, IReadOnlyList<Vector3> Offsets);

/// <summary>
/// NiMorphData at 10.1.0.0 (EE2 cooked): morph and vertex counts wrapped
/// by two unexplained flag bytes, then per morph a float key group and the
/// vertex offsets. Decoded structurally only; conversion is C6 work.
/// </summary>
public sealed record NifMorphData : NifBlock
{
    public byte Unknown0 { get; }

    public uint NumVertices { get; }

    public byte Unknown1 { get; }

    public IReadOnlyList<NifMorph> Morphs { get; }

    private NifMorphData(NifBinaryReader reader)
    {
        Unknown0 = reader.ReadByte("Unknown0");
        var morphCount = reader.ReadElementCount(4, "Morphs.Count");
        NumVertices = reader.ReadUInt32("NumVertices");
        Unknown1 = reader.ReadByte("Unknown1");

        var morphs = new NifMorph[morphCount];
        var offsetBytes = (int)(NumVertices * 12);
        for (var i = 0; i < morphCount; i++)
        {
            var weightKeys = NifKeyGroupReader.ReadFloatGroup(reader, "NiMorphData", $"Morphs[{i}]");
            if (reader.RemainingBytes < offsetBytes)
            {
                throw new InvalidDataException(
                    $"NIF NiMorphData morph {i} declares {NumVertices} vertex offsets " +
                    $"({offsetBytes} bytes) but only {reader.RemainingBytes} bytes remain.");
            }

            var offsets = new Vector3[NumVertices];
            for (var v = 0; v < NumVertices; v++)
            {
                offsets[v] = reader.ReadVector3($"Morphs[{i}].Offsets[{v}]");
            }

            morphs[i] = new NifMorph(weightKeys, offsets);
        }

        Morphs = morphs;
    }

    internal static NifMorphData Read(NifBinaryReader reader)
    {
        return new NifMorphData(reader);
    }
}
