#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Manifest entry indexing one retained source payload in the unit package.</summary>
public sealed record UnitPackageRetainedPayloadEntry(
    string Path,
    string Dialect,
    int DialectVersion,
    long ByteLength,
    string Sha256,
    UnitPackageProvenance? Provenance = null);
