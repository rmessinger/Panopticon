#nullable enable

using System;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>One palette entry in the package manifest blend model.</summary>
public sealed record MapPackageMaterialPaletteEntry(byte Index, string Name, MapPackageAssetReference Texture)
{
    public static MapPackageMaterialPaletteEntry FromModel(MaterialPaletteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new MapPackageMaterialPaletteEntry(entry.Index, entry.Name, MapPackageAssetReference.FromModel(entry.Texture));
    }

    public MaterialPaletteEntry ToModel() => new(Index, Name, Texture.ToModel());
}
