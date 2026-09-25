using Panopticon.Data.Ini;

namespace Panopticon.Audio;

public enum AudioPriority
{
    [IniEnum("lowest")]
    Lowest,

    [IniEnum("low")]
    Low,

    [IniEnum("normal")]
    Normal,

    [IniEnum("high")]
    High,

    [IniEnum("critical")]
    Critical
}
