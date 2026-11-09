#nullable enable

using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Scoped asset name reference in the package manifest.</summary>
public readonly record struct MapPackageAssetReference(string Scope, string Name)
{
    public static MapPackageAssetReference FromModel(AssetReference reference) =>
        new(reference.Scope, reference.Name);

    public AssetReference ToModel() => new(Scope, Name);
}
