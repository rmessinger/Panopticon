namespace Panopticon.Terrain.Cdlod;

public interface ICdlodErrorProvider
{
    (float Min, float Max) GetHeightRange(CdlodNode node);
}
