#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Material palette: the material vocabulary that <see cref="MaterialLayer"/> indexes.</summary>
public sealed record BlendModel(IReadOnlyList<MaterialPaletteEntry> Palette);
