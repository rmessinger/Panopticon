#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Core-only <see cref="MapMaskFlags"/> bits, core per axis = min(129, <see cref="TerrainLayout"/> lattice - chunk * 128); unknown bits fail validation. Flags are copied at construction.</summary>
public sealed record MaskLayer
{
    public MaskLayer(IReadOnlyList<byte> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        Flags = flags.ToArray();
    }

    public IReadOnlyList<byte> Flags { get; }
}
