using System.Numerics;

namespace Panopticon.Terrain.Decals;

public sealed record TerrainDecal(TerrainDecalTemplate Template, Vector3 Position, float Radius, int Cell);
