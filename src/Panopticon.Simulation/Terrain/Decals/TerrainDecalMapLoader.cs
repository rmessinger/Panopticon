using Panopticon.Data.Map;
using Panopticon.FX;

namespace Panopticon.Terrain.Decals;

internal static class TerrainDecalMapLoader
{
    internal static bool TryAdd(TerrainDecalStore store, MapObject mapObject)
    {
        // MO_SCORCH is a runtime flag derived from this typed property, not a serialized road flag.
        var scorch = mapObject.Properties.GetPropOrNull("scorchType");
        if (scorch?.Key.PropertyType != AssetPropertyType.Integer)
        {
            return false;
        }

        var radius = mapObject.Properties.GetPropOrNull("objectRadius");
        if (radius?.Key.PropertyType == AssetPropertyType.RealNumber)
        {
            var cell = (int)scorch.Value;
            store.Add(TerrainDecalTemplate.Scorch, mapObject.Position, (float)radius.Value,
                cell >= 0 && cell <= (int)TerrainScorchType.ShadowScorch ? cell : 0);
        }

        return true;
    }
}
