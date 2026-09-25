using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public class HordeMemberCollideModuleData : BehaviorModuleData
{
    internal static HordeMemberCollideModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    internal static readonly IniParseTable<HordeMemberCollideModuleData> FieldParseTable = new IniParseTable<HordeMemberCollideModuleData>();
}
