using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme2)]
public class ShipSlowDeathBehaviorModuleData : SlowDeathBehaviorModuleData
{
    internal new static ShipSlowDeathBehaviorModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private new static readonly IniParseTable<ShipSlowDeathBehaviorModuleData> FieldParseTable = SlowDeathBehaviorModuleData.FieldParseTable
        .Concat(new IniParseTable<ShipSlowDeathBehaviorModuleData>());
}
