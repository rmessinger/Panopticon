#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Material palette vocabulary in the package manifest.</summary>
public sealed record MapPackageBlendModel
{
    [JsonConstructor]
    public MapPackageBlendModel(IReadOnlyList<MapPackageMaterialPaletteEntry>? palette = null)
    {
        Palette = palette?.ToArray() ?? [];
    }

    public IReadOnlyList<MapPackageMaterialPaletteEntry> Palette { get; init; }

    public static MapPackageBlendModel FromModel(BlendModel blend)
    {
        ArgumentNullException.ThrowIfNull(blend);
        return new MapPackageBlendModel(blend.Palette.Select(MapPackageMaterialPaletteEntry.FromModel).ToArray());
    }

    public BlendModel ToModel() => new(Palette.Select(e => e.ToModel()).ToArray());
}
