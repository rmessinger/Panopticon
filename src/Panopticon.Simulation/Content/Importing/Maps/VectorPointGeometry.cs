#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>A single vector position.</summary>
public sealed record VectorPointGeometry(VectorPosition Position) : VectorGeometry;
