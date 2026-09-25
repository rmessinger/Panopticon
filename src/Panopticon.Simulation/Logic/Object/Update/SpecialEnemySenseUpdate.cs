using System.Collections.Generic;
using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public sealed class SpecialEnemySenseUpdateModuleData : UpdateModuleData
{
    internal static SpecialEnemySenseUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<SpecialEnemySenseUpdateModuleData> FieldParseTable = new IniParseTable<SpecialEnemySenseUpdateModuleData>
    {
       { "SpecialEnemyFilter", (parser, x) => x.EnemyFilter = ObjectFilter.Parse(parser) },
       { "ScanRange", (parser, x) => x.ScanRange = parser.ParseInteger() },
       { "ScanInterval", (parser, x) => x.ScanInterval = parser.ParseInteger() },
    };

    public ObjectFilter EnemyFilter { get; private set; }
    public int ScanRange { get; private set; }
    public int ScanInterval { get; private set; }
}
