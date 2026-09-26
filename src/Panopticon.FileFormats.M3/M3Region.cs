namespace Panopticon.FileFormats.M3;

public sealed record M3Region(
    uint Id,
    uint Unknown,
    uint FirstVertex,
    uint VertexCount,
    uint FirstFaceIndex,
    uint FaceIndexCount,
    ushort BoneCount,
    ushort FirstBoneLookup,
    ushort BoneLookupCount,
    ushort Unknown2,
    byte VertexLookupsUsed,
    byte Unknown3,
    ushort RootBone);
