namespace Panopticon.Terrain.Decals;

public sealed record TerrainDecalTemplate(string TextureName, int CellsPerRow, float CellSpacing)
{
    public static readonly TerrainDecalTemplate Scorch = new("EXScorch01.tga", 3, 1.5f);
}
