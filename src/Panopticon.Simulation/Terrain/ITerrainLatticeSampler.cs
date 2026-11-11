#nullable enable

namespace Panopticon.Terrain;

public interface ITerrainLatticeSampler
{
    float GetHeight(int latticeX, int latticeY);
}
