#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>One palette entry. <c>Index</c> is the value stored in <see cref="MaterialLayer"/>.</summary>
public sealed record MaterialPaletteEntry(byte Index, string Name, AssetReference Texture);
