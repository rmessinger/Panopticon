#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Optional manifest metadata supplied by the writer's caller.</summary>
public readonly record struct UnitPackageWriteOptions(UnitPackageProvenance? Provenance = null);
