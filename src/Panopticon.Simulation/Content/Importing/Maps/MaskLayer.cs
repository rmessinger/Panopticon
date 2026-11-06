#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>129 x 129 core-only <see cref="MapMaskFlags"/> bits; unknown bits fail validation.</summary>
public sealed record MaskLayer(IReadOnlyList<byte> Flags);
