using Panopticon.Data.Ini;

namespace Panopticon.Input;

public enum CommandMapCategory
{
    [IniEnum("INTERFACE")]
    Interface,

    [IniEnum("TEAM")]
    Team,

    [IniEnum("SELECTION")]
    Selection,

    [IniEnum("CONTROL")]
    Control,

    [IniEnum("MISC")]
    Misc
}
