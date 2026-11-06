#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Chunk-aligned RGBA8 pixels. Asset-level reference, not gameplay data. Pixels are copied at construction.</summary>
public sealed record ImageryLayer
{
    public ImageryLayer(int width, int height, IReadOnlyList<byte> rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        Width = width;
        Height = height;
        Rgba = rgba.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<byte> Rgba { get; }
}
