#nullable enable

namespace Panopticon.Content.Importing.Units;

/// <summary>Where a record or value was authored. Line is 1-based; 0 means unknown.</summary>
public sealed record Provenance(string SourceId, string FilePath, int Line = 0, string? ArchiveLayer = null)
{
    public override string ToString() => Line > 0 ? $"{FilePath}:{Line}" : FilePath;
}
