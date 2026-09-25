using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme2)]
public class DOTNugget : DamageNugget
{
    internal static new DOTNugget Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<DOTNugget> FieldParseTable = DamageNugget.FieldParseTable
        .Concat(new IniParseTable<DOTNugget>
        {
            { "DamageInterval", (parser, x) => x.DamageInterval = parser.ParseInteger() },
            { "DamageDuration", (parser, x) => x.DamageDuration = parser.ParseInteger() },
        });

    public int DamageInterval { get; private set; }
    public int DamageDuration { get; private set; }
}
