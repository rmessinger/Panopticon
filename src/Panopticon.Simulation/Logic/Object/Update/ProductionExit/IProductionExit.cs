using System.Numerics;

namespace Panopticon.Logic.Object;

interface IProductionExit
{
    bool CanProduce => true;
    Vector3 GetUnitCreatePoint();
    Vector3? GetNaturalRallyPoint();
    void ProduceUnit() { }
}
