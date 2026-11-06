#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>129 x 129 core-only palette indices into <see cref="BlendModel.Palette"/>.</summary>
public sealed record MaterialLayer(IReadOnlyList<byte> Indices);
