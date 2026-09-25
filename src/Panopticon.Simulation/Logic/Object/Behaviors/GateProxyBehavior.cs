using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public class GateProxyBehaviorModuleData : BehaviorModuleData
{
    internal static GateProxyBehaviorModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    internal static readonly IniParseTable<GateProxyBehaviorModuleData> FieldParseTable = new IniParseTable<GateProxyBehaviorModuleData>();

}
