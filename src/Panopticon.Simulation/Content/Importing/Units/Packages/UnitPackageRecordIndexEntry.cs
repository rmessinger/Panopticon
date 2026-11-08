#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Manifest entry indexing one record payload file in the unit package.</summary>
public sealed record UnitPackageRecordIndexEntry(
    string Name,
    ContentKind Kind,
    string Path,
    long ByteLength,
    string Sha256);
