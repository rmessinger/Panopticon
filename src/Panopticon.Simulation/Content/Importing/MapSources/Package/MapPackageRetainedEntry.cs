#nullable enable

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Inventory entry for one retained opaque section binary file in the package manifest.</summary>
public sealed record MapPackageRetainedEntry(
    string SectionName,
    int SectionVersion,
    string Path,
    long ByteLength,
    string Sha256);
