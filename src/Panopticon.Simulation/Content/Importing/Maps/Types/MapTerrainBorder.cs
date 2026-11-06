#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>One legacy terrain border rectangle in lattice coordinates.</summary>
public sealed record MapTerrainBorder(uint Corner1X, uint Corner1Y, uint X, uint Y);
