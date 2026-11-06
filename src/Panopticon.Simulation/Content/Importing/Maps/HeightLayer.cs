#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Stored 131 x 131 float32 heights in chunk-local Z (GameUnit), apron included, finite only. Samples are copied at construction.</summary>
public sealed record HeightLayer
{
    public HeightLayer(IReadOnlyList<float> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        Samples = samples.ToArray();
    }

    public IReadOnlyList<float> Samples { get; }
}
