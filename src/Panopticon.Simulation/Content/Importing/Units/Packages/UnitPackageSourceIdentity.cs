#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Source identity declared in a unit package manifest.</summary>
public sealed record UnitPackageSourceIdentity(string Id, bool CaseSensitiveNames);
