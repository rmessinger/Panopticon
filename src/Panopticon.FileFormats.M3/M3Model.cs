using System.Collections.Generic;

namespace Panopticon.FileFormats.M3;

public sealed record M3Model(
    string Name,
    IReadOnlyList<M3Bone> Bones,
    uint SkinBoneCount,
    uint VertexFlags,
    M3VertexFormat VertexFormat,
    IReadOnlyList<byte> VertexBytes,
    IReadOnlyList<M3Division> Divisions,
    IReadOnlyList<ushort> BoneLookup,
    IReadOnlyList<M3MaterialReference> MaterialReferences,
    IReadOnlyList<M3Material> Materials);
