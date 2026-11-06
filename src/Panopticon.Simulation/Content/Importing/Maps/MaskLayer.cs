#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>129 x 129 core-only <see cref="MapMaskFlags"/> bits; unknown bits fail validation. Flags are copied at construction.</summary>
public sealed record MaskLayer
{
    public MaskLayer(IReadOnlyList<byte> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        Flags = flags.ToArray();
    }

    public IReadOnlyList<byte> Flags { get; }
}
