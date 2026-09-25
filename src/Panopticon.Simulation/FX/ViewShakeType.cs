using Panopticon.Data.Ini;

namespace Panopticon.FX;

public enum ViewShakeType
{
    [IniEnum("SUBTLE")]
    Subtle,

    [IniEnum("NORMAL")]
    Normal,

    [IniEnum("STRONG")]
    Strong,

    [IniEnum("SEVERE")]
    Severe,

    [IniEnum("CINE_EXTREME")]
    CineExtreme,

    [IniEnum("CINE_INSANE")]
    CineInsane,
}
