#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Material palette: the material vocabulary that <see cref="MaterialLayer"/> indexes. Entries are copied at construction.</summary>
public sealed record BlendModel
{
    public BlendModel(IReadOnlyList<MaterialPaletteEntry> palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        Palette = palette.ToArray();
    }

    public IReadOnlyList<MaterialPaletteEntry> Palette { get; }
}
