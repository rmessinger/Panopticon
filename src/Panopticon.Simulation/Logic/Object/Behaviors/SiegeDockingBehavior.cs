using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public sealed class SiegeDockingBehaviorModuleData : BehaviorModuleData
{
    internal static SiegeDockingBehaviorModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<SiegeDockingBehaviorModuleData> FieldParseTable = new IniParseTable<SiegeDockingBehaviorModuleData>();
}
