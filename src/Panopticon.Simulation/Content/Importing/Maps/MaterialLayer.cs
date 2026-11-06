#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>129 x 129 core-only palette indices into <see cref="BlendModel.Palette"/>. Indices are copied at construction.</summary>
public sealed record MaterialLayer
{
    public MaterialLayer(IReadOnlyList<byte> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        Indices = indices.ToArray();
    }

    public IReadOnlyList<byte> Indices { get; }
}
