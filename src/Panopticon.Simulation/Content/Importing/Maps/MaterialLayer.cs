#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Core-only palette indices into <see cref="BlendModel.Palette"/>, core per axis = min(129, <see cref="TerrainLayout"/> lattice - chunk * 128). Indices are copied at construction.</summary>
public sealed record MaterialLayer
{
    public MaterialLayer(IReadOnlyList<byte> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        Indices = indices.ToArray();
    }

    public IReadOnlyList<byte> Indices { get; }
}
