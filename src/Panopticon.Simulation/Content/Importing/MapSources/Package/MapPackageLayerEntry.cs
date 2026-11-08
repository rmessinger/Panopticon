#nullable enable

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Inventory entry for one chunk layer binary file in the package manifest.</summary>
public sealed record MapPackageLayerEntry(
    int ChunkX,
    int ChunkY,
    string LayerKind,
    string Path,
    string Encoding,
    int Width,
    int Height,
    long ByteLength,
    string Sha256);
