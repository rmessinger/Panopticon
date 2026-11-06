#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Scoped asset name, resolved through AssetStore scopes at the runtime bridge.</summary>
public readonly record struct AssetReference(string Scope, string Name);
