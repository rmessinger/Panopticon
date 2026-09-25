using System.Collections.Generic;
using Panopticon.Data.Ini;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public sealed class SymbioticStructuresBodyModuleData : ActiveBodyModuleData
{
    internal static new SymbioticStructuresBodyModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<SymbioticStructuresBodyModuleData> FieldParseTable = ActiveBodyModuleData.FieldParseTable
        .Concat(new IniParseTable<SymbioticStructuresBodyModuleData>
        {
            { "Symbiote", (parser, x) => x.Symbiote = parser.ParseString() }
        });

    public string Symbiote { get; private set; }
}
