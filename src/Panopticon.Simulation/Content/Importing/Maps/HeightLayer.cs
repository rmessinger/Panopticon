#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Stored float32 heights in chunk-local Z (GameUnit), apron included, finite only: (core + 2) per axis, with core = min(129, <see cref="TerrainLayout"/> lattice - chunk * 128). Samples are copied at construction.</summary>
public sealed record HeightLayer
{
    public HeightLayer(IReadOnlyList<float> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        Samples = samples.ToArray();
    }

    public IReadOnlyList<float> Samples { get; }
}
