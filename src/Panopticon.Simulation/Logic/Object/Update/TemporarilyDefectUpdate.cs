using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public sealed class TemporarilyDefectUpdateModuleData : UpdateModuleData
{
    internal static TemporarilyDefectUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<TemporarilyDefectUpdateModuleData> FieldParseTable = new IniParseTable<TemporarilyDefectUpdateModuleData>
    {
        { "DefectDuration", (parser, x) => x.DefectDuration = parser.ParseInteger() }
    };

    public int DefectDuration { get; private set; }
}
