using Panopticon.Data.Ini;

namespace Panopticon.FX;

public enum TerrainScorchType
{
    [IniEnum("RANDOM")]
    Random = -1,

    [IniEnum("SCORCH_1")]
    Scorch1 = 0,
    [IniEnum("SCORCH_2")]
    Scorch2 = 1,
    [IniEnum("SCORCH_3")]
    Scorch3 = 2,
    [IniEnum("SCORCH_4")]
    Scorch4 = 3,
    [IniEnum("SHADOW_SCORCH")]
    ShadowScorch = 4,
}
