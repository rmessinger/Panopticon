#nullable enable

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Inventory entry for the strict-JSON vector feature sidecar in the package manifest.</summary>
public sealed record MapPackageVectorLayerEntry(
    string Path,
    string Encoding,
    long ByteLength,
    string Sha256,
    int FeatureCount);
