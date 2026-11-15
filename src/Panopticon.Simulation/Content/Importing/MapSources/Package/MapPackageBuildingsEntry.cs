#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.MapSources.Package;

public sealed record MapPackageBuildingsEntry(
    string Path,
    string Encoding,
    long ByteLength,
    string Sha256,
    int BuildingCount,
    IReadOnlyList<MapPackageBuildingMeshEntry> MeshChunks);
