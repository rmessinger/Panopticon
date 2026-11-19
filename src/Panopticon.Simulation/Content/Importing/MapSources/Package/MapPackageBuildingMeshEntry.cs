#nullable enable

namespace Panopticon.Content.Importing.MapSources.Package;

public sealed record MapPackageBuildingMeshEntry(
    int ChunkX,
    int ChunkY,
    string Path,
    string Encoding,
    long ByteLength,
    string Sha256,
    int SegmentCount,
    int VertexCount,
    int IndexCount);
