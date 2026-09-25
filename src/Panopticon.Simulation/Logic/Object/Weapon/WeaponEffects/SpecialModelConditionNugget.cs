using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public class SpecialModelConditionNugget : WeaponEffectNugget
{
    internal static SpecialModelConditionNugget Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<SpecialModelConditionNugget> FieldParseTable = WeaponEffectNugget.FieldParseTable
        .Concat(new IniParseTable<SpecialModelConditionNugget>
        {
            { "ModelConditionNames", (parser, x) => x.ModelConditionNames = parser.ParseAssetReferenceArray() },
            { "ModelConditionDuration", (parser, x) => x.ModelConditionDuration = parser.ParseInteger() },
        });

    public string[] ModelConditionNames { get; private set; }
    public int ModelConditionDuration { get; private set; }
}
