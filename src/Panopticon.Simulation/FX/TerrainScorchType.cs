using Panopticon.Data.Ini;

namespace Panopticon.FX;

public enum TerrainScorchType
{
    [IniEnum("RANDOM")]
    Random,

    [IniEnum("SCORCH_4"), AddedIn(SageGame.CncGeneralsZeroHour)]
    Scorch4,
}
