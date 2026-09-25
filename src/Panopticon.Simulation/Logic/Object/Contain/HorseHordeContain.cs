using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public sealed class HorseHordeContainModuleData : HordeContainModuleData
{
    internal new static HorseHordeContainModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    internal static new readonly IniParseTable<HorseHordeContainModuleData> FieldParseTable = HordeContainModuleData.FieldParseTable
        .Concat(new IniParseTable<HorseHordeContainModuleData>());
}
