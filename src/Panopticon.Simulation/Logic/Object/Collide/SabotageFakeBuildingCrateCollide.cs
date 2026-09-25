using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

/// <summary>
/// Hardcoded to play the SabotageBuilding sound definition when triggered.
/// </summary>
[AddedIn(SageGame.CncGeneralsZeroHour)]
public sealed class SabotageFakeBuildingCrateCollideModuleData : CrateCollideModuleData
{
    internal static SabotageFakeBuildingCrateCollideModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<SabotageFakeBuildingCrateCollideModuleData> FieldParseTable = CrateCollideModuleData.FieldParseTable
        .Concat(new IniParseTable<SabotageFakeBuildingCrateCollideModuleData>());
}
