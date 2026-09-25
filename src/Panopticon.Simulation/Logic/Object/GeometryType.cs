using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

public enum GeometryType
{
    [IniEnum("SPHERE")]
    Sphere = 0,

    [IniEnum("CYLINDER")]
    Cylinder = 1,

    [IniEnum("BOX")]
    Box = 2,
}
