#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Root record reference declared in a unit package manifest.</summary>
public sealed record UnitPackageRootRecordEntry(string Name, ContentKind Kind = ContentKind.Unit);
