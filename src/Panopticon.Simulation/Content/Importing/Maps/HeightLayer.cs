#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Stored 131 x 131 float32 heights in chunk-local Z (GameUnit), apron included, finite only.</summary>
public sealed record HeightLayer(IReadOnlyList<float> Samples);
