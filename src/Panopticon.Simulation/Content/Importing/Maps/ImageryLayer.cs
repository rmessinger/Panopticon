#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Chunk-aligned RGBA8 pixels. Asset-level reference, not gameplay data.</summary>
public sealed record ImageryLayer(int Width, int Height, IReadOnlyList<byte> Rgba);
