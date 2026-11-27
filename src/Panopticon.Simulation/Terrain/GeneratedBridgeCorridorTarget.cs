#nullable enable

namespace Panopticon.Terrain;

/// <summary>
/// One planned lattice vertex: border-inclusive indices and the world height before quantization.
/// </summary>
internal readonly record struct GeneratedBridgeCorridorTarget(int X, int Y, double Height, GeneratedBridgeCorridorRegion Region);
