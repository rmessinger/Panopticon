#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Source provenance serialized in unit packages.</summary>
public sealed record UnitPackageProvenance(
    string SourceId,
    string FilePath,
    int Line = 0,
    string? ArchiveLayer = null)
{
    public Provenance ToModel() => new(SourceId, FilePath, Line, ArchiveLayer);
}
