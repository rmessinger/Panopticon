using Panopticon.Data.Ini;

namespace Panopticon.Gui;

public sealed class ShellMenuScheme : BaseAsset
{
    internal static ShellMenuScheme Parse(IniParser parser)
    {
        return parser.ParseNamedBlock(
            (x, name) => x.SetNameAndInstanceId("ShellMenuScheme", name),
            FieldParseTable);
    }

    private static readonly IniParseTable<ShellMenuScheme> FieldParseTable = new IniParseTable<ShellMenuScheme>
    {
    };
}
