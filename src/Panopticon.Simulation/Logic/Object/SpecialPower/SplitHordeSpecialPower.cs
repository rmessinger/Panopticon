using System.Collections.Generic;
using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public sealed class SplitHordeSpecialPowerModuleData : SpecialPowerModuleData
{
    internal static new SplitHordeSpecialPowerModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<SplitHordeSpecialPowerModuleData> FieldParseTable = SpecialPowerModuleData.FieldParseTable
        .Concat(new IniParseTable<SplitHordeSpecialPowerModuleData>());
}
