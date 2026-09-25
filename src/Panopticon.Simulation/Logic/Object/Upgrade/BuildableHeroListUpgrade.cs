using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme2)]
public sealed class BuildableHeroListUpgradeModuleData : UpgradeModuleData
{
    internal static BuildableHeroListUpgradeModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<BuildableHeroListUpgradeModuleData> FieldParseTable = UpgradeModuleData.FieldParseTable
        .Concat(new IniParseTable<BuildableHeroListUpgradeModuleData>());
}
