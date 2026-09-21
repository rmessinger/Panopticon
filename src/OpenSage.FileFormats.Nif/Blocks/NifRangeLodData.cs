using System;
using System.Collections.Generic;
using System.IO;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiRangeLODData at 10.1.0.0: LOD center plus ordered (near, far) range
/// pairs, one per child of the owning NiLODNode.
/// </summary>
public sealed record NifRangeLodData : NifLodData
{
    public System.Numerics.Vector3 LodCenter { get; }
    public IReadOnlyList<(float Near, float Far)> Ranges { get; }

    private NifRangeLodData(NifBinaryReader reader)
    {
        LodCenter = reader.ReadVector3("LODCenter");
        var count = reader.ReadElementCount(8, "NumLODLevels");
        var ranges = new (float, float)[count];
        for (var i = 0; i < count; i++)
        {
            var near = reader.ReadSingle($"LODLevels[{i}].Near");
            var far = reader.ReadSingle($"LODLevels[{i}].Far");
            if (!float.IsFinite(near) || !float.IsFinite(far) || far < near)
            {
                throw new InvalidDataException(
                    $"NIF range LOD level {i} is ({near}, {far}); expected finite values with near <= far.");
            }
            ranges[i] = (near, far);
        }
        Ranges = ranges;
    }

    internal static NifRangeLodData Read(NifBinaryReader reader)
    {
        return new NifRangeLodData(reader);
    }
}
