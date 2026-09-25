using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme2)]
public class SlaveAttackNugget : WeaponEffectNugget
{
    internal static SlaveAttackNugget Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<SlaveAttackNugget> FieldParseTable = WeaponEffectNugget.FieldParseTable
        .Concat(new IniParseTable<SlaveAttackNugget>
        {
        });
}
