using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiParticlesData at 10.1.0.0: geometry data plus per-particle radii,
/// sizes, and rotation quaternions. The EE2 cooked arrays are capacity
/// evidence only; active particle state lives in the controller.
/// </summary>
public sealed record NifParticlesData : NifGeometryData
{
    public IReadOnlyList<float>? Radii { get; }
    public ushort ActiveCount { get; }
    public IReadOnlyList<float>? Sizes { get; }
    public IReadOnlyList<Quaternion>? Rotations { get; }

    private NifParticlesData(NifBinaryReader reader)
        : base(reader)
    {
        Radii = ReadOptionalFloatArray(reader, NumVertices, "Radii");
        ActiveCount = reader.ReadUInt16("ActiveCount");

        var hasSizes = reader.ReadByte("HasSizes");
        if (hasSizes != 0)
        {
            var sizes = new float[NumVertices];
            for (var i = 0; i < NumVertices; i++)
            {
                sizes[i] = reader.ReadSingle($"Sizes[{i}]");
            }

            Sizes = sizes;
        }

        var hasRotations = reader.ReadByte("HasRotations");
        if (hasRotations != 0)
        {
            var rotations = new Quaternion[NumVertices];
            for (var i = 0; i < NumVertices; i++)
            {
                rotations[i] = ReadRotation(reader, i);
            }

            Rotations = rotations;
        }
    }

    internal static NifParticlesData Read(NifBinaryReader reader)
    {
        return new NifParticlesData(reader);
    }

    private static IReadOnlyList<float>? ReadOptionalFloatArray(
        NifBinaryReader reader,
        int count,
        string fieldName)
    {
        var hasValues = reader.ReadByte($"Has{fieldName}");
        if (hasValues == 0)
        {
            return null;
        }

        if (hasValues != 1)
        {
            throw new InvalidDataException(
                $"NIF NiParticlesData declares Has{fieldName}={hasValues}; only 0 or 1 is valid.");
        }

        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = reader.ReadSingle($"{fieldName}[{i}]");
        }

        return values;
    }

    private static Quaternion ReadRotation(NifBinaryReader reader, int index)
    {
        var w = reader.ReadSingle($"Rotations[{index}].W");
        var x = reader.ReadSingle($"Rotations[{index}].X");
        var y = reader.ReadSingle($"Rotations[{index}].Y");
        var z = reader.ReadSingle($"Rotations[{index}].Z");
        return new Quaternion(x, y, z, w);
    }
}
